using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Vocabulary.Core;

namespace Vocabulary;

internal static class WordDetail
{
    internal static async Task ShowAsync(App app, Word word, XamlRoot root)
    {
        var content = UI.Column(16);
        var pronunciation = UI.Column(4);
        pronunciation.Children.Add(UI.Secondary(word.Phonetic.Length == 0 ? "暂无音标" : $"/{word.Phonetic}/", 16));
        if (word.PartOfSpeech.Length > 0) pronunciation.Children.Add(UI.Secondary(word.PartOfSpeech));
        content.Children.Add(pronunciation);
        var meaning = UI.Text(word.Meaning, 16);
        meaning.IsTextSelectionEnabled = true;
        content.Children.Add(meaning);
        content.Children.Add(new BookmarkButton(app, word.Id, showCaption: true));

        string original = app.Store.GetEntry(word.Id).Note;
        var editor = new TextBox
        {
            Header = "我的笔记", Text = original, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 76, MaxHeight = 150, PlaceholderText = "写下联想、易混词或记忆提示"
        };
        AutomationProperties.SetAutomationId(editor, "WordNote");
        content.Children.Add(editor);
        var history = UI.Column(8);
        var schedule = UI.Secondary("");
        var recent = new Expander { HorizontalAlignment = HorizontalAlignment.Stretch, Content = history };
        content.Children.Add(schedule);
        content.Children.Add(recent);
        void RefreshHistory()
        {
            var record = app.Store.GetWordHistory(word.Id);
            schedule.Text = record.Due is { } due ? $"下次复习　{due.LocalDateTime:yyyy/MM/dd HH:mm}" : "首次评分后安排复习";
            recent.Header = $"学习记录　{record.Attempts} 次";
            recent.IsEnabled = record.Attempts > 0;
            history.Children.Clear();
            if (record.Attempts > record.Recent.Count) history.Children.Add(UI.Secondary("最近 5 次"));
            foreach (var review in record.Recent)
            {
                string result = review.Rating == Rating.Again ? "不记得" : "记得";
                string kind = review.WasNew ? "新学" : "复习";
                history.Children.Add(UI.Text($"{review.At.LocalDateTime:MM/dd HH:mm}　{kind}　·　{result}"));
            }
        }
        RefreshHistory();
        var dialog = new ContentDialog
        {
            Title = word.English, XamlRoot = root, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "保存笔记", IsPrimaryButtonEnabled = false, CloseButtonText = "关闭"
        };
        editor.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = editor.Text.Trim() != original;
            dialog.CloseButtonText = dialog.IsPrimaryButtonEnabled ? "取消" : "关闭";
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            app.StudyAction(() => { app.Store.SetNote(word.Id, editor.Text); args.Cancel = false; });
        };
        app.Session.RecordsChanged += RefreshHistory;
        try { await dialog.ShowAsync(); }
        finally { app.Session.RecordsChanged -= RefreshHistory; }
    }
}
