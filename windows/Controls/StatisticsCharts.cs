using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lexi;

/// <summary>
/// Full-year contribution grid. Daily mode uses colour intensity; aggregated modes fill each
/// column bottom-up so the same calendar structure remains readable without inventing data.
/// </summary>
public sealed class StatisticsHeatmap : Control
{
    private static readonly IBrush FallbackEmpty = new SolidColorBrush(Color.Parse("#EDF1F4"));
    private static readonly IBrush FallbackInk = new SolidColorBrush(Color.Parse("#728492"));
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _reveal = 1;
    private bool _animate;

    public StudyStatisticsSnapshot Snapshot { get; set; } = new();
    public StatisticsPeriod Period { get; set; }
    public StatisticsActivityFilter Filter { get; set; }
    public int Year { get; set; } = DateTime.Today.Year;
    public Func<string, string, string> Text { get; set; } = (zh, _) => zh;

    public StatisticsHeatmap()
    {
        Height = 152;
        MinWidth = 350;
        _timer.Tick += (_, _) =>
        {
            _reveal = Math.Min(1, _clock.Elapsed.TotalMilliseconds / 800);
            InvalidateVisual();
            if (_reveal >= 1) _timer.Stop();
        };
    }

    public double RevealProgress => _reveal;
    public new bool IsAnimating => _timer.IsEnabled;

    public void Replay(bool reduceMotion)
    {
        _timer.Stop();
        _animate = !reduceMotion;
        _reveal = reduceMotion ? 1 : 0;
        _clock.Restart();
        if (!reduceMotion) _timer.Start();
        InvalidateVisual();
    }

    public void StopAnimation()
    {
        _timer.Stop();
        _reveal = 1;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private IReadOnlyList<StatisticsColumn> Columns() =>
        StudyStatistics.BuildGrid(Snapshot, Year, Period, Filter);

    private (double Size, double Pitch, double Left) Layout(IReadOnlyList<StatisticsColumn> columns)
    {
        var pitch = Math.Max(5, (Bounds.Width - 42) / Math.Max(1, columns.Count));
        return (Math.Min(12, pitch - 3), pitch, 36);
    }

    private FormattedText Label(string text) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter"), 10,
            (IBrush?)this.FindResource(ActualThemeVariant, "MutedBrush") ?? FallbackInk);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var columns = Columns();
        if (columns.Count == 0) return;

        var (size, pitch, left) = Layout(columns);
        const double top = 24;
        var empty = (IBrush?)this.FindResource(ActualThemeVariant, "InsetBrush") ?? FallbackEmpty;
        var ink = (IBrush?)this.FindResource(ActualThemeVariant, "MutedBrush") ?? FallbackInk;
        var max = Period == StatisticsPeriod.Day
            ? Snapshot.Days.Where(pair => pair.Key.Year == Year)
                .Select(pair => pair.Value.Seconds(Filter))
                .DefaultIfEmpty()
                .Max()
            : columns.Max(column => column.Seconds);
        var first = new DateOnly(Year, 1, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            var alpha = _animate
                ? Math.Clamp((_reveal * 1.18 - columnIndex / (double)columns.Count) / 0.18, 0, 1)
                : 1;
            using (context.PushOpacity(alpha))
            {
                for (var row = 0; row < 7; row++)
                {
                    var brush = empty;
                    if (Period == StatisticsPeriod.Day)
                    {
                        var date = start.AddDays(columnIndex * 7 + row);
                        if (date.Year == Year &&
                            date <= Snapshot.Today &&
                            Snapshot.Days.TryGetValue(date, out var day) &&
                            day.Seconds(Filter) > 0)
                        {
                            var strength = max > 0 ? day.Seconds(Filter) / max : 0;
                            var color = strength < .25 ? "#DFEDF3"
                                : strength < .5 ? "#A7D3E3"
                                : strength < .75 ? "#69B6D2"
                                : "#357B9B";
                            brush = new SolidColorBrush(Color.Parse(color));
                        }
                    }
                    else
                    {
                        var cells = column.Seconds <= 0
                            ? 0
                            : Math.Max(1, (int)Math.Ceiling(column.Seconds / Math.Max(1, max) * 7));
                        if (!column.Future && row >= 7 - cells)
                            brush = new SolidColorBrush(Color.Parse("#69B6D2"));
                    }

                    context.DrawRectangle(
                        brush, null,
                        new Rect(left + columnIndex * pitch, top + row * (size + 3), size, size),
                        2, 2);
                }
            }
        }

