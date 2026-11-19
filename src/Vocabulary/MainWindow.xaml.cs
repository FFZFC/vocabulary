using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vocabulary.Core;
using Windows.System;
using WinRT.Interop;

namespace Vocabulary;

public sealed partial class MainWindow : Window
{
    private readonly App app;
    private string page = "today";
    private bool studying;
    private ToggleSwitch? widgetSwitch;
    private Action? updateToday;
    private bool starting;
    private Action? refreshLibrary;
    private int chartDays = 7;
    private ChallengeView? challenges;
    private bool challengeStatistics;
    internal MainWindow(App app)
    {
        this.app = app;
        InitializeComponent();
        AppWindow.SetIcon(App.IconPath);
        SystemBackdrop = new MicaBackdrop();
        double scale = Math.Max(1, Native.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96d);
        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        int width = Math.Min(area.Width, (int)(1000 * scale)), height = Math.Min(area.Height, (int)(740 * scale));
        AppWindow.MoveAndResize(new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        Nav.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is NavigationViewItem item) { page = (string)item.Tag; ShowPage(); }
        };
        Nav.SelectedItem = Nav.MenuItems[0];
        void SyncCaption()
        {
            int dark = Nav.ActualTheme == ElementTheme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), 20, ref dark, sizeof(int));
        }
        Nav.ActualThemeChanged += (_, _) => SyncCaption();
        Nav.Loaded += (_, _) => SyncCaption();
        Nav.Loaded += Startup;
        AppWindow.Closing += (_, e) => { if (!app.Exiting) { e.Cancel = true; AppWindow.Hide(); } };
        app.Session.Changed += () => updateToday?.Invoke();
        app.Session.RecordsChanged += () =>
        {
            if (page == "history") ShowPage();
            refreshLibrary?.Invoke();
        };
        app.Store.BookmarkChanged += (_, _) => refreshLibrary?.Invoke();
        app.Store.NoteChanged += _ => refreshLibrary?.Invoke();
    }
    private async void Startup(object sender, RoutedEventArgs args)
    {
        if (starting) return;
        starting = true;
        var p = app.Store.Load();
        if (!p.Configured)
        {
            var book = UI.Books(p.Book); var goal = UI.Goal(p.DailyGoal);
            var form = UI.Column(18); form.Children.Add(book); form.Children.Add(goal);
            var dialog = new ContentDialog { Title = "设置学习计划", Content = form, PrimaryButtonText = "开始使用", DefaultButton = ContentDialogButton.Primary, XamlRoot = Nav.XamlRoot };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) { app.Quit(); return; }
            app.Store.Save(new((Book)book.SelectedIndex, UI.ReadGoal(goal, 20), true, true));
        }
        app.Begin(); ShowPage();
    }
    internal void SetDesktopStatus(bool attached) => ShellStatus.IsOpen = !attached;
    internal void RefreshAppearance() => challenges?.RefreshAppearance();
    internal void UpdateToggle() { if (widgetSwitch != null) widgetSwitch.IsOn = app.Store.Load().WidgetVisible; }
    private void ShowPage()
    {
        Body.Children.Clear(); PageScroll.ChangeView(null, 0, null, true);
        updateToday = null; widgetSwitch = null; refreshLibrary = null;
        switch (page)
        {
            case "today": Today(); break;
            case "challenges": challenges ??= new ChallengeView(app); Body.Children.Add(challenges); break;
            case "library": Library(LearnedFilter.All); break;
            case "forgotten": Library(LearnedFilter.Forgotten); break;
            case "notebook": Library(LearnedFilter.Notebook); break;
            case "history": History(); break;
            case "settings": Settings(); break;
        }
    }
    private void Heading(string title, string subtitle)
    {
        var h = UI.Column(4); h.Children.Add(UI.Text(title, 30, true)); h.Children.Add(UI.Secondary(subtitle, 14)); Body.Children.Add(h);
    }
    private static Grid Metrics(params (string label, TextBlock value)[] items)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var col = UI.Column(6); col.Children.Add(UI.Secondary(items[i].label)); col.Children.Add(items[i].value);
            var tile = UI.Panel(col, 14); Grid.SetColumn(tile, i); grid.Children.Add(tile);
        }
        return grid;
    }
    private void Today()
    {
        var p = app.Store.Load();
        string book = p.Book == Book.Cet4 ? "大学英语四级" : "大学英语六级";
        Heading("今日学习", DateTime.Today.ToString("M月d日  dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN")));
        var fresh = UI.Text("", 26, true); var due = UI.Text("", 26, true); var streak = UI.Text("", 26, true);
        var metrics = Metrics(("今日新学", fresh), ("待复习", due), ("连续学习", streak));
        var col = UI.Column(12);
        var detail = UI.Secondary("");
        var studyBook = UI.Secondary(book);
        ProgressRing? ring = null;
        TextBlock? pct = null;
        if (studying)
        {
            var toolbar = new Grid();
            toolbar.Children.Add(studyBook);
            var end = UI.Button("返回概览", () => { studying = false; if (app.Session.Mode != StudyMode.Daily) app.Session.StartRound(); ShowPage(); });
            end.HorizontalAlignment = HorizontalAlignment.Right; toolbar.Children.Add(end); col.Children.Add(toolbar);
            col.Children.Add(new StudyCard(app, false));
        }
        else
        {
            var hero = new Grid { ColumnSpacing = 20 };
            hero.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var copy = UI.Column(6);
            copy.Children.Add(UI.Secondary("当前词书"));
            copy.Children.Add(UI.Text(book, 32, true));
            copy.Children.Add(detail);
            hero.Children.Add(copy);
            ring = new ProgressRing { IsIndeterminate = false, Value = 0, Width = 68, Height = 68 };
            AutomationProperties.SetName(ring, "词书完成度");
            pct = UI.Text("", 14, true); pct.HorizontalAlignment = HorizontalAlignment.Center;
            var indicator = new Grid(); indicator.Children.Add(ring); indicator.Children.Add(pct);
            Grid.SetColumn(indicator, 1); hero.Children.Add(indicator);
            col.Children.Add(hero);
            var go = UI.Button("开始学习", () => { studying = true; ShowPage(); }, true);
            go.Padding = new Thickness(28, 9, 28, 9);
            col.Children.Add(go);
        }
        Body.Children.Add(UI.Panel(col, 24));
        Body.Children.Add(metrics);
        var goalStatus = UI.Secondary("今日目标已达成，可继续学习");
        Body.Children.Add(goalStatus);
        var done = UI.Secondary(""); Body.Children.Add(done);
        updateToday = () =>
        {
            var d = app.Store.Summary(p, DateTimeOffset.Now);
            studyBook.Text = app.Session.Mode switch
            {
                StudyMode.Notebook => $"{book}　·　生词本",
                StudyMode.Forgotten => $"{book}　·　遗忘词复习",
                _ => book
            };
            fresh.Text = $"{d.NewToday} / {p.DailyGoal}"; due.Text = d.Due.ToString(); streak.Text = $"{d.Streak} 天";
            goalStatus.Visibility = d.GoalReached ? Visibility.Visible : Visibility.Collapsed;
            detail.Text = $"已学 {d.Learned:N0} / {d.Total:N0} 词"; if (ring is not null && pct is not null)
            {
                ring.Value = 100d * d.Learned / d.Total;
                pct.Text = $"{ring.Value:0}%";
            }
            done.Text = $"今日已学 {d.NewToday} 个新词，复习 {d.ReviewToday} 个单词。";
        };
        updateToday();
    }
    private void Library(LearnedFilter collection)
    {
        bool notebook = collection == LearnedFilter.Notebook;
        bool allWords = collection == LearnedFilter.All;
        var selectedBook = app.Store.Load().Book;
        string bookName = selectedBook == Book.Cet4 ? "大学英语四级" : "大学英语六级";
        if (notebook) Heading("生词本", $"{bookName}　·　按收藏时间排列");
        else if (allWords) Heading("词库", "搜索英文单词或中文释义");
        else Heading("遗忘词", "当前词书中最近一次标记为不记得的单词");
        Button? practice = null;
        var collectionSize = UI.Text("", 24, true);
        if (!allWords)
        {
            practice = UI.Button(notebook ? "练习生词本" : "开始专项复习", () =>
            {
                app.Session.StartRound(notebook ? StudyMode.Notebook : StudyMode.Forgotten);
                studying = true;
                Nav.SelectedItem = Nav.MenuItems[0];
            }, true);
            if (notebook)
            {
                var bar = new Grid { ColumnSpacing = 12 };
                bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                bar.Children.Add(collectionSize);
                Grid.SetColumn(practice, 1);
                bar.Children.Add(practice);
                Body.Children.Add(UI.Panel(bar, 18));
            }
            else Body.Children.Add(practice);
        }
        string placeholder = notebook ? "搜索单词、释义或笔记" : "搜索单词或释义";
        var search = new TextBox { PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(search, placeholder); Body.Children.Add(search);
        var filters = UI.Row();
        var book = UI.Books(app.Store.Load().Book); book.Header = null;
        AutomationProperties.SetName(book, "筛选词书");
        var filter = new ComboBox { Items = { "全部状态", "未学", "已学" }, SelectedIndex = 0 };
        AutomationProperties.SetName(filter, "学习状态");
        filters.Children.Add(book); filters.Children.Add(filter); if (allWords) Body.Children.Add(filters);
        var results = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
        results.ContainerContentChanging += (_, e) =>
        {
            if (!e.InRecycleQueue && e.Item is StackPanel { Tag: Word word })
                AutomationProperties.SetName(e.ItemContainer, $"查看词条 {word.English}");
        };
        var count = UI.Secondary(""); Body.Children.Add(count);
        var listPanel = UI.Panel(results, 6);
        Body.Children.Add(listPanel);
        int offset = 0; const int size = 30;
        Button? prev = null, next = null;
        var pages = UI.Row();
        void Refresh()
        {
            var whichBook = (Book)book.SelectedIndex;
            var whichList = allWords ? (LearnedFilter)filter.SelectedIndex : collection;
            var rows = app.Store.SearchEntries(whichBook, search.Text, whichList, offset, size + 1);
            while (offset > 0 && rows.Count == 0)
            {
                offset = Math.Max(0, offset - size);
                rows = app.Store.SearchEntries(whichBook, search.Text, whichList, offset, size + 1);
            }
            int saved = notebook ? app.Store.BookmarkCount(whichBook) : 0;
            if (practice is not null)
                practice.IsEnabled = notebook ? saved > 0 : app.Store.Search(whichBook, "", collection, 0, 1).Count > 0;
            collectionSize.Text = $"已收藏 {saved:N0} 词";
            results.Items.Clear();
            foreach (var row in rows.Take(size))
            {
                var word = row.Word;
                var entry = UI.Column(3); entry.Tag = word; entry.Padding = new Thickness(4, 6, 4, 6);
                var heading = UI.Row(8);
                heading.Children.Add(UI.Text(word.English, 17, true));
                if (!notebook && row.Entry.Saved) heading.Children.Add(new FontIcon { Glyph = "\uE735", FontSize = 12 });
                entry.Children.Add(heading);
                var line = UI.Secondary(word.Meaning.Replace('\n', ' ')); line.MaxLines = 1; line.TextTrimming = TextTrimming.CharacterEllipsis;
                entry.Children.Add(line);
                if (notebook && row.Entry.Note.Length > 0)
                {
                    var note = UI.Secondary($"笔记　{row.Entry.Note}");
                    note.MaxLines = 1; note.TextTrimming = TextTrimming.CharacterEllipsis;
                    entry.Children.Add(note);
                }
                AutomationProperties.SetName(entry, word.English);
                results.Items.Add(entry);
            }
            count.Text = notebook && saved == 0 ? "遇到想再看的词，点击词卡上的星标即可收藏。" :
                rows.Count == 0 ? "没有匹配的词条" : $"第 {offset + 1}–{offset + Math.Min(size, rows.Count)} 条";
            count.Visibility = notebook && saved > 0 && saved <= size && string.IsNullOrWhiteSpace(search.Text) ? Visibility.Collapsed : Visibility.Visible;
            listPanel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            prev!.IsEnabled = offset > 0; next!.IsEnabled = rows.Count > size;
            pages.Visibility = prev.IsEnabled || next.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
        prev = UI.Button("上一页", () => { offset = Math.Max(0, offset - size); Refresh(); });
        next = UI.Button("下一页", () => { offset += size; Refresh(); });
        pages.Children.Add(prev); pages.Children.Add(next); Body.Children.Add(pages);
        search.TextChanged += (_, _) => { offset = 0; Refresh(); };
        book.SelectionChanged += (_, _) => { offset = 0; Refresh(); }; filter.SelectionChanged += (_, _) => { offset = 0; Refresh(); };
        results.ItemClick += async (_, e) =>
        {
            var word = (Word)((StackPanel)e.ClickedItem).Tag;
            await WordDetail.ShowAsync(app, word, Nav.XamlRoot);
        };
        refreshLibrary = Refresh;
        Refresh();
    }
    private void History()
    {
        var titleBar = new Grid();
        titleBar.Children.Add(UI.Text("学习统计", 30, true));
        Body.Children.Add(titleBar);
        var periods = new ComboBox { Items = { "最近 7 天", "最近 30 天" }, SelectedIndex = chartDays == 7 ? 0 : 1 };
        AutomationProperties.SetName(periods, "统计周期");
        periods.SelectionChanged += (_, _) => { chartDays = periods.SelectedIndex == 0 ? 7 : 30; ShowPage(); };
        periods.HorizontalAlignment = HorizontalAlignment.Right;
        titleBar.Children.Add(periods);
        var sections = UI.Row();
        sections.Children.Add(UI.Button("学习概况", () => { challengeStatistics = false; ShowPage(); }, !challengeStatistics));
        sections.Children.Add(UI.Button("挑战成绩", () => { challengeStatistics = true; ShowPage(); }, challengeStatistics));
        Body.Children.Add(sections);
        if (challengeStatistics)
        {
            periods.Visibility = Visibility.Collapsed;
            Body.Children.Add(ChallengeHistoryView.Create(app, Nav.XamlRoot));
            return;
        }
        var history = app.Store.Statistics(DateOnly.FromDateTime(DateTime.Now), 84);
        var selected = history.TakeLast(chartDays).ToArray();
        int attempts = selected.Sum(d => d.Attempts);
        string recall = attempts == 0 ? "—" : $"{(double)selected.Sum(d => d.Remembered) / attempts:P0}";
        Body.Children.Add(Metrics(("新学单词", UI.Text(selected.Sum(d => d.NewWords).ToString(), 24, true)),
            ("复习词数", UI.Text(selected.Sum(d => d.Reviewed).ToString(), 24, true)), ("记得率", UI.Text(recall, 24, true))));
        var pair = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        pair.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        pair.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        pair.RowDefinitions.Add(new() { Height = GridLength.Auto });
        pair.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var volume = UI.Panel(StudyCharts.Trend(selected, false), 16);
        var recallChart = UI.Panel(StudyCharts.Trend(selected, true), 16);
        pair.Children.Add(volume); pair.Children.Add(recallChart);
        pair.SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= 620;
            Grid.SetColumnSpan(volume, wide ? 1 : 2);
            Grid.SetColumn(recallChart, wide ? 1 : 0);
            Grid.SetColumnSpan(recallChart, wide ? 1 : 2);
            Grid.SetRow(recallChart, wide ? 0 : 1);
        };
        Body.Children.Add(pair);
        Body.Children.Add(UI.Panel(StudyCharts.Heatmap(history)));
        var daily = UI.Column(8);
        foreach (var d in selected.Reverse()) daily.Children.Add(UI.Text(StudyCharts.Description(d)));
        Body.Children.Add(new Expander { Header = "每日明细", Content = daily, HorizontalAlignment = HorizontalAlignment.Stretch });
    }
    private void Settings()
    {
        Heading("设置", "学习计划与桌面卡片");
        var p = app.Store.Load(); var col = UI.Column(14);
        var book = UI.Books(p.Book); var goal = UI.Goal(p.DailyGoal);
        col.Children.Add(book); col.Children.Add(goal);
        col.Children.Add(UI.Secondary("每日目标用于进度提示，达到后可继续学习。"));
        var saved = UI.Secondary("");
        col.Children.Add(UI.Button("保存学习计划", () =>
        {
            app.UpdateLearningPlan(app.Store.Load() with { Book = (Book)book.SelectedIndex, DailyGoal = UI.ReadGoal(goal, p.DailyGoal) });
            saved.Text = "学习计划已保存";
        }, true));
        col.Children.Add(saved); Body.Children.Add(UI.Panel(col));
        widgetSwitch = new ToggleSwitch { Header = "显示桌面卡片", IsOn = p.WidgetVisible, OnContent = "开启", OffContent = "关闭" };
        widgetSwitch.Toggled += (_, _) => { if (widgetSwitch != null && widgetSwitch.IsOn != app.Store.Load().WidgetVisible) app.ToggleWidget(); };
        var appearance = UI.Column(12);
        appearance.Children.Add(widgetSwitch);
        var density = new Slider { Header = "毛玻璃浓度", Minimum = 0, Maximum = 100, StepFrequency = 1, Value = app.GlassDensity };
        density.ValueChanged += (_, e) => app.SetDensity((int)e.NewValue);
        appearance.Children.Add(density);
        Body.Children.Add(UI.Panel(appearance));
        var info = UI.Column(5); info.Children.Add(UI.Text("离线词库", 16, true));
        var cet4 = app.Store.Summary(p with { Book = Book.Cet4 }, DateTimeOffset.Now);
        var cet6 = app.Store.Summary(p with { Book = Book.Cet6 }, DateTimeOffset.Now);
        info.Children.Add(UI.Secondary($"ECDICT　·　四级 {cet4.Total:N0} 词　·　六级含四级基础 {cet6.Total:N0} 词"));
        Body.Children.Add(UI.Panel(info));
        var about = UI.Column(5);
        about.Children.Add(UI.Text("关于 LexiPane", 16, true));
        var version = typeof(App).Assembly.GetName().Version!;
        about.Children.Add(UI.Secondary($"{version.ToString(3)} · Windows 原生离线词汇学习"));
        Body.Children.Add(UI.Panel(about));
    }
}
