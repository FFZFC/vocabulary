using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Vocabulary;

internal static class UI
{
    public static StackPanel Column(double spacing = 12) => new() { Spacing = spacing };
    public static StackPanel Row(double spacing = 10) => new() { Orientation = Orientation.Horizontal, Spacing = spacing };
    public static TextBlock Text(string text, double size = 14, bool strong = false) => new()
    { Text = text, FontSize = size, FontWeight = new FontWeight { Weight = (ushort)(strong ? 600 : 400) }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock Secondary(string text, double size = 13)
    {
        var label = Text(text, size); label.Style = (Style)Application.Current.Resources["MutedText"]; return label;
    }
    public static Button Button(string label, Action action, bool accent = false)
    {
        var b = new Button { Content = label, MinHeight = 34 };
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += (_, _) => action();
        return b;
    }
    public static Button IconButton(string glyph, string label, Action action)
    {
        var b = Button(label, action);
        b.Content = new FontIcon { Glyph = glyph, FontSize = 16 };
        AutomationProperties.SetName(b, label);
        ToolTipService.SetToolTip(b, label);
        return b;
    }
    public static Border Panel(UIElement child, double padding = 22) => new()
    { Style = (Style)Application.Current.Resources["Surface"], Padding = new Thickness(padding), Child = child };
    public static ComboBox Books(Vocabulary.Core.Book selected) =>
        new() { Header = "学习词书", Items = { "大学英语四级", "大学英语六级（含四级基础）" }, SelectedIndex = (int)selected, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static NumberBox Goal(int value) => new() { Header = "每日新词目标", Minimum = 1, Maximum = int.MaxValue, Value = value, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static int ReadGoal(NumberBox box, int fallback) => double.IsFinite(box.Value) ? (int)Math.Clamp(Math.Round(box.Value), 1, int.MaxValue) : fallback;
}