        var rowNames = Period == StatisticsPeriod.Day
            ? new[] { Text("一", "M"), "", Text("三", "W"), "", Text("五", "F"), "", "" }
            : new[] { max > 0 ? $"{max / 60:0.#}" : "0", "", "", "", "", "", "0" };
        for (var row = 0; row < rowNames.Length; row++)
        {
            if (rowNames[row].Length > 0)
                context.DrawText(Label(rowNames[row]), new Point(0, top + row * (size + 3)));
        }

        var lastMonth = 0;
        for (var index = 0; index < columns.Count; index++)
        {
            var anchor = start.AddDays(index * 7 + 3);
            if (anchor.Year != Year || anchor.Month == lastMonth) continue;
            lastMonth = anchor.Month;
            var month = Text(
                CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(anchor.Month),
                CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(anchor.Month));
            context.DrawText(Label(month), new Point(left + index * pitch, 2));
        }

        var units = Period == StatisticsPeriod.Day
            ? Text("色深 · 有效学习分钟", "Colour · active minutes")
            : Text("柱高 · 有效学习分钟", "Column height · active minutes");
        context.DrawText(Label(units), new Point(left, top + 7 * (size + 3) + 8));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var columns = Columns();
        if (columns.Count == 0)
        {
            ToolTip.SetTip(this, null);
            return;
        }

        var (size, pitch, left) = Layout(columns);
        var point = e.GetPosition(this);
        var columnIndex = (int)Math.Floor((point.X - left) / pitch);
        var row = (int)Math.Floor((point.Y - 24) / (size + 3));
        if (columnIndex < 0 || columnIndex >= columns.Count || row < 0 || row >= 7)
        {
            ToolTip.SetTip(this, null);
            return;
        }

        if (Period == StatisticsPeriod.Day)
        {
            var first = new DateOnly(Year, 1, 1);
            var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
            var date = start.AddDays(columnIndex * 7 + row);
            Snapshot.Days.TryGetValue(date, out var day);
            day ??= new StudyStatisticsDay();
            ToolTip.SetTip(this,
                $"{date:yyyy-MM-dd}\n" +
                Text(
                    $"初学 {day.FirstLearnSeconds / 60:0.#} · 复习 {day.ReviewSeconds / 60:0.#} · 拼写 {day.SpellingSeconds / 60:0.#} 分钟\n新学 {day.NewWords} 个词",
                    $"First learn {day.FirstLearnSeconds / 60:0.#} · Review {day.ReviewSeconds / 60:0.#} · Spelling {day.SpellingSeconds / 60:0.#} min\n{day.NewWords} newly learned words"));
            return;
        }

        var column = columns[columnIndex];
        ToolTip.SetTip(this,
            $"{column.Start:yyyy-MM-dd} - {column.End:yyyy-MM-dd}\n" +
            (column.Future
                ? Text("未来日期 · 暂无记录", "Future date · no record")
                : Text(
                    $"初学 {column.FirstLearnSeconds / 60:0.#} · 复习 {column.ReviewSeconds / 60:0.#} · 拼写 {column.SpellingSeconds / 60:0.#} 分钟\n新学 {column.NewWords} 个词",
                    $"First learn {column.FirstLearnSeconds / 60:0.#} · Review {column.ReviewSeconds / 60:0.#} · Spelling {column.SpellingSeconds / 60:0.#} min\n{column.NewWords} newly learned words")));
    }
}

/// <summary>FSRS prediction with observed recall groups. The curve is never framed as a ranking.</summary>
public sealed class StatisticsCurve : Control
{
    private static readonly IBrush FallbackInk = new SolidColorBrush(Color.Parse("#728492"));
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private double _reveal = 1;

