using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Vocabulary;

internal sealed class BookmarkButton : Button
{
    private readonly App app;
    private readonly string id;
    private readonly FontIcon star = new() { FontSize = 16 };
    private readonly TextBlock? caption;
    private bool saved;

    public BookmarkButton(App app, string id, bool showCaption = false)
    {
        this.app = app;
        this.id = id;
        if (showCaption)
        {
            var row = UI.Row(6);
            caption = UI.Text("");
            row.Children.Add(star);
            row.Children.Add(caption);
            Content = row;
        }
        else
        {
            Content = star;
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        }
        AutomationProperties.SetAutomationId(this, "BookmarkWord");
        Click += (_, _) => app.StudyAction(() => app.Store.SetBookmark(id, !saved));
        Loaded += (_, _) =>
        {
            app.Store.BookmarkChanged += OnBookmarkChanged;
            Update(app.Store.GetEntry(id).Saved);
        };
        Unloaded += (_, _) => app.Store.BookmarkChanged -= OnBookmarkChanged;
    }

    private void OnBookmarkChanged(string changed, bool value)
    {
        if (changed == id) Update(value);
    }

    private void Update(bool value)
    {
        saved = value;
        string action = saved ? "移出生词本" : "加入生词本";
        star.Glyph = saved ? "\uE735" : "\uE734";
        if (caption is not null) caption.Text = action;
        AutomationProperties.SetName(this, action);
        ToolTipService.SetToolTip(this, action);
    }
}
