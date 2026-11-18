using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vocabulary.Core;

namespace Vocabulary;

internal static class ChallengeHistoryView
{
    internal static UIElement Create(App app, XamlRoot root)
    {
        var content = UI.Column(14);
        var period = new ComboBox { Items = { "最近 7 天", "最近 30 天" }, SelectedIndex = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(period, "挑战统计周期");
        content.Children.Add(period);
        var metrics = UI.Column(10);
        content.Children.Add(metrics);
        content.Children.Add(UI.Text("最近 20 局", 18, true));
        var recent = UI.Column(8); content.Children.Add(recent);
        void Refresh()
        {
            metrics.Children.Clear(); recent.Children.Clear();
            var end = DateOnly.FromDateTime(DateTime.Now);
            var history = app.Store.ChallengeHistory(end.AddDays(period.SelectedIndex == 0 ? -6 : -29), end);
            foreach (var kind in Enum.GetValues<ChallengeKind>())
            {
                var rounds = history.Where(r => r.Options.Kind == kind).ToArray();
                var answers = rounds.SelectMany(r => r.Answers).ToArray();
                string performance = "暂无成绩";
                if (answers.Length > 0)
                {
                    int correct = answers.Count(a => a.Outcome == AnswerOutcome.Correct);
                    double rate = (double)correct / answers.Length;
                    if (kind == ChallengeKind.Wordle)
                    {
                        var wins = answers.Where(a => a.Outcome == AnswerOutcome.Correct).ToArray();
                        performance = $"胜率 {rate:P0}" + (wins.Length == 0 ? "" : $"　·　获胜平均 {wins.Average(a => a.Attempts):0.0} 次");
                    }
                    else performance = $"独立答对 {correct} / {answers.Length}　·　{rate:P0}";
                }
                var row = UI.Column(4);
                row.Children.Add(UI.Text($"{ChallengeView.Names[(int)kind]}　·　{rounds.Length} 局", 16, true));
                row.Children.Add(UI.Secondary(performance));
                metrics.Children.Add(UI.Panel(row, 14));
            }
            var latest = app.Store.ChallengeHistory(limit: 20);
            if (latest.Count == 0) recent.Children.Add(UI.Secondary("完成挑战后，这里会出现成绩。"));
            foreach (var record in latest)
            {
                string book = record.Options.Book == Book.Cet4 ? "四级" : "六级";
                var button = UI.Button($"{record.Completed.LocalDateTime:MM/dd HH:mm}　{ChallengeView.Names[(int)record.Options.Kind]}　{book}　{record.Correct}/{record.Answers.Count}", () => { _ = ShowResult(app, record, root); });
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                recent.Children.Add(button);
            }
        }
        period.SelectionChanged += (_, _) => Refresh();
        Refresh();
        return content;
    }

    private static async Task ShowResult(App app, ChallengeResult result, XamlRoot root)
    {
        var detail = UI.Column(12);
        detail.Children.Add(UI.Secondary($"{result.Completed.LocalDateTime:yyyy/MM/dd HH:mm}　·　{ChallengeView.Sources[(int)result.Options.Source]}"));
        foreach (var answer in result.Answers)
        {
            var row = UI.Column(6);
            row.Children.Add(UI.Text(answer.Word.English, 23, true));
            row.Children.Add(UI.Text(answer.Outcome switch { AnswerOutcome.Correct => "✓ 独立答对", AnswerOutcome.Assisted => "提示完成", _ => "未独立答对" }));
            row.Children.Add(UI.Secondary($"作答　{(answer.Input.Length == 0 ? "（未填写）" : answer.Input)}"));
            row.Children.Add(UI.Text(answer.Word.Meaning));
            row.Children.Add(new BookmarkButton(app, answer.Word.Id, true));
            if (answer.Guesses is { } guesses)
                foreach (var guess in guesses) row.Children.Add(UI.Secondary($"{guess.Text.ToUpperInvariant()}　{string.Join(" ", guess.Marks.Select(m => m switch { LetterMark.Exact => "✓", LetterMark.Present => "↔", _ => "×" }))}"));
            detail.Children.Add(UI.Panel(row, 12));
        }
        await new ContentDialog { Title = ChallengeView.Names[(int)result.Options.Kind], Content = new ScrollViewer { Content = detail }, CloseButtonText = "关闭", XamlRoot = root }.ShowAsync();
    }
}