    public IReadOnlyList<(double Days, double Rate)> Prediction { get; set; } = [];
    public IReadOnlyList<RecallObservation> Observations { get; set; } = [];
    public Func<string, string, string> Text { get; set; } = (zh, _) => zh;

    public StatisticsCurve()
    {
        Height = 230;
        MinWidth = 320;
        _timer.Tick += (_, _) =>
        {
            _reveal = Math.Min(1, _clock.Elapsed.TotalMilliseconds / 720);
            InvalidateVisual();
            if (_reveal >= 1) _timer.Stop();
        };
    }

    public double RevealProgress => _reveal;
    public new bool IsAnimating => _timer.IsEnabled;

    public void Replay(bool reduceMotion)
    {
        _timer.Stop();
        _reveal = reduceMotion ? 1 : 0;
        _clock.Restart();
        if (!reduceMotion) _timer.Start();
        InvalidateVisual();
    }

    public void StopAnimation()
    {
        _timer.Stop();
        _reveal = 1;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private FormattedText Label(string text) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter"), 11,
            (IBrush?)this.FindResource(ActualThemeVariant, "MutedBrush") ?? FallbackInk);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var plot = new Rect(42, 20, Math.Max(1, Bounds.Width - 62), 165);
        Point Map(double days, double rate) =>
            new(plot.X + days / 30 * plot.Width,
                plot.Bottom - Math.Clamp(rate, 0, 1) * plot.Height);

        var grid = new Pen(
            (IBrush?)this.FindResource(ActualThemeVariant, "LineBrush")
                ?? new SolidColorBrush(Color.Parse("#E7EEF2")), 1);
        for (var percent = 0; percent <= 100; percent += 25)
        {
            var y = Map(0, percent / 100d).Y;
            context.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
            context.DrawText(Label(percent + "%"), new Point(1, y - 7));
        }
        foreach (var day in new[] { 0, 7, 14, 21, 30 })
            context.DrawText(Label(day.ToString(CultureInfo.InvariantCulture)),
                new Point(Map(day, 0).X - 4, plot.Bottom + 8));
        context.DrawText(Label(Text("距上次学习的天数", "Days since previous study")),
            new Point(plot.X, plot.Bottom + 28));

        using (context.PushClip(new Rect(plot.X - 3, plot.Y - 3, plot.Width * _reveal + 6, plot.Height + 6)))
        {
            var prediction = Prediction
                .Where(point => point.Days is >= 0 and <= 30)
                .OrderBy(point => point.Days)
                .Select(point => Map(point.Days, point.Rate))
                .ToArray();
            var pen = new Pen(new SolidColorBrush(Color.Parse("#357B9B")), 2.5);
            for (var index = 1; index < prediction.Length; index++)
                context.DrawLine(pen, prediction[index - 1], prediction[index]);

            foreach (var observation in Observations.Where(point => point.Days is >= 0 and <= 30))
                context.DrawEllipse(
                    new SolidColorBrush(Color.Parse("#79BDD4")),
                    new Pen(Brushes.White, 1.5),
                    Map(observation.Days, observation.Rate), 4, 4);
        }

        if (Prediction.Count == 0 && Observations.Count == 0)
            context.DrawText(Label(Text(
                    "完成学习后显示真实记录与模型预测",
                    "Study to see your observations and model prediction")),
                new Point(plot.X + 12, plot.Y + 68));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var plotWidth = Math.Max(1, Bounds.Width - 62);
        var day = Math.Clamp((e.GetPosition(this).X - 42) / plotWidth * 30, 0, 30);
        var prediction = Prediction.OrderBy(point => Math.Abs(point.Days - day)).FirstOrDefault();
        var observation = Observations
            .Where(point => point.Days is >= 0 and <= 30)
            .OrderBy(point => Math.Abs(point.Days - day))
            .FirstOrDefault();
        var tip = Text($"间隔约 {day:0.#} 天", $"About {day:0.#} days after study");
        if (Prediction.Count > 0)
            tip += "\n" + Text(
                $"模型预计可回忆 {prediction.Rate:P0}",
                $"Predicted recall {prediction.Rate:P0}");
        if (observation != null)
            tip += "\n" + Text(
                $"最近的观察组：平均间隔 {observation.Days:0.#} 天，成功回忆 {observation.Rate:P0}（{observation.Samples} 次有效复习）",
                $"Nearest observed group: mean gap {observation.Days:0.#} days, recall {observation.Rate:P0} ({observation.Samples} valid reviews)");
        ToolTip.SetTip(this, tip);
    }
}

