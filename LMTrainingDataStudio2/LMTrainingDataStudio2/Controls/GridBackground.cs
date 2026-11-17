using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LMTrainingDataStudio2.Controls;

/// <summary>
/// Canvas grid that follows pan/zoom. The spacing is multiplied until lines
/// stay readable at very small zoom levels.
/// </summary>
public sealed class GridBackground : Control
{
    public static readonly StyledProperty<double> GridSizeProperty =
        AvaloniaProperty.Register<GridBackground, double>(nameof(GridSize), 16);

    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<GridBackground, double>(nameof(Scale), 1);

    public static readonly StyledProperty<double> OffsetXProperty =
        AvaloniaProperty.Register<GridBackground, double>(nameof(OffsetX));

    public static readonly StyledProperty<double> OffsetYProperty =
        AvaloniaProperty.Register<GridBackground, double>(nameof(OffsetY));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<GridBackground, IBrush?>(nameof(LineBrush));

    static GridBackground()
    {
        AffectsRender<GridBackground>(
            GridSizeProperty, ScaleProperty, OffsetXProperty, OffsetYProperty, LineBrushProperty);
    }

    public double GridSize
    {
        get => GetValue(GridSizeProperty);
        set => SetValue(GridSizeProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public double OffsetX
    {
        get => GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    public double OffsetY
    {
        get => GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (LineBrush is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var scale = Math.Max(0.05, Scale);
        var spacing = Math.Max(1, GridSize) * scale;
        while (spacing < 6)
        {
            spacing *= 2;
        }

        var pen = new Pen(LineBrush, 1);
        var startX = OffsetX % spacing;
        if (startX > 0) startX -= spacing;
        for (var x = startX; x <= Bounds.Width; x += spacing)
        {
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        }

        var startY = OffsetY % spacing;
        if (startY > 0) startY -= spacing;
        for (var y = startY; y <= Bounds.Height; y += spacing)
        {
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
        }
    }
}
