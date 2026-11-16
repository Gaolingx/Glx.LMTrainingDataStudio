namespace LMTrainingDataStudio2.Services;

public readonly record struct CanvasBounds(double X, double Y, double Width, double Height);

public static class CanvasSnap
{
    public static double Snap(double value, double gridSize) =>
        gridSize <= 0 ? value : Math.Round(value / gridSize) * gridSize;
}

/// <summary>
/// Screen/world transform for the recipe canvas. The transform is
/// <c>screen = world * scale + offset</c>.
/// </summary>
public sealed class CanvasViewport
{
    public const double MinScale = 0.25;
    public const double MaxScale = 2.5;
    public const double ScaleStep = 0.1;

    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public void Set(double scale, double offsetX, double offsetY)
    {
        Scale = Math.Clamp(scale, MinScale, MaxScale);
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public void PanBy(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
    }

    /// <summary>
    /// Zooms around a screen-space anchor so the world point under the pointer stays put.
    /// </summary>
    public void ZoomAt(double pointerX, double pointerY, double delta)
    {
        var target = Math.Clamp(Scale + delta, MinScale, MaxScale);
        if (Math.Abs(target - Scale) < 0.0001) return;

        var (worldX, worldY) = ScreenToWorld(pointerX, pointerY);
        Scale = target;
        OffsetX = pointerX - worldX * Scale;
        OffsetY = pointerY - worldY * Scale;
    }

    public (double X, double Y) ScreenToWorld(double x, double y) =>
        ((x - OffsetX) / Scale, (y - OffsetY) / Scale);

    public (double X, double Y) WorldToScreen(double x, double y) =>
        (x * Scale + OffsetX, y * Scale + OffsetY);

    public void CenterOn(double worldX, double worldY, double viewportWidth, double viewportHeight)
    {
        OffsetX = viewportWidth / 2 - worldX * Scale;
        OffsetY = viewportHeight / 2 - worldY * Scale;
    }

    /// <summary>
    /// Fits all bounds into the viewport with padding.
    /// </summary>
    public void Fit(
        IEnumerable<CanvasBounds> bounds,
        double viewportWidth,
        double viewportHeight,
        double padding = 60)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var list = bounds.ToList();
        if (list.Count == 0)
        {
            Set(1, 0, 0);
            return;
        }

        var minX = list.Min(item => item.X);
        var minY = list.Min(item => item.Y);
        var maxX = list.Max(item => item.X + item.Width);
        var maxY = list.Max(item => item.Y + item.Height);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);

        var availableWidth = Math.Max(1, viewportWidth - padding * 2);
        var availableHeight = Math.Max(1, viewportHeight - padding * 2);
        var scale = Math.Clamp(
            Math.Min(availableWidth / spanX, availableHeight / spanY),
            MinScale, MaxScale);

        Scale = scale;
        OffsetX = (viewportWidth - spanX * scale) / 2 - minX * scale;
        OffsetY = (viewportHeight - spanY * scale) / 2 - minY * scale;
    }
}