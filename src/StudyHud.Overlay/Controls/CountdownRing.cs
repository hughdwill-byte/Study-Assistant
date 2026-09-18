using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// A small circular countdown that visibly <em>drains</em> as time runs out — externalising time for
/// ADHD (time blindness). Non-interactive; the owner calls <see cref="SetFraction"/> each tick.
/// </summary>
public sealed class CountdownRing : Grid
{
    private readonly double _size;
    private readonly double _thickness;
    private readonly Ellipse _track;
    private readonly Ellipse _full;
    private readonly Path _arc;
    private readonly TextBlock _label;

    public CountdownRing(double size = 54, double thickness = 6)
    {
        _size = size;
        _thickness = thickness;
        Width = size;
        Height = size;
        IsHitTestVisible = false;

        _track = new Ellipse
        {
            Width = size - thickness, Height = size - thickness,
            StrokeThickness = thickness,
            Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Fill = Brushes.Transparent
        };
        Children.Add(_track);

        _full = new Ellipse
        {
            Width = size - thickness, Height = size - thickness,
            StrokeThickness = thickness, Fill = Brushes.Transparent,
            Visibility = Visibility.Collapsed
        };
        Children.Add(_full);

        _arc = new Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        Children.Add(_arc);

        _label = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = size * 0.24,
            Foreground = Brushes.White
        };
        Children.Add(_label);
    }

    /// <summary>Sets the fraction of time remaining (1 = full, 0 = empty) and an optional centre label.</summary>
    public void SetFraction(double remaining, Brush stroke, string? centreLabel = null)
    {
        remaining = Math.Clamp(remaining, 0, 1);
        _full.Stroke = stroke;
        _arc.Stroke = stroke;
        _label.Text = centreLabel ?? "";

        if (remaining >= 0.999)
        {
            _full.Visibility = Visibility.Visible;
            _arc.Visibility = Visibility.Collapsed;
            return;
        }
        _full.Visibility = Visibility.Collapsed;

        if (remaining <= 0.001)
        {
            _arc.Visibility = Visibility.Collapsed;
            return;
        }

        _arc.Visibility = Visibility.Visible;
        double r = (_size - _thickness) / 2;
        double cx = _size / 2, cy = _size / 2;
        double startAngle = -90;                     // 12 o'clock
        double endAngle = -90 + remaining * 360;     // clockwise as it drains

        Point start = PointOnCircle(cx, cy, r, startAngle);
        Point end = PointOnCircle(cx, cy, r, endAngle);

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = remaining > 0.5,
            RotationAngle = 0
        });
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        _arc.Data = geo;
    }

    private static Point PointOnCircle(double cx, double cy, double r, double angleDegrees)
    {
        double a = angleDegrees * Math.PI / 180.0;
        return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }
}
