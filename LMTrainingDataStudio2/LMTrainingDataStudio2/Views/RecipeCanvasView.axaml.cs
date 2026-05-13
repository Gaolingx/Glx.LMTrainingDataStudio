using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using LMTrainingDataStudio2.ViewModels;
using System.Collections.Specialized;
using Path = Avalonia.Controls.Shapes.Path;

namespace LMTrainingDataStudio2.Views;

public partial class RecipeCanvasView : UserControl
{
    private Canvas? _canvas;

    public RecipeCanvasView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _canvas = this.FindControl<Canvas>("CanvasArea");
        RenderBlocks();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Blocks.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RenderBlocks);
            vm.Edges.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RenderEdges);
            RenderBlocks();
        }
    }

    private void RenderBlocks()
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;

        _canvas.Children.Clear();

        // Render edges first (below nodes)
        RenderEdges();

        // Render block nodes
        foreach (var block in vm.Blocks)
        {
            var node = CreateBlockNode(block);
            Canvas.SetLeft(node, block.X);
            Canvas.SetTop(node, block.Y);
            _canvas.Children.Add(node);
        }
    }

    private void RenderEdges()
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;

        // Remove existing edge paths
        var existingPaths = _canvas.Children.OfType<Path>().ToList();
        foreach (var p in existingPaths)
            _canvas.Children.Remove(p);

        foreach (var edge in vm.Edges)
        {
            var sourceBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.SourceBlockId);
            var targetBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.TargetBlockId);
            if (sourceBlock == null || targetBlock == null) continue;

            // Calculate connection points
            var startX = sourceBlock.X + sourceBlock.Width;
            var startY = sourceBlock.Y + 50;
            var endX = targetBlock.X;
            var endY = targetBlock.Y + 50;

            // Create bezier curve
            var path = CreateBezierEdge(startX, startY, endX, endY, edge.IsHighlighted);
            _canvas.Children.Insert(0, path); // Insert behind nodes

            // Add label if present
            if (!string.IsNullOrEmpty(edge.Label))
            {
                var label = new TextBlock
                {
                    Text = edge.Label,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
                    Padding = new Thickness(3, 1)
                };
                Canvas.SetLeft(label, (startX + endX) / 2 - 20);
                Canvas.SetTop(label, (startY + endY) / 2 - 8);
                _canvas.Children.Add(label);
            }
        }
    }

    private static Path CreateBezierEdge(double startX, double startY, double endX, double endY, bool isHighlighted)
    {
        var controlOffset = Math.Abs(endX - startX) * 0.4;

        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(startX, startY),
            IsClosed = false
        };

        figure.Segments!.Add(new BezierSegment
        {
            Point1 = new Point(startX + controlOffset, startY),
            Point2 = new Point(endX - controlOffset, endY),
            Point3 = new Point(endX, endY)
        });

        geometry.Figures!.Add(figure);

        var color = isHighlighted ? "#4A90D9" : "#AAAAAA";

        return new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Color.Parse(color)),
            StrokeThickness = 2,
            StrokeDashArray = isHighlighted ? null : new Avalonia.Collections.AvaloniaList<double> { 4, 2 }
        };
    }

    private Border CreateBlockNode(BlockNodeViewModel block)
    {
        var headerColor = block.Type switch
        {
            Models.BlockType.Seed => "#4A90D9",
            Models.BlockType.Llm => "#7B61FF",
            Models.BlockType.Expression => "#F5A623",
            Models.BlockType.Validator => "#D0021B",
            Models.BlockType.Sampler => "#417505",
            Models.BlockType.ToolProfile => "#9B9B9B",
            _ => "#4A90D9"
        };

        var header = new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Background = new SolidColorBrush(Color.Parse(headerColor)),
            Padding = new Thickness(10, 4),
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = block.IconText, FontSize = 12 },
                    new TextBlock
                    {
                        Text = block.DisplayName,
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    }
                }
            }
        };

        var body = new StackPanel
        {
            Margin = new Thickness(10, 6, 10, 8),
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = $"Name: {{{{ {block.Name} }}}}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#AAAAAA"))
                },
                new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#3A3A4E")), Margin = new Thickness(0, 2) }
            }
        };

        // Add output ports
        foreach (var port in block.OutputPorts)
        {
            body.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Children =
                {
                    new TextBlock { Text = port.Name, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#888888")) },
                    new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.Parse("#4A90D9")), Stroke = new SolidColorBrush(Color.Parse("#6AB0F9")), StrokeThickness = 1 }
                }
            });
        }

        // Add input ports
        foreach (var port in block.InputPorts)
        {
            body.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Children =
                {
                    new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.Parse("#F5A623")), Stroke = new SolidColorBrush(Color.Parse("#FFD080")), StrokeThickness = 1 },
                    new TextBlock { Text = port.Name, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#888888")) }
                }
            });
        }

        var border = new Border
        {
            Width = block.Width,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#2A2A3E")),
            BorderBrush = new SolidColorBrush(Color.Parse(block.IsSelected ? "#4A90D9" : "#3A3A4E")),
            BorderThickness = new Thickness(block.IsSelected ? 2 : 1),
            Child = new StackPanel
            {
                Children = { header, body }
            }
        };

        // Click to select
        border.PointerPressed += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                foreach (var b in vm.Blocks) b.IsSelected = false;
                block.IsSelected = true;
                vm.SelectedBlock = block;
            }
        };

        return border;
    }
}
