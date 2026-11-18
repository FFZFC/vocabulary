using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Vocabulary.Core;
using Windows.System;

namespace Vocabulary;

internal static class StudyCharts
{
    public static string Description(DailyStats d) =>
        $"{d.Date:MM/dd}　新学 {d.NewWords}　复习 {d.Reviewed}　记得率 {(d.Recall is { } rate ? rate.ToString("P0") : "—")}";

    public static FrameworkElement Trend(IReadOnlyList<DailyStats> data, bool recall) =>
        Build(data, recall ? Chart.Recall : Chart.Volume);

    public static FrameworkElement Heatmap(IReadOnlyList<DailyStats> data) => Build(data, Chart.Calendar);
    private enum Chart { Volume, Recall, Calendar }

    private static FrameworkElement Build(IReadOnlyList<DailyStats> days, Chart chart)
    {
        var container = UI.Column(8);
        string title = chart switch { Chart.Volume => "学习量", Chart.Recall => "记得率趋势", _ => "近 12 周学习日历" };
        var heading = UI.Row(14);
        heading.Children.Add(UI.Text(title, 18, true));
        container.Children.Add(heading);
        var canvas = new Canvas { Height = chart == Chart.Calendar ? 160 : 145, HorizontalAlignment = HorizontalAlignment.Stretch };
        var surface = new Button
        {
            Content = canvas,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        surface.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        AutomationProperties.SetName(surface, title);
        AutomationProperties.SetHelpText(surface, "使用方向键查看日期和数值");
        container.Children.Add(surface);
        var detail = UI.Secondary("");
        container.Children.Add(detail);
        int active = days.Count - 1;
        const double left = 36, right = 12, top = 10;
        Brush Resource(string key) => (Brush)Application.Current.Resources[key];
        SolidColorBrush Accent() => (SolidColorBrush)Resource("AccentFillColorDefaultBrush");
        Brush Second() => Resource("TextFillColorSecondaryBrush");
        void Marker(double x, double y, double w, double h, Brush fill, double opacity = 1)
        {
            var rect = new Rectangle { Width = Math.Max(1, w), Height = Math.Max(1, h), Fill = fill, Opacity = opacity, RadiusX = 2, RadiusY = 2 };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); canvas.Children.Add(rect);
        }
        void Label(string value, double x, double y)
        {
            var t = UI.Secondary(value, 11);
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y); canvas.Children.Add(t);
        }
        void Line(double x, double y, double x2, double y2, Brush stroke, double thick)
        {
            canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line { X1 = x, Y1 = y, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thick });
        }
        string Describe(int i)
        {
            var d = days[i];
            return chart == Chart.Calendar ? $"{d.Date:yyyy/MM/dd}　已评分 {d.DistinctWords} 词" :
                chart == Chart.Recall ? $"{d.Date:MM/dd}　记得 {d.Remembered} 次 / 评分 {d.Attempts} 次　{(d.Recall is { } r ? r.ToString("P0") : "—")}" :
                $"{d.Date:MM/dd}　新学 {d.NewWords} 词　复习 {d.Reviewed} 词";
        }
        void Draw()
        {
            canvas.Children.Clear();
            double width = canvas.ActualWidth - left - right;
            if (width < 30) return;
            var accent = Accent();
            var neutral = Second();
            var gridBrush = Resource("DividerStrokeColorDefaultBrush");
            if (chart == Chart.Calendar)
            {
                double cell = width / 12;
                int peak = Math.Max(1, days.Max(day => day.DistinctWords));
                for (int row = 0; row < 7; row++)
                {
                    var weekday = "日一二三四五六"[(int)days[row].Date.DayOfWeek];
                    Label(weekday.ToString(), 4, top + row * 18);
                }
                for (int i = 0; i < days.Count; i++)
                {
                    double x = left + i / 7 * cell, y = top + i % 7 * 18;
                    int count = days[i].DistinctWords;
                    Marker(x, y, cell - 3, 15, count == 0 ? gridBrush : accent, count == 0 ? 1 : .2 + .8 * count / peak);
                    if (i == active) Line(x, y + 15, x + cell - 3, y + 15, neutral, 2);
                }
            }
            else
            {
                const double height = 104;
                int max = chart == Chart.Recall ? 100 : Math.Max(2, days.Max(d => Math.Max(d.NewWords, d.Reviewed)));
                if (chart == Chart.Volume && max % 2 == 1) max++;
                double step = width / days.Count;
                for (int level = 0; level <= 2; level++)
                {
                    double y = top + height * (1 - level / 2d);
                    Label(chart == Chart.Recall ? $"{level * 50}%" : (max * level / 2).ToString(), 0, y - 7);
                    Line(left, y, left + width, y, gridBrush, 1);
                }
                double? previousY = null;
                for (int i = 0; i < days.Count; i++)
                {
                    var d = days[i]; double x = left + (i + .5) * step;
                    if (chart == Chart.Volume)
                    {
                        double bar = Math.Min(22, step * .34);
                        if (d.NewWords > 0) Marker(x - bar, top + height * (1 - (double)d.NewWords / max), bar - 1, height * d.NewWords / max, accent);
                        if (d.Reviewed > 0) Marker(x + 1, top + height * (1 - (double)d.Reviewed / max), bar - 1, height * d.Reviewed / max, neutral, .8);
                    }
                    else if (d.Recall is { } pct)
                    {
                        double y = top + height * (1 - pct);
                        if (previousY is { } py) Line(x - step, py, x, y, accent, 2);
                        var dot = new Ellipse { Width = 6, Height = 6, Fill = accent };
                        Canvas.SetLeft(dot, x - 3); Canvas.SetTop(dot, y - 3); canvas.Children.Add(dot);
                        previousY = y;
                    }
                    else previousY = null;
                    if (i == active) Line(x - 3, top + height + 3, x + 3, top + height + 3, neutral, 2);
                }
            }
            double baseY = canvas.Height - 19;
            Label(days[0].Date.ToString("MM/dd"), left, baseY);
            Label(days[^1].Date.ToString("MM/dd"), left + width - 32, baseY);
            detail.Text = Describe(active);
            AutomationProperties.SetItemStatus(surface, detail.Text);
            ToolTipService.SetToolTip(surface, detail.Text);
        }
        if (chart == Chart.Volume)
        {
            void Legend(string text, Brush brush)
            {
                var key = UI.Row(5);
                key.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = brush, VerticalAlignment = VerticalAlignment.Center });
                key.Children.Add(UI.Secondary(text, 12)); heading.Children.Add(key);
            }
            Legend("新学", Accent()); Legend("复习", Second());
        }
        if (chart == Chart.Calendar)
        {
            var scale = UI.Row(3);
            scale.Children.Add(UI.Secondary("少", 11));
            for (int i = 0; i < 4; i++)
                scale.Children.Add(new Rectangle
                {
                    Width = 10,
                    Height = 10,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = Accent(),
                    Opacity = .2 + i * .8 / 3,
                    VerticalAlignment = VerticalAlignment.Center
                });
            scale.Children.Add(UI.Secondary("多", 11));
            heading.Children.Add(scale);
        }
        void Select(int index)
        {
            index = Math.Clamp(index, 0, days.Count - 1);
            if (index != active) { active = index; Draw(); }
        }
        surface.PointerMoved += (_, e) =>
        {
            var pos = e.GetCurrentPoint(canvas).Position;
            double width = Math.Max(1, canvas.ActualWidth - left - right);
            int index = chart == Chart.Calendar
                ? Math.Clamp((int)((pos.X - left) * 12 / width), 0, 11) * 7 + Math.Clamp((int)((pos.Y - top) / 18), 0, 6)
                : (int)((pos.X - left) * days.Count / width);
            Select(index);
        };
        surface.PreviewKeyDown += (_, e) =>
        {
            int jump = chart == Chart.Calendar ? 7 : 1;
            int next = e.Key switch
            {
                VirtualKey.Left => active - jump,
                VirtualKey.Right => active + jump,
                VirtualKey.Up => active - 1,
                VirtualKey.Down => active + 1,
                VirtualKey.Home => 0,
                VirtualKey.End => days.Count - 1,
                _ => -1
            };
            if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)
            { e.Handled = true; Select(next); }
        };
        canvas.SizeChanged += (_, _) => Draw();
        canvas.Loaded += (_, _) => Draw();
        canvas.ActualThemeChanged += (_, _) => Draw();
        return container;
    }
}
