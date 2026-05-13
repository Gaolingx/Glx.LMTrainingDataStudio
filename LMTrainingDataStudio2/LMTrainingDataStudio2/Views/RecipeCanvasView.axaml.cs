using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LMTrainingDataStudio2.ViewModels;
using System.Collections.Specialized;
using Path = Avalonia.Controls.Shapes.Path;

namespace LMTrainingDataStudio2.Views;

public partial class RecipeCanvasView : UserControl
{
    private Canvas? _canvas;

    // Node dragging state (left button)
    private bool _isDraggingNode;
    private BlockNodeViewModel? _draggedBlock;
    private Border? _draggedBorder;
    private Point _dragStartPoint;
    private double _dragStartX;
    private double _dragStartY;

    // Canvas panning state (middle button)
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panOffsetX;
    private double _panOffsetY;

    // Canvas transform offset
    private double _canvasOffsetX;
    private double _canvasOffsetY;

    public RecipeCanvasView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _canvas = this.FindControl<Canvas>("CanvasArea");

        if (_canvas != null)
        {
            _canvas.PointerPressed += OnCanvasPointerPressed;
            _canvas.PointerMoved += OnCanvasPointerMoved;
            _canvas.PointerReleased += OnCanvasPointerReleased;
        }

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

    #region Pointer Event Handlers

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;

        var point = e.GetPosition(_canvas);
        var props = e.GetCurrentPoint(_canvas).Properties;

        if (props.IsMiddleButtonPressed)
        {
            // Middle button: start panning
            _isPanning = true;
            _panStartPoint = point;
            _panOffsetX = _canvasOffsetX;
            _panOffsetY = _canvasOffsetY;
            e.Pointer.Capture(_canvas);
            e.Handled = true;
        }
        else if (props.IsRightButtonPressed)
        {
            // Right button: show context menu on node or canvas
            var (block, border) = HitTestBlock(point);
            if (block != null)
            {
                ShowNodeContextMenu(block, point);
            }
            else
            {
                ShowCanvasContextMenu(point);
            }
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            // Left button: select and start dragging node
            var (block, border) = HitTestBlock(point);
            if (block != null && border != null)
            {
                // Select the block
                foreach (var b in vm.Blocks) b.IsSelected = false;
                block.IsSelected = true;
                vm.SelectedBlock = block;

                // Start dragging
                _isDraggingNode = true;
                _draggedBlock = block;
                _draggedBorder = border;
                _dragStartPoint = point;
                _dragStartX = block.X;
                _dragStartY = block.Y;
                e.Pointer.Capture(_canvas);
                e.Handled = true;
            }
            else
            {
                // Clicked on empty canvas: deselect all
                foreach (var b in vm.Blocks) b.IsSelected = false;
                vm.SelectedBlock = null;
                RenderBlocks();
            }
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null) return;

        var point = e.GetPosition(_canvas);

        if (_isDraggingNode && _draggedBlock != null && _draggedBorder != null)
        {
            // Move the node
            var dx = point.X - _dragStartPoint.X;
            var dy = point.Y - _dragStartPoint.Y;

            _draggedBlock.X = _dragStartX + dx;
            _draggedBlock.Y = _dragStartY + dy;

            Canvas.SetLeft(_draggedBorder, _draggedBlock.X);
            Canvas.SetTop(_draggedBorder, _draggedBlock.Y);

            // Re-render edges to follow the node
            RenderEdges();
            e.Handled = true;
        }
        else if (_isPanning)
        {
            // Pan the canvas
            var dx = point.X - _panStartPoint.X;
            var dy = point.Y - _panStartPoint.Y;

            _canvasOffsetX = _panOffsetX + dx;
            _canvasOffsetY = _panOffsetY + dy;

            if (_canvas.RenderTransform is TranslateTransform tt)
            {
                tt.X = _canvasOffsetX;
                tt.Y = _canvasOffsetY;
            }
            else
            {
                _canvas.RenderTransform = new TranslateTransform(_canvasOffsetX, _canvasOffsetY);
            }
            e.Handled = true;
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingNode)
        {
            _isDraggingNode = false;
            _draggedBlock = null;
            _draggedBorder = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    #endregion

    #region Hit Testing

    private (BlockNodeViewModel? block, Border? border) HitTestBlock(Point point)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return (null, null);

        // Iterate in reverse to hit top-most nodes first
        for (int i = _canvas.Children.Count - 1; i >= 0; i--)
        {
            if (_canvas.Children[i] is Border border && border.Tag is BlockNodeViewModel block)
            {
                var left = Canvas.GetLeft(border);
                var top = Canvas.GetTop(border);
                var right = left + border.Bounds.Width;
                var bottom = top + border.Bounds.Height;

                if (point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom)
                {
                    return (block, border);
                }
            }
        }

        return (null, null);
    }

    #endregion

    #region Context Menus

    private void ShowNodeContextMenu(BlockNodeViewModel block, Point position)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Select the node
        foreach (var b in vm.Blocks) b.IsSelected = false;
        block.IsSelected = true;
        vm.SelectedBlock = block;
        RenderBlocks();

        var menu = new ContextMenu();

        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) =>
        {
            var newBlock = new BlockNodeViewModel
            {
                Type = block.Type,
                Name = $"{block.Name}_copy",
                DisplayName = $"{block.DisplayName} (Copy)",
                X = block.X + 40,
                Y = block.Y + 40,
            };
            foreach (var port in block.InputPorts)
                newBlock.InputPorts.Add(new PortViewModel { Name = port.Name, DataType = port.DataType });
            foreach (var port in block.OutputPorts)
                newBlock.OutputPorts.Add(new PortViewModel { Name = port.Name, DataType = port.DataType });
            vm.Blocks.Add(newBlock);
        };

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => vm.DeleteSelectedBlockCommand.Execute(null);

        var renameItem = new MenuItem { Header = "Rename" };
        renameItem.Click += (_, _) =>
        {
            // Focus on properties panel for editing
            vm.IsPropertyPanelVisible = true;
        };

        menu.Items.Add(duplicateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(renameItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);

        menu.Open(this);
    }

    private void ShowCanvasContextMenu(Point position)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var menu = new ContextMenu();

        var addSeed = new MenuItem { Header = "Add Seed Block" };
        addSeed.Click += (_, _) => AddBlockAtPosition(vm, Models.BlockType.Seed, position);

        var addLlm = new MenuItem { Header = "Add LLM / Model Block" };
        addLlm.Click += (_, _) => AddBlockAtPosition(vm, Models.BlockType.Llm, position);

        var addExpr = new MenuItem { Header = "Add Expression Block" };
        addExpr.Click += (_, _) => AddBlockAtPosition(vm, Models.BlockType.Expression, position);

        var addValidator = new MenuItem { Header = "Add Validator Block" };
        addValidator.Click += (_, _) => AddBlockAtPosition(vm, Models.BlockType.Validator, position);

        var addSampler = new MenuItem { Header = "Add Sampler Block" };
        addSampler.Click += (_, _) => AddBlockAtPosition(vm, Models.BlockType.Sampler, position);

        var addTool = new MenuItem { Header = "Add Tool Profile Block" };
        addTool.Click += (_, _) => AddBlockAtPosition(vm, Models.BlockType.ToolProfile, position);

        menu.Items.Add(addSeed);
        menu.Items.Add(addLlm);
        menu.Items.Add(addExpr);
        menu.Items.Add(addValidator);
        menu.Items.Add(addSampler);
        menu.Items.Add(addTool);

        menu.Open(this);
    }

    private static void AddBlockAtPosition(MainWindowViewModel vm, Models.BlockType type, Point position)
    {
        var block = new BlockNodeViewModel
        {
            Type = type,
            Name = $"block_{vm.Blocks.Count + 1}",
            DisplayName = $"New {type}",
            X = position.X,
            Y = position.Y,
        };
        block.OutputPorts.Add(new PortViewModel { Name = "output", DataType = "any" });
        block.InputPorts.Add(new PortViewModel { Name = "input", DataType = "any" });
        vm.Blocks.Add(block);
        vm.SelectedBlock = block;
    }

    #endregion

    #region Rendering

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

        // Remove existing edge paths and labels
        var existingPaths = _canvas.Children.OfType<Path>().ToList();
        foreach (var p in existingPaths)
            _canvas.Children.Remove(p);

        // Remove edge labels (TextBlocks without Tag)
        var existingLabels = _canvas.Children.OfType<TextBlock>()
            .Where(t => t.Tag is string s && s == "edge-label").ToList();
        foreach (var l in existingLabels)
            _canvas.Children.Remove(l);

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
                    Padding = new Thickness(3, 1),
                    Tag = "edge-label"
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
            Tag = block, // Tag with the view model for hit testing
            Child = new StackPanel
            {
                Children = { header, body }
            }
        };

        return border;
    }

    #endregion
}
