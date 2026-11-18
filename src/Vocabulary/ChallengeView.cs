using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vocabulary.Core;
using Windows.System;

namespace Vocabulary;

internal sealed class ChallengeView : StackPanel
{
    private readonly App app;
    private readonly StackPanel stage = UI.Column(14);
    private ChallengeKind? selected;
    private ChallengeSource source;
    private int count = 10;
    private int length = 5;
    private TextBox? input;
    private Button? newRound;
    private Button? matchFocus;
    private bool busy;
    private IReadOnlyList<Word> candidates = [];
    private string saveError = "";
    internal static readonly string[] Names = ["中文默写", "Wordle 猜词", "字母拼词", "中英配对"];
    internal static readonly string[] Sources = ["当前词书全部", "已学词", "生词本", "遗忘词"];
    private static readonly string[] Descriptions = ["看中文，写出英文拼写", "用六次机会找到隐藏的单词", "重新排列字母，拼出正确单词", "为英文单词找到对应的中文释义"];

    internal ChallengeView(App app)
    {
        this.app = app;
        Spacing = 18;
        Loaded += (_, _) => RenderPage();
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshAppearance);
    }

    internal void RefreshAppearance()
    {
        if (XamlRoot is not null && Parent is not null) RenderStage();
    }

    private ChallengeRound? Round => selected is { } kind ? app.Challenges.GetValueOrDefault(kind) : null;
    private ChallengeOptions Options => new(selected!.Value, app.Store.Load().Book, source, count, length);
    private void RenderPage()
    {
        Children.Clear();
        if (selected is null) { Hub(); return; }
        var back = UI.Button("挑战首页", () => { selected = null; RenderPage(); });
        back.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        Children.Add(back);
        Children.Add(UI.Text(Names[(int)selected.Value], 30, true));
        Children.Add(UI.Secondary(Descriptions[(int)selected.Value], 14));
        var config = UI.Column(10);
        var selectors = new Grid { ColumnSpacing = 12 };
        selectors.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        selectors.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var scope = new ComboBox { Header = "选词范围", SelectedIndex = (int)source, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in Sources) scope.Items.Add(name);
        scope.SelectedIndex = (int)source;
        AutomationProperties.SetName(scope, "挑战选词范围");
        AutomationProperties.SetAutomationId(scope, "ChallengeScope");
        selectors.Children.Add(scope);
        var size = new ComboBox { Header = selected == ChallengeKind.Wordle ? "单词长度" : "每轮词数", HorizontalAlignment = HorizontalAlignment.Stretch };
        if (selected == ChallengeKind.Wordle)
        {
            foreach (int n in new[] { 4, 5, 6, 7 }) size.Items.Add($"{n} 字母");
            size.SelectedIndex = length - 4;
        }
        else { size.Items.Add("10 词"); size.Items.Add("20 词"); size.SelectedIndex = count == 10 ? 0 : 1; }
        AutomationProperties.SetName(size, selected == ChallengeKind.Wordle ? "单词长度" : "每轮词数");
        AutomationProperties.SetAutomationId(size, "ChallengeSize");
        Grid.SetColumn(size, 1); selectors.Children.Add(size);
        config.Children.Add(selectors);
        var available = UI.Secondary("");
        var start = UI.Button(Round is { Finished: false } ? "结束当前局并重新开始" : "开始新一轮", Start, Round is null);
        newRound = start;
        void UpdatePool()
        {
            candidates = app.Store.ChallengeCandidates(Options);
            string book = Options.Book == Book.Cet4 ? "四级" : "六级";
            available.Text = $"{book} · 可用 {candidates.Count:N0} 词" + (candidates.Count == 0 ? "，请切换选词范围。" : "");
            start.IsEnabled = candidates.Count > 0;
        }
        scope.SelectionChanged += (_, _) => { if (scope.SelectedIndex >= 0) { source = (ChallengeSource)scope.SelectedIndex; UpdatePool(); } };
        size.SelectionChanged += (_, _) =>
        {
            if (size.SelectedIndex < 0) return;
            if (selected == ChallengeKind.Wordle) length = size.SelectedIndex + 4;
            else count = size.SelectedIndex == 0 ? 10 : 20;
            UpdatePool();
        };
        config.Children.Add(available); config.Children.Add(start);
        UpdatePool();
        if (Round is null) Children.Add(UI.Panel(config, 18));
        else Children.Add(new Expander { Header = "新局设置", Content = config, HorizontalAlignment = HorizontalAlignment.Stretch });
        Children.Add(stage);
        RenderStage();
    }

    private void Hub()
    {
        Children.Add(UI.Text("挑战", 30, true));
        Children.Add(UI.Secondary("换一种方式，记牢单词。随时可以开始下一轮。", 14));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tiles = new List<Button>();
        for (int i = 0; i < Names.Length; i++)
        {
            var kind = (ChallengeKind)i;
            var copy = UI.Column(8);
            copy.Children.Add(UI.Text(Names[i], 22, true));
            copy.Children.Add(UI.Secondary(Descriptions[i], 14));
            if (app.Challenges.GetValueOrDefault(kind) is { Finished: false }) copy.Children.Add(UI.Secondary("有一局可以继续"));
            var tile = UI.Button(Names[i], () => { selected = kind; RenderPage(); });
            tile.Content = copy; tile.Padding = new Thickness(22); tile.HorizontalAlignment = HorizontalAlignment.Stretch;
            tile.HorizontalContentAlignment = HorizontalAlignment.Left; tile.MinHeight = 132;
            AutomationProperties.SetName(tile, Names[i]);
            tiles.Add(tile); grid.Children.Add(tile);
        }
        grid.SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= 560;
            for (int i = 0; i < tiles.Count; i++)
            {
                Grid.SetColumn(tiles[i], wide ? i % 2 : 0); Grid.SetRow(tiles[i], wide ? i / 2 : i); Grid.SetColumnSpan(tiles[i], wide ? 1 : 2);
            }
        };
        Children.Add(grid);
        Children.Add(UI.Secondary("完成一轮后保存成绩。退出软件前，每种玩法都能继续当前局。"));
    }

    private void Start()
    {
        if (Round is { Finished: true, Saved: false } old && !Save(old)) return;
        var pool = app.Store.ChallengeCandidates(Options);
        if (pool.Count == 0) { RenderPage(); return; }
        // Loading here surfaces a missing deployment asset before a Wordle round starts.
        if (selected == ChallengeKind.Wordle) _ = app.GuessWords;
        app.Challenges[selected!.Value] = new(Options, pool);
        RenderPage();
        FocusInput();
    }

    private bool Save(ChallengeRound round)
    {
        try { app.Store.SaveChallenge(round.Result(DateTimeOffset.Now)); round.Saved = true; saveError = ""; return true; }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        { saveError = $"成绩暂未保存，可重试。{e.Message}"; return false; }
    }

    private void RenderStage()
    {
        input = null;
        matchFocus = null;
        stage.Children.Clear();
        var round = Round;
        if (newRound is not null) newRound.Content = round is { Finished: false } ? "结束当前局并重新开始" : "开始新一轮";
        if (round is null) return;
        string book = round.Options.Book == Book.Cet4 ? "四级" : "六级";
        stage.Children.Add(UI.Secondary($"本局 · {book} · {Sources[(int)round.Options.Source]}"));
        if (round.Finished) { Results(round); return; }
        if (round.Options.Kind == ChallengeKind.Matching) { Matching(round); return; }
        if (round.Options.Kind == ChallengeKind.Wordle) { Wordle(round); return; }
        Writing(round);
    }

    private void Act(Action action)
    {
        if (busy) return;
        busy = true;
        try { action(); RenderStage(); }
        finally { busy = false; }
        FocusInput();
    }

    private void FocusInput()
    {
        if (input is { } editor)
            editor.DispatcherQueue.TryEnqueue(() => { if (editor.XamlRoot is not null) { editor.Focus(FocusState.Programmatic); editor.SelectionStart = editor.Text.Length; } });
        else if (matchFocus is { } next)
            next.DispatcherQueue.TryEnqueue(() => { if (next.XamlRoot is not null) next.Focus(FocusState.Programmatic); });
    }

    private TextBox InputBox(ChallengeRound round, int maxLength)
    {
        var editor = new TextBox { Text = round.Input, PlaceholderText = "输入英文单词", FontSize = 24, MaxLength = maxLength,
            IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
        AutomationProperties.SetName(editor, "挑战答案");
        AutomationProperties.SetAutomationId(editor, "ChallengeInput");
        editor.TextChanged += (_, _) => round.Input = editor.Text;
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            if (e.KeyStatus.WasKeyDown) return;
            Act(() => { if (round.Options.Kind == ChallengeKind.Wordle) round.Guess(round.Current.Id, app.GuessWords); else round.Submit(round.Current.Id); });
        };
        input = editor;
        return editor;
    }

    private void Writing(ChallengeRound round)
    {
        var q = round.Current;
        var box = UI.Column(16);
        box.Children.Add(UI.Secondary($"第 {round.Progress + 1} / {round.Questions.Count} 词"));
        var prompt = UI.Text(ChallengeRules.Prompt(q.Word), 21, true);
        AutomationProperties.SetAutomationId(prompt, "ChallengePrompt");
        box.Children.Add(prompt);
        string part = q.Word.PartOfSpeech.Length > 0 ? q.Word.PartOfSpeech + "　·　" : "";
        box.Children.Add(UI.Secondary($"{part}{q.Word.English.Length} 个字母"));
        if (round.CurrentAnswer is { } result)
        {
            box.Children.Add(UI.Text(round.Feedback, 18, true));
            box.Children.Add(UI.Secondary($"你的答案　{(result.Input.Length == 0 ? "（未填写）" : result.Input)}"));
            box.Children.Add(UI.Text(q.Word.English, 32, true));
            AddDefinition(box, q.Word);
            var next = UI.Button(round.Progress + 1 == round.Questions.Count ? "查看本轮成绩" : "下一题", () => Act(() => round.Continue(q.Id)), true);
            next.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && e.KeyStatus.WasKeyDown) e.Handled = true; };
            box.Children.Add(next);
            next.Loaded += (_, _) => next.Focus(FocusState.Programmatic);
        }
        else
        {
            var editor = InputBox(round, q.Word.English.Length + 8);
            box.Children.Add(editor);
            if (round.Options.Kind == ChallengeKind.Scramble)
            {
                var letters = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 42, ItemHeight = 44, MaximumRowsOrColumns = 8 };
                var used = new List<char>(ChallengeRules.Normalize(round.Input));
                foreach (char letter in q.Letters)
                {
                    bool taken = used.Remove(letter);
                    var key = UI.Button(letter.ToString().ToUpperInvariant(), () => Act(() => round.Input += letter));
                    key.IsEnabled = !taken; key.Margin = new Thickness(2); key.HorizontalAlignment = HorizontalAlignment.Stretch;
                    AutomationProperties.SetName(key, $"字母 {char.ToUpperInvariant(letter)}"); letters.Children.Add(key);
                }
                // Rebuild the letter availability after typing, without replacing the text editor.
                editor.TextChanged += (_, _) =>
                {
                    var consumed = new List<char>(ChallengeRules.Normalize(editor.Text));
                    int at = 0;
                    foreach (Button key in letters.Children) key.IsEnabled = !consumed.Remove(q.Letters[at++]);
                };
                box.Children.Add(letters);
                var edit = UI.Row();
                edit.Children.Add(UI.Button("退格", () => Act(() => { if (round.Input.Length > 0) round.Input = round.Input[..^1]; })));
                edit.Children.Add(UI.Button("清空", () => Act(() => round.Input = "")));
                box.Children.Add(edit);
            }
            var operations = UI.Row(8);
            operations.Children.Add(UI.Button("提交答案", () => Act(() => round.Submit(q.Id)), true));
            operations.Children.Add(UI.Button(round.Options.Kind == ChallengeKind.Dictation ? "提示首字母" : "提示一个字母", () => Act(() => round.Hint(q.Id))));
            operations.Children.Add(UI.Button("查看答案", () => Act(() => round.Reveal(q.Id))));
            operations.Children.Add(UI.Button("跳过", () => Act(() => round.Reveal(q.Id))));
            // Wrap at small window widths using two rows of native buttons.
            var operationGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (int i = 0; i < 2; i++) { operationGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); operationGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            var operationButtons = operations.Children.Cast<Button>().ToArray(); operations.Children.Clear();
            for (int i = 0; i < operationButtons.Length; i++)
            {
                Grid.SetColumn(operationButtons[i], i % 2); Grid.SetRow(operationButtons[i], i / 2);
                operationButtons[i].HorizontalAlignment = HorizontalAlignment.Stretch; operationGrid.Children.Add(operationButtons[i]);
            }
            box.Children.Add(operationGrid);
            box.Children.Add(UI.Secondary(round.Feedback));
        }
        stage.Children.Add(UI.Panel(box));
    }

    private void AddDefinition(StackPanel panel, Word word)
    {
        panel.Children.Add(UI.Secondary(word.Phonetic.Length == 0 ? "暂无音标" : $"/{word.Phonetic}/"));
        panel.Children.Add(UI.Text(word.Meaning, 16));
        string note = app.Store.GetEntry(word.Id).Note;
        if (note.Length > 0) panel.Children.Add(UI.Secondary($"我的笔记　{note}"));
        panel.Children.Add(new BookmarkButton(app, word.Id, true));
    }

    private void Wordle(ChallengeRound round)
    {
        var box = UI.Column(12);
        box.Children.Add(UI.Secondary($"{round.Options.Length} 字母 · 六次机会"));
        var boardSide = UI.Column(10);
        boardSide.Children.Add(WordleBoard(round));
        boardSide.Children.Add(UI.Secondary("✓ 位置正确　↔ 字母存在、位置不同　× 无剩余匹配字母"));
        var playSide = UI.Column(12);
        var editor = InputBox(round, round.Options.Length);
        playSide.Children.Add(editor);
        var row = UI.Row();
        row.Children.Add(UI.Button("猜一次", () => Act(() => round.Guess(round.Current.Id, app.GuessWords)), true));
        row.Children.Add(UI.Button("揭晓答案", () => Act(() => round.Reveal(round.Current.Id))));
        playSide.Children.Add(row);
        playSide.Children.Add(UI.Text(round.Feedback));
        var keyboard = UI.Column(4);
        var marks = round.KeyboardMarks();
        foreach (string keys in new[] { "qwertyuiop", "asdfghjkl", "zxcvbnm" })
        {
            var line = new Grid { ColumnSpacing = 3 };
            foreach (char letter in keys)
            {
                var key = UI.Button(letter.ToString().ToUpperInvariant(), () =>
                {
                    if (round.Input.Length < round.Options.Length) editor.Text = round.Input + letter;
                    FocusInput();
                });
                key.MinWidth = 0; key.Padding = new Thickness(0); key.FontSize = 15;
                key.HorizontalAlignment = HorizontalAlignment.Stretch;
                if (marks.TryGetValue(letter, out var mark)) { key.Content = $"{char.ToUpperInvariant(letter)}{Symbol(mark)}"; ApplyMark(key, mark); }
                AutomationProperties.SetName(key, $"字母 {char.ToUpperInvariant(letter)}" + (marks.TryGetValue(letter, out var m) ? $"，{MarkName(m)}" : ""));
                Grid.SetColumn(key, line.ColumnDefinitions.Count); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.Children.Add(key);
            }
            keyboard.Children.Add(line);
        }
        var controls = UI.Row();
        controls.Children.Add(UI.Button("退格", () => { if (editor.Text.Length > 0) editor.Text = editor.Text[..^1]; FocusInput(); }));
        controls.Children.Add(UI.Button("清空", () => { editor.Text = ""; FocusInput(); }));
        keyboard.Children.Add(controls);
        playSide.Children.Add(keyboard);
        var pair = new Grid { ColumnSpacing = 18, RowSpacing = 14 };
        pair.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        pair.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        pair.RowDefinitions.Add(new() { Height = GridLength.Auto });
        pair.RowDefinitions.Add(new() { Height = GridLength.Auto });
        pair.Children.Add(boardSide); pair.Children.Add(playSide);
        pair.SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= 560;
            Grid.SetColumnSpan(boardSide, wide ? 1 : 2);
            Grid.SetColumn(playSide, wide ? 1 : 0); Grid.SetColumnSpan(playSide, wide ? 1 : 2); Grid.SetRow(playSide, wide ? 0 : 1);
        };
        box.Children.Add(pair);
        stage.Children.Add(UI.Panel(box));
    }

    internal static UIElement WordleBoard(ChallengeRound round)
    {
        var board = new Grid { ColumnSpacing = 5, RowSpacing = 5, MaxWidth = 430, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int c = 0; c < round.Options.Length; c++) board.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (int r = 0; r < 6; r++)
        {
            board.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int c = 0; c < round.Options.Length; c++)
            {
                bool played = r < round.Guesses.Count;
                var label = UI.Text(played ? $"{char.ToUpperInvariant(round.Guesses[r].Text[c])} {Symbol(round.Guesses[r].Marks[c])}" : "", 19, true);
                label.TextAlignment = TextAlignment.Center;
                var tile = UI.Panel(label, 4); tile.MinHeight = 42;
                if (played) ApplyMark(tile, round.Guesses[r].Marks[c]);
                AutomationProperties.SetName(tile, played ? $"第 {r + 1} 行，第 {c + 1} 列，{round.Guesses[r].Text[c]}，{MarkName(round.Guesses[r].Marks[c])}" : $"第 {r + 1} 行，第 {c + 1} 列，待猜测");
                Grid.SetRow(tile, r); Grid.SetColumn(tile, c); board.Children.Add(tile);
            }
        }
        return board;
    }

    private static string Symbol(LetterMark mark) => mark switch { LetterMark.Exact => "✓", LetterMark.Present => "↔", _ => "×" };
    private static string MarkName(LetterMark mark) => mark switch { LetterMark.Exact => "位置正确", LetterMark.Present => "字母存在、位置不同", _ => "无剩余匹配字母" };
    private static void ApplyMark(Control element, LetterMark mark)
    {
        if (mark == LetterMark.Absent) return;
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return;
        element.Background = new SolidColorBrush(mark == LetterMark.Exact ? Windows.UI.Color.FromArgb(255, 36, 99, 68) : Windows.UI.Color.FromArgb(255, 120, 83, 11));
        element.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
    }
    private static void ApplyMark(Border element, LetterMark mark)
    {
        if (mark == LetterMark.Absent || new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return;
        element.Background = new SolidColorBrush(mark == LetterMark.Exact ? Windows.UI.Color.FromArgb(255, 36, 99, 68) : Windows.UI.Color.FromArgb(255, 120, 83, 11));
        ((TextBlock)element.Child).Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    private void Matching(ChallengeRound round)
    {
        var box = UI.Column(14);
        box.Children.Add(UI.Secondary($"已配对 {round.Answers.Count} / {round.Questions.Count} 词 · 先选英文，再选中文"));
        var instruction = UI.Text(round.Feedback);
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (int i = 0; i < round.MatchCount; i++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            int left = round.MatchStart + i, right = round.MatchingOrder[round.MatchStart + i], page = round.MatchPage;
            bool matched = round.IsMatched(left);
            var english = UI.Button((matched ? "✓ " : "") + round.Questions[left].Word.English, () => Act(() => round.SelectedMatch = left), round.SelectedMatch == left);
            english.IsEnabled = !matched; english.HorizontalAlignment = HorizontalAlignment.Stretch; english.MinHeight = 62;
            var meaning = UI.Button("", () =>
            {
                if (round.SelectedMatch is { } chosen) Act(() => round.Match(page, chosen, right));
                else instruction.Text = "请先选择左侧的英文单词。";
            });
            var text = UI.Text((round.IsMatched(right) ? "✓ " : "") + ChallengeRules.MatchPrompt(round.Questions[right].Word), 14);
            meaning.Content = text; meaning.HorizontalAlignment = HorizontalAlignment.Stretch;
            meaning.HorizontalContentAlignment = HorizontalAlignment.Left; meaning.IsEnabled = !round.IsMatched(right);
            if (matchFocus is null) matchFocus = round.SelectedMatch.HasValue ? meaning.IsEnabled ? meaning : null : english.IsEnabled ? english : null;
            AutomationProperties.SetName(meaning, $"释义 {i + 1}，{text.Text}");
            AutomationProperties.SetAutomationId(english, $"MatchEnglish{left}");
            AutomationProperties.SetAutomationId(meaning, $"MatchMeaning{right}");
            Grid.SetRow(english, i); Grid.SetRow(meaning, i); Grid.SetColumn(meaning, 1); grid.Children.Add(english); grid.Children.Add(meaning);
        }
        box.Children.Add(grid);
        box.Children.Add(instruction);
        if (round.GroupFinished)
        {
            int page = round.MatchPage;
            matchFocus = UI.Button("下一组", () => Act(() => round.NextGroup(page)), true);
            box.Children.Add(matchFocus);
        }
        stage.Children.Add(UI.Panel(box));
    }

    private void Results(ChallengeRound round)
    {
        if (!round.Saved) Save(round);
        var result = round.Result(DateTimeOffset.Now);
        var box = UI.Column(14);
        box.Children.Add(UI.Text(round.Options.Kind == ChallengeKind.Wordle ? round.Feedback : "本轮挑战完成", 24, true));
        if (round.Options.Kind == ChallengeKind.Wordle)
        {
            box.Children.Add(WordleBoard(round));
            box.Children.Add(UI.Text(round.Current.Word.English, 30, true));
            AddDefinition(box, round.Current.Word);
        }
        else
        {
            box.Children.Add(UI.Text($"独立答对 {result.Correct} / {result.Answers.Count}　·　{result.Accuracy:P0}", 22, true));
            box.Children.Add(UI.Secondary($"提示后完成 {result.Assisted} 词　·　使用提示 {result.Answers.Count(a => a.Hinted)} 词"));
        }
        box.Children.Add(UI.Secondary(round.Saved ? "成绩已保存" : saveError));
        if (!round.Saved) box.Children.Add(UI.Button("重试保存成绩", () => { Save(round); RenderStage(); }));
        box.Children.Add(UI.Button("再来一轮", Start, true));
        var wrong = result.Answers.Where(a => a.Outcome != AnswerOutcome.Correct).Select(a => a.Word).ToArray();
        if (wrong.Length > 0 && round.Options.Kind != ChallengeKind.Wordle)
            box.Children.Add(UI.Button("重练未独立答对的词", () =>
            {
                if (!round.Saved && !Save(round)) return;
                app.Challenges[round.Options.Kind] = new(round.Options, wrong); RenderPage(); FocusInput();
            }));
        stage.Children.Add(UI.Panel(box));
        if (round.Options.Kind != ChallengeKind.Wordle)
            foreach (var answer in result.Answers) stage.Children.Add(AnswerRow(app, answer, XamlRoot));
    }

    internal static UIElement AnswerRow(App app, ChallengeAnswer answer, XamlRoot root)
    {
        var entry = UI.Column(6);
        string label = answer.Outcome switch { AnswerOutcome.Correct => "✓ 独立答对", AnswerOutcome.Assisted => "提示完成", _ => "未独立答对" };
        var title = new Grid();
        title.Children.Add(UI.Text(answer.Word.English, 19, true));
        var star = new BookmarkButton(app, answer.Word.Id); star.HorizontalAlignment = HorizontalAlignment.Right; title.Children.Add(star);
        entry.Children.Add(title); entry.Children.Add(UI.Secondary(label));
        if (answer.Input.Length > 0) entry.Children.Add(UI.Secondary($"作答　{answer.Input}"));
        entry.Children.Add(UI.Text(answer.Word.Meaning));
        var detail = UI.Button("查看词条", () => { _ = WordDetail.ShowAsync(app, answer.Word, root); });
        AutomationProperties.SetName(detail, $"查看词条 {answer.Word.English}"); entry.Children.Add(detail);
        return UI.Panel(entry, 16);
    }
}
