using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Vocabulary.Core;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Vocabulary;

internal sealed class StudyCard : Grid
{
    private readonly App app;
    private readonly bool compact;
    private readonly StackPanel words = UI.Column(8);
    private readonly StackPanel actions = UI.Column(6);
    private readonly ScrollViewer scroll;
    private readonly UISettings settings = new();
    private readonly Grid meta = new();
    private readonly TextBlock status = UI.Secondary("");
    private readonly Button undo;
    private BookmarkButton? bookmark;
    private readonly StackPanel note = UI.Column(2);
    private readonly TextBlock noteText = UI.Text("");
    private Guid? renderedUndo;
    private bool rendered;

    public StudyCard(App app, bool compact)
    {
        this.app = app;
        this.compact = compact;
        RowSpacing = compact ? 6 : 14;
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        scroll = new ScrollViewer
        {
            Content = words,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Children.Add(scroll);
        Grid.SetRow(actions, 1);
        Children.Add(actions);
        undo = UI.IconButton("\uE7A7", "撤销最近一次评分", () =>
        {
            if (renderedUndo is { } token) app.StudyAction(() => app.Session.Undo(token));
        });
        undo.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        meta.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        meta.Children.Add(status);
        Grid.SetColumn(undo, 2);
        meta.Children.Add(undo);
        if (!compact) Height = 302;
        note.Children.Add(UI.Secondary("我的笔记"));
        note.Children.Add(noteText);
        Loaded += (_, _) =>
        {
            app.Session.Changed += Render;
            app.Store.NoteChanged += UpdateNote;
            Render();
            if (!compact) FocusAction();
        };
        Unloaded += (_, _) => { app.Session.Changed -= Render; app.Store.NoteChanged -= UpdateNote; };
        PreviewKeyDown += HandleKey;
    }

    internal void FocusAction()
    {
        if (actions.Children.LastOrDefault() is Grid buttons &&
            buttons.Children.OfType<Button>().FirstOrDefault() is { } button)
            button.Focus(FocusState.Programmatic);
    }

    private void HandleKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or RichEditBox || app.Session.Current is not { } card) return;
        if (e.Key == VirtualKey.Space && (bookmark is { FocusState: not FocusState.Unfocused } || undo.FocusState != FocusState.Unfocused)) return;
        if (e.KeyStatus.WasKeyDown && e.Key is VirtualKey.Space or VirtualKey.Number1 or VirtualKey.Number2 or VirtualKey.NumberPad1 or VirtualKey.NumberPad2)
        { e.Handled = true; return; }
        Action? operation = (card.Face, e.Key) switch
        {
            (CardFace.Front, VirtualKey.Number1 or VirtualKey.NumberPad1) => () => app.Session.Remember(card.Token),
            (CardFace.Front, VirtualKey.Number2 or VirtualKey.NumberPad2) => () => app.Session.Forget(card.Token),
            (CardFace.Front, VirtualKey.Space) => () => app.Session.Peek(card.Token),
            (_, VirtualKey.Space) => () => app.Session.Continue(card.Token),
            _ => null
        };
        if (operation is null) return;
        e.Handled = true;
        app.StudyAction(operation);
    }

    private void Render()
    {
        bool hadFocus = XamlRoot is not null && IsWithin(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);
        words.Children.Clear();
        actions.Children.Clear();
        var session = app.Session;
        var card = session.Current;
        if (bookmark is not null) { meta.Children.Remove(bookmark); bookmark = null; }
        if (card is not null)
        {
            bookmark = new BookmarkButton(app, card.Word.Id);
            Grid.SetColumn(bookmark, 1);
            meta.Children.Add(bookmark);
        }
        renderedUndo = session.UndoToken;
        undo.Visibility = renderedUndo.HasValue ? Visibility.Visible : Visibility.Collapsed;
        words.Children.Add(meta);
        status.Text = card?.Face switch
        {
            CardFace.Lookup => "仅查看",
            CardFace.Forgotten => "已记录为不记得",
            _ => session.Mode switch { StudyMode.Notebook => "生词本练习", StudyMode.Forgotten => "遗忘词复习", _ => "英译中" }
        };
        var buttons = new Grid { ColumnSpacing = 8 };
        void AddButton(string label, Action action, bool accent = false)
        {
            var button = UI.Button(label, () => app.StudyAction(action), accent);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(4, 6, 4, 6);
            Grid.SetColumn(button, buttons.ColumnDefinitions.Count);
            buttons.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            buttons.Children.Add(button);
        }

        if (card is null)
        {
            string title = session.Browsed ? "本轮已浏览完" : session.Mode != StudyMode.Daily ? "本轮复习结束" : "当前词书新词已学完，暂无到期复习";
            words.Children.Add(UI.Text(title, compact ? 22 : 32, true));
            var book = app.Store.Load().Book;
            bool retryPractice = session.Mode switch
            {
                StudyMode.Notebook => app.Store.BookmarkCount(book) > 0,
                StudyMode.Forgotten => app.Store.Search(book, "", LearnedFilter.Forgotten, 0, 1).Count > 0,
                _ => false
            };
            if (session.Browsed || retryPractice)
                AddButton("再学一轮", () => session.StartRound(session.Mode), true);
            if (session.Mode != StudyMode.Daily) AddButton("返回日常学习", () => session.StartRound());
            if (session.Mode == StudyMode.Daily && !session.Browsed)
            {
                var due = app.Store.Summary(app.Store.Load(), DateTimeOffset.Now).NextDue;
                words.Children.Add(UI.Secondary(due is { } next ? $"下次复习　{next.LocalDateTime:MM月dd日 HH:mm}" : "可以在词库中查看已学单词。"));
            }
        }
        else
        {
            var spelling = UI.Text(card.Word.English, compact ? 34 : 48, true);
            AutomationProperties.SetAutomationId(spelling, "StudyWord");
            words.Children.Add(spelling);
            words.Children.Add(UI.Secondary(card.Word.Phonetic.Length == 0 ? "暂无音标" : $"/{card.Word.Phonetic}/", compact ? 14 : 17));
            if (card.Face == CardFace.Front)
            {
                AddButton("记得", () => session.Remember(card.Token), true);
                AddButton("不记得", () => session.Forget(card.Token));
                AddButton("查看释义", () => session.Peek(card.Token));
                ((Button)buttons.Children[2]).Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
            }
            else
            {
                if (card.Word.PartOfSpeech.Length > 0) words.Children.Add(UI.Secondary(card.Word.PartOfSpeech));
                words.Children.Add(UI.Text(card.Word.Meaning, compact ? 15 : 17));
                words.Children.Add(note);
                UpdateNote(card.Word.Id);
                AddButton("下一词", () => session.Continue(card.Token), true);
            }
        }
        actions.Children.Add(buttons);
        scroll.ChangeView(null, 0, null, true);
        if (rendered && settings.AnimationsEnabled)
        {
            var transition = new DoubleAnimation { From = .6, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(130)) };
            Storyboard.SetTarget(transition, words);
            Storyboard.SetTargetProperty(transition, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(transition);
            storyboard.Begin();
        }
        rendered = true;
        if (hadFocus) FocusAction();
    }

    private void UpdateNote(string id)
    {
        if (app.Session.Current is not { } card || card.Face == CardFace.Front || card.Word.Id != id) return;
        noteText.Text = app.Store.GetEntry(id).Note;
        note.Visibility = noteText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool IsWithin(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element == this) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }
}