/// <summary>Fourteen-day stacked duration bars, split into first learning, review and spelling.</summary>
public sealed class StatisticsTimeTrend : Control
{
    public StudyStatisticsSnapshot Snapshot { get; set; } = new();
    public Func<string, string, string> Text { get; set; } = (zh, _) => zh;

    public StatisticsTimeTrend()
    {
        Height = 170;
        MinWidth = 300;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var ink = (IBrush?)this.FindResource(ActualThemeVariant, "MutedBrush")
            ?? new SolidColorBrush(Color.Parse("#728492"));
        var recent = Enumerable.Range(0, 14)
            .Select(index => Snapshot.Today.AddDays(index - 13))
            .ToArray();
        var maximum = recent
            .Select(date => Snapshot.Days.TryGetValue(date, out var day)
                ? day.Seconds(StatisticsActivityFilter.All)
                : 0)
            .DefaultIfEmpty()
            .Max();
        const double plotLeft = 35;
        var plotRight = Math.Max(plotLeft + 1, Bounds.Width - 10);
        var pitch = (plotRight - plotLeft) / 14;
        const double bottom = 135;
        var colors = new[] { "#DFEDF3", "#357B9B", "#82B8CC" };
        var dateLabels = recent
            .Select(date => new FormattedText(
                date.ToString("MM/dd", CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Inter"), 10, ink))
            .ToArray();

        for (var index = 0; index < recent.Length; index++)
        {
            Snapshot.Days.TryGetValue(recent[index], out var day);
            day ??= new StudyStatisticsDay();
            var values = new[] { day.FirstLearnSeconds, day.ReviewSeconds, day.SpellingSeconds };
            var y = bottom;
            for (var kind = 0; kind < values.Length; kind++)
            {
                var height = maximum > 0 ? values[kind] / maximum * 105 : 0;
                y -= height;
                context.DrawRectangle(
                    new SolidColorBrush(Color.Parse(colors[kind])), null,
                    new Rect(plotLeft + index * pitch, y, Math.Max(3, pitch - 6), height), 2, 2);
            }
        }

        // Keep the newest date, then walk backwards so labels that would collide are omitted.
        var rightBoundary = plotRight;
        for (var index = recent.Length - 1; index >= 0; index--)
        {
            var label = dateLabels[index];
            var idealLeft = plotLeft + (index + .5) * pitch - label.Width / 2;
            var labelLeft = Math.Clamp(idealLeft, plotLeft, Math.Max(plotLeft, plotRight - label.Width));
            if (labelLeft + label.Width > rightBoundary) continue;
            context.DrawText(label, new Point(labelLeft, bottom + 7));
            rightBoundary = labelLeft - 8;
        }

        context.DrawLine(
            new Pen(new SolidColorBrush(Color.Parse("#E2EAF0")), 1),
            new Point(plotLeft, bottom), new Point(plotRight, bottom));
        context.DrawText(
            new FormattedText(
                Text($"{maximum / 60:0.#} 分", $"{maximum / 60:0.#} min"),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Inter"), 10, ink),
            new Point(0, 17));

        if (recent.All(date => !Snapshot.Days.ContainsKey(date)))
        {
            context.DrawText(
                new FormattedText(
                    Text("近 14 天暂无时长记录", "No duration recorded in the last 14 days"),
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Inter"), 11, ink),
                new Point(48, 67));
        }
    }
}
