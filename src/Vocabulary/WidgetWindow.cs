using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinRT.Interop;

namespace Vocabulary;

internal sealed class WidgetWindow : Window
{
    private readonly App app;
    private readonly DesktopHost host;
    private readonly CardBackdrop glass = new();
    private nint lastOwner;
    private readonly TextBlock label;
    private readonly TextBlock goalStatus;
    private bool desktopAttached;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer saveTimer;

    public WidgetWindow(App app)
    {
        this.app = app;
        Title = "四六级单词 · 桌面卡片";
        glass.Density = app.GlassDensity;
        DispatcherQueue.EnsureSystemDispatcherQueue();
        AppWindow.SetIcon(App.IconPath);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        AppWindow.IsShownInSwitchers = false;
        var layout = new Grid { Padding = new Thickness(16, 8, 16, 14), RowSpacing = 6 };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        label = UI.Secondary("", 13);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(label, "WidgetGoalProgress");
        var dragArea = new Border { Child = label, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        ToolTipService.SetToolTip(dragArea, "拖动卡片");
        void DragRegion()
        {
            if (dragArea.XamlRoot is null || dragArea.ActualWidth <= 0) return;
            var at = dragArea.TransformToVisual(layout).TransformPoint(new Windows.Foundation.Point(0, 0));
            double dpi = dragArea.XamlRoot.RasterizationScale;
            var bounds = new RectInt32((int)(at.X * dpi), (int)(at.Y * dpi), (int)(dragArea.ActualWidth * dpi), (int)(dragArea.ActualHeight * dpi));
            Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
                .SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption, [bounds]);
        }
        dragArea.Loaded += (_, _) => DragRegion();
        dragArea.SizeChanged += (_, _) => DragRegion();
        bar.Children.Add(dragArea);
        var hide = UI.IconButton("\uE711", "隐藏桌面卡片", app.ToggleWidget);
        hide.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        Grid.SetColumn(hide, 1); bar.Children.Add(hide);
        layout.Children.Add(bar);
        goalStatus = UI.Secondary("今日目标已达成，可继续学习", 12);
        Grid.SetRow(goalStatus, 1); layout.Children.Add(goalStatus);
        var scroll = new StudyCard(app, true);
        Grid.SetRow(scroll, 2); layout.Children.Add(scroll);
        Content = layout;
        SystemBackdrop = glass;
        host = new(this);
        double scale = Math.Max(1, Native.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96d);
        var area = DisplayArea.Primary.WorkArea;
        int width = (int)(360 * scale), height = (int)(310 * scale);
        int left = area.X + area.Width - width - (int)(32 * scale), top = area.Y + (int)(40 * scale);
        var saved = (app.Store.Setting("bounds") ?? "").Split(',');
        if (saved.Length == 5 && saved.All(s => int.TryParse(s, out _)))
        {
            int[] n = Array.ConvertAll(saved, int.Parse);
            left = n[0]; top = n[1]; double ratio = scale * 96 / Math.Clamp(n[4], 96, 768);
            width = (int)Math.Clamp(n[2] * ratio, 300 * scale, 1200 * scale);
            height = (int)Math.Clamp(n[3] * ratio, 200 * scale, 1200 * scale);
        }
        AppWindow.MoveAndResize(Native.VisibleRect(left, top, width, height));
        saveTimer = DispatcherQueue.CreateTimer(); saveTimer.Interval = TimeSpan.FromMilliseconds(350); saveTimer.IsRepeating = false;
        saveTimer.Tick += (_, _) => SaveBounds();
        AppWindow.Changed += (_, e) => { if (e.DidSizeChange || e.DidPositionChange) { saveTimer.Stop(); saveTimer.Start(); } };
        AppWindow.Closing += (_, e) => { if (!app.Exiting) { e.Cancel = true; if (app.Store.Load().WidgetVisible) app.ToggleWidget(); } };
        UpdateGoal();
        app.Session.RecordsChanged += UpdateGoal;
        Closed += (_, _) => { app.Session.RecordsChanged -= UpdateGoal; saveTimer.Stop(); SystemBackdrop = null; host.Dispose(); };
    }
    public bool ShowOnDesktop()
    {
        if (!AppWindow.IsVisible) AppWindow.Show(false);
        bool attached = host.Attach();
        if (attached != desktopAttached)
        {
            desktopAttached = attached;
            ToolTipService.SetToolTip(label, attached ? "拖动卡片" : "单词卡正在等待桌面恢复");
        }
        var owner = Native.GetLong(WindowNative.GetWindowHandle(this), -8);
        if (owner != lastOwner)
        {
            lastOwner = owner;
            glass.ApplyAppearance();
        }
        return desktopAttached;
    }
    internal void UpdateGoal()
    {
        var preferences = app.Store.Load();
        var summary = app.Store.Summary(preferences, DateTimeOffset.Now);
        string book = preferences.Book == Core.Book.Cet4 ? "四级" : "六级";
        label.Text = $"{book} · 今日新学 {summary.NewToday} / {preferences.DailyGoal}";
        goalStatus.Visibility = summary.GoalReached ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetDensity(int value) => glass.Density = value;
    public void CorrectBounds() => host.RestoreBounds();
    public void SaveBounds()
    {
        var s = AppWindow.Size; var p = AppWindow.Position;
        if (s.Width > 0 && s.Height > 0)
            app.Store.Set("bounds", $"{p.X},{p.Y},{s.Width},{s.Height},{Native.GetDpiForWindow(WindowNative.GetWindowHandle(this))}");
    }
}
