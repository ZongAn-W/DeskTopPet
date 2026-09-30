using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPet.Core;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using WpfColor = System.Windows.Media.Color;
using WpfSize = System.Windows.Size;

namespace DesktopPet;

public sealed class PetVisual : FrameworkElement
{
    private double _phase;
    private readonly SpriteAnimator _sprites = new();
    public PetState State { get; set; } = PetState.Idle;
    public bool FacingRight { get; set; } = true;

    public PetVisual()
    {
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
    }

    public void Advance(double seconds)
    {
        _phase += seconds;
        InvalidateVisual();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        var face = new Rect(38, 18, 104, 145);
        return face.Contains(point) ? new PointHitTestResult(this, point) : null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_sprites.HasFrames)
        {
            dc.DrawImage(_sprites.Frame(State, _phase), new Rect(0, 0, 180, 210));
            return;
        }
        var breath = State == PetState.Sleeping ? 1.5 : Math.Sin(_phase * 3.4) * 1.5;
        var sleeping = State == PetState.Sleeping;
        var responding = State == PetState.Responding;
        var walk = State is PetState.WalkingLeft or PetState.WalkingRight ? Math.Sin(_phase * 10) * 3 : 0;
        var hair = new SolidColorBrush(WpfColor.FromRgb(24, 27, 33));
        var hairLight = new SolidColorBrush(WpfColor.FromRgb(43, 48, 57));
        var skin = new SolidColorBrush(WpfColor.FromRgb(248, 220, 205));
        var top = new SolidColorBrush(WpfColor.FromRgb(237, 238, 230));
        var topEdge = new SolidColorBrush(WpfColor.FromRgb(195, 202, 190));
        var shoe = new SolidColorBrush(WpfColor.FromRgb(45, 45, 53));

        dc.PushTransform(new TranslateTransform(FacingRight ? 0 : 180, breath + walk));
        if (!FacingRight) dc.PushTransform(new ScaleTransform(-1, 1));

        // Long, center-parted hair is the strongest silhouette cue from the reference photo.
        dc.DrawEllipse(hair, null, new Point(90, 80), 61, 82);
        dc.DrawEllipse(skin, null, new Point(90, 68), 41, 47);
        dc.DrawGeometry(hair, null, PathGeometry("M 90,22 C 70,18 45,33 34,65 C 27,87 31,132 43,166 C 48,178 58,184 64,174 C 58,135 60,91 72,59 C 78,45 84,36 90,22 Z"));
        dc.DrawGeometry(hair, null, PathGeometry("M 90,22 C 111,18 136,34 147,67 C 154,91 151,136 138,171 C 133,182 123,185 117,174 C 123,135 121,92 109,59 C 103,44 96,35 90,22 Z"));
        dc.DrawGeometry(hairLight, null, PathGeometry("M 89,23 C 78,29 71,40 67,54 C 77,46 84,38 90,29 Z"));
        dc.DrawGeometry(hairLight, null, PathGeometry("M 92,23 C 104,29 111,40 115,54 C 105,46 98,38 92,29 Z"));

        // Sleeveless light top and exposed arms echo the reference silhouette.
        dc.DrawRoundedRectangle(top, null, new Rect(49, 108, 82, 70), 22, 22);
        dc.DrawLine(new Pen(topEdge, 2), new Point(68, 111), new Point(76, 126));
        dc.DrawLine(new Pen(topEdge, 2), new Point(112, 111), new Point(104, 126));
        dc.DrawLine(new Pen(topEdge, 1), new Point(76, 145), new Point(104, 145));
        dc.DrawRoundedRectangle(shoe, null, new Rect(48, 174, 33, 14), 7, 7);
        dc.DrawRoundedRectangle(shoe, null, new Rect(99, 174, 33, 14), 7, 7);

        var eyePen = new Pen(new SolidColorBrush(WpfColor.FromRgb(55, 48, 55)), 2.1);
        if (sleeping)
        {
            dc.DrawLine(eyePen, new Point(67, 71), new Point(77, 73));
            dc.DrawLine(eyePen, new Point(103, 73), new Point(113, 71));
        }
        else if (Math.Sin(_phase * 4.8) > 0.86)
        {
            dc.DrawLine(eyePen, new Point(67, 72), new Point(77, 72));
            dc.DrawLine(eyePen, new Point(103, 72), new Point(113, 72));
        }
        else
        {
            dc.DrawEllipse(eyePen.Brush, null, new Point(73, 71), 2.7, 3.5);
            dc.DrawEllipse(eyePen.Brush, null, new Point(107, 71), 2.7, 3.5);
        }
        dc.DrawLine(new Pen(new SolidColorBrush(WpfColor.FromRgb(206, 175, 166)), 1), new Point(90, 76), new Point(89, 82));
        var mouthPen = new Pen(new SolidColorBrush(WpfColor.FromRgb(163, 99, 108)), 1.6);
        if (responding)
            dc.DrawArc(mouthPen, new Point(82, 87), new Point(98, 87), false, true);
        else
            dc.DrawLine(mouthPen, new Point(87, 87), new Point(93, 87));
        if (responding)
        {
            var handPen = new Pen(skin, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(handPen, new Point(124, 126), new Point(147, 105 + Math.Sin(_phase * 12) * 4));
        }

        if (!FacingRight) dc.Pop();
        dc.Pop();
    }

    private static Geometry PathGeometry(string data) => Geometry.Parse(data);
}

internal static class DrawingExtensions
{
    public static void DrawArc(this DrawingContext dc, Pen pen, Point start, Point end, bool isLargeArc, bool sweepDirection)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new WpfSize(9, 7), 0, isLargeArc, sweepDirection ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }
}
