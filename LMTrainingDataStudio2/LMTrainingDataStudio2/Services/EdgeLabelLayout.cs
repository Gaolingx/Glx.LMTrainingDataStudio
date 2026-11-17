using Avalonia;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Computes collision-free positions for edge labels along a cubic bezier curve.
/// </summary>
public static class EdgeLabelLayout
{
    private static readonly double[] MidpointCandidates = { 0.5, 0.38, 0.62, 0.28, 0.72, 0.2, 0.8 };
    private static readonly double[] VerticalOffsets = { 0, -18, 18, -30, 30 };
    private const double LabelHeight = 15;

    public static Rect FindBounds(
        Point start,
        Point end,
        int textLength,
        Rect sourceBounds,
        Rect targetBounds,
        IReadOnlyList<Rect> existingLabels)
    {
        var width = Math.Max(18, textLength * 5.5 + 8);

        foreach (var t in MidpointCandidates)
        {
            var point = BezierPoint(start, end, t);
            foreach (var offsetY in VerticalOffsets)
            {
                var candidate = new Rect(point.X - width / 2, point.Y - 8 + offsetY, width, LabelHeight);
                if (sourceBounds.Intersects(candidate) ||
                    targetBounds.Intersects(candidate) ||
                    existingLabels.Any(existing => existing.Intersects(candidate)))
                {
                    continue;
                }

                return candidate;
            }
        }

        var fallback = BezierPoint(start, end, 0.5);
        return new Rect(fallback.X - width / 2, fallback.Y - 26, width, LabelHeight);
    }

    public static Point BezierPoint(Point start, Point end, double t)
    {
        var controlOffset = Math.Max(60, Math.Abs(end.X - start.X) * 0.4);
        var p1 = new Point(start.X + controlOffset, start.Y);
        var p2 = new Point(end.X - controlOffset, end.Y);
        var mt = 1 - t;
        var x = mt * mt * mt * start.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * end.X;
        var y = mt * mt * mt * start.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * end.Y;
        return new Point(x, y);
    }
}
