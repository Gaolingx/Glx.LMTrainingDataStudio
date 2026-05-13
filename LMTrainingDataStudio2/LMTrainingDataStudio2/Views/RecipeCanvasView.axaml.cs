using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.ViewModels;
using Path = Avalonia.Controls.Shapes.Path;

namespace LMTrainingDataStudio2.Views;

public partial class RecipeCanvasView : UserControl
{
    private const string DragBlockFormat = "application/x-lmtds-block-type";

    private Canvas? _canvas;
    private Border? _marquee;
    private Border? _nodeMenu;
    private ContextMenu? _contextMenu;
    private Path? _previewEdge;

    private bool _isDraggingNode;
    private bool _isPanning;
    private bool _isSelecting;
    private bool _isConnecting;
    private Point _dragStartPoint;
    private Point _panStartPoint;
    private Point _selectionStartPoint;
    private double _dragStartX;
    private double _dragStartY;
    private double _canvasOffsetX;
    private double _canvasOffsetY;
    private double _panOffsetX;
    private double _panOffsetY;
    private BlockNodeViewModel? _draggedBlock;
    private Border? _draggedBorder;
    private BlockNodeViewModel? _connectionSourceBlock;
    private PortViewModel? _connectionSourcePort;
    private Point _lastCanvasMenuPosition = new(300, 200);
    private readonly List<BlockNodeViewModel> _clipboard = new();

    public RecipeCanvasView()
    {
        InitializeComponent();
        Focusable = true;
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _canvas = this.FindControl<Canvas>("CanvasArea");

        if (_canvas != null)
        {
            DragDrop.SetAllowDrop(_canvas, true);
            _canvas.PointerPressed += OnCanvasPointerPressed;
            _canvas.PointerMoved += OnCanvasPointerMoved;
            _canvas.PointerReleased += OnCanvasPointerReleased;
            _canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
            _canvas.AddHandler(DragDrop.DropEvent, OnCanvasDrop);
        }

        KeyDown += OnKeyDown;
        RenderBlocks();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Blocks.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RenderBlocks);
            vm.Edges.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RenderBlocks);
            RenderBlocks();
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;

        Focus();
        HideNodeMenu();
        CloseContextMenu();

        var point = e.GetPosition(_canvas);
        var props = e.GetCurrentPoint(_canvas).Properties;

        if (props.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panStartPoint = point;
            _panOffsetX = _canvasOffsetX;
            _panOffsetY = _canvasOffsetY;
            e.Pointer.Capture(_canvas);
            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed)
        {
            _lastCanvasMenuPosition = point;
            var (block, _) = HitTestBlock(point);
            if (block != null) ShowNodeContextMenu(block);
            else ShowCanvasContextMenu(point);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        if (HitTestOutputPort(point) is { } outputHit)
        {
            ClearEdgeSelection(vm);
            _isConnecting = true;
            _connectionSourceBlock = outputHit.Block;
            _connectionSourcePort = outputHit.Port;
            _previewEdge = CreateBezierEdge(outputHit.Center.X, outputHit.Center.Y, point.X, point.Y, true, null);
            _previewEdge.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 6, 3 };
            _canvas.Children.Insert(0, _previewEdge);
            e.Pointer.Capture(_canvas);
            e.Handled = true;
            return;
        }

        if (HitTestEdge(point) is { } edge)
        {
            foreach (var b in vm.Blocks) b.IsSelected = false;
            vm.SelectedBlock = null;
            foreach (var e2 in vm.Edges)
            {
                e2.IsSelected = false;
                e2.IsHighlighted = false;
            }
            edge.IsSelected = true;
            edge.IsHighlighted = true;
            RenderBlocks();
            e.Handled = true;
            return;
        }

        var (hitBlock, hitBorder) = HitTestBlock(point);
        if (hitBlock != null && hitBorder != null)
        {
            ClearEdgeSelection(vm);
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                foreach (var b in vm.Blocks) b.IsSelected = false;
            }
            hitBlock.IsSelected = true;
            vm.SelectedBlock = hitBlock;

            _isDraggingNode = true;
            _draggedBlock = hitBlock;
            _draggedBorder = hitBorder;
            _dragStartPoint = point;
            _dragStartX = hitBlock.X;
            _dragStartY = hitBlock.Y;
            e.Pointer.Capture(_canvas);
            RenderBlocks();
            e.Handled = true;
            return;
        }

        ClearEdgeSelection(vm);
        foreach (var b in vm.Blocks) b.IsSelected = false;
        vm.SelectedBlock = null;
        _isSelecting = true;
        _selectionStartPoint = point;
        ShowMarquee(point, point);
        e.Pointer.Capture(_canvas);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null) return;
        var point = e.GetPosition(_canvas);

        if (_isConnecting && _previewEdge != null && _connectionSourceBlock != null)
        {
            var start = GetOutputPortCenter(_connectionSourceBlock, _connectionSourcePort);
            _previewEdge.Data = CreateBezierGeometry(start.X, start.Y, point.X, point.Y);
            e.Handled = true;
            return;
        }

        if (_isDraggingNode && _draggedBlock != null && _draggedBorder != null)
        {
            var dx = point.X - _dragStartPoint.X;
            var dy = point.Y - _dragStartPoint.Y;
            var selected = GetSelectedBlocks().ToList();

            if (selected.Count > 1 && selected.Contains(_draggedBlock))
            {
                foreach (var block in selected)
                {
                    block.X += dx;
                    block.Y += dy;
                }
                _dragStartPoint = point;
            }
            else
            {
                _draggedBlock.X = _dragStartX + dx;
                _draggedBlock.Y = _dragStartY + dy;
            }

            RenderBlocks();
            e.Handled = true;
            return;
        }

        if (_isSelecting)
        {
            ShowMarquee(_selectionStartPoint, point);
            SelectBlocksInRectangle(_selectionStartPoint, point);
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            var dx = point.X - _panStartPoint.X;
            var dy = point.Y - _panStartPoint.Y;
            _canvasOffsetX = _panOffsetX + dx;
            _canvasOffsetY = _panOffsetY + dy;
            _canvas.RenderTransform = new TranslateTransform(_canvasOffsetX, _canvasOffsetY);
            e.Handled = true;
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var point = e.GetPosition(_canvas);

        if (_isConnecting)
        {
            var target = HitTestInputPort(point);
            if (_connectionSourceBlock != null && _connectionSourcePort != null && target != null && target.Value.Block != _connectionSourceBlock)
            {
                vm.Edges.Add(new EdgeViewModel
                {
                    SourceBlockId = _connectionSourceBlock.Id,
                    SourcePortId = _connectionSourcePort.Id,
                    TargetBlockId = target.Value.Block.Id,
                    TargetPortId = target.Value.Port.Id,
                    Label = _connectionSourcePort.DataType
                });
            }
            _isConnecting = false;
            _connectionSourceBlock = null;
            _connectionSourcePort = null;
            _previewEdge = null;
            RenderBlocks();
        }

        if (_isSelecting)
        {
            HideMarquee();
            _isSelecting = false;
        }

        _isDraggingNode = false;
        _draggedBlock = null;
        _draggedBorder = null;
        _isPanning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DragBlockFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var text = e.Data.Get(DragBlockFormat) as string;
        if (Enum.TryParse<BlockType>(text, out var type))
        {
            AddBlockAtPosition(vm, type, e.GetPosition(_canvas));
            RenderBlocks();
        }
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == Key.Oem3)
        {
            ShowSearchableNodeMenu(_lastCanvasMenuPosition);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelection(vm);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            _clipboard.Clear();
            _clipboard.AddRange(GetSelectedBlocks().Select(CloneBlock));
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
        {
            PasteClipboard(vm);
            e.Handled = true;
        }
    }

    private (BlockNodeViewModel Block, PortViewModel Port, Point Center)? HitTestOutputPort(Point point) => HitTestPort(point, true);

    private (BlockNodeViewModel Block, PortViewModel Port, Point Center)? HitTestInputPort(Point point) => HitTestPort(point, false);

    private (BlockNodeViewModel Block, PortViewModel Port, Point Center)? HitTestPort(Point point, bool output)
    {
        if (DataContext is not MainWindowViewModel vm) return null;
        foreach (var block in vm.Blocks.AsEnumerable().Reverse())
        {
            var ports = output ? block.OutputPorts : block.InputPorts;
            foreach (var port in ports)
            {
                var center = output ? GetOutputPortCenter(block, port) : GetInputPortCenter(block, port);
                if (Distance(point, center) <= 10) return (block, port, center);
            }
        }
        return null;
    }

    private (BlockNodeViewModel? block, Border? border) HitTestBlock(Point point)
    {
        if (_canvas == null) return (null, null);
        for (var i = _canvas.Children.Count - 1; i >= 0; i--)
        {
            if (_canvas.Children[i] is Border { Tag: BlockNodeViewModel block } border)
            {
                var left = Canvas.GetLeft(border);
                var top = Canvas.GetTop(border);
                var height = Math.Max(border.Bounds.Height, block.Height);
                if (point.X >= left && point.X <= left + block.Width && point.Y >= top && point.Y <= top + height)
                    return (block, border);
            }
        }
        return (null, null);
    }

    private EdgeViewModel? HitTestEdge(Point point)
    {
        if (DataContext is not MainWindowViewModel vm) return null;
        foreach (var edge in vm.Edges.AsEnumerable().Reverse())
        {
            var source = vm.Blocks.FirstOrDefault(b => b.Id == edge.SourceBlockId);
            var target = vm.Blocks.FirstOrDefault(b => b.Id == edge.TargetBlockId);
            if (source == null || target == null) continue;
            var start = GetOutputPortCenter(source, source.OutputPorts.FirstOrDefault(p => p.Id == edge.SourcePortId));
            var end = GetInputPortCenter(target, target.InputPorts.FirstOrDefault(p => p.Id == edge.TargetPortId));
            if (DistanceToSegment(point, start, end) <= 8) return edge;
        }
        return null;
    }

    private void ShowNodeContextMenu(BlockNodeViewModel block)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        CloseContextMenu();
        foreach (var b in vm.Blocks) b.IsSelected = false;
        block.IsSelected = true;
        vm.SelectedBlock = block;
        RenderBlocks();

        var menu = new ContextMenu();
        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) => PasteBlocks(vm, new[] { CloneBlock(block) }, new Point(block.X + 40, block.Y + 40));
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => DeleteSelection(vm);
        var renameItem = new MenuItem { Header = "Rename" };
        renameItem.Click += (_, _) => vm.IsPropertyPanelVisible = true;
        menu.Items.Add(duplicateItem);
        menu.Items.Add(renameItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        _contextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_contextMenu, menu)) _contextMenu = null;
        };
        menu.Open(this);
    }

    private void ShowCanvasContextMenu(Point position)
    {
        CloseContextMenu();
        var menu = new ContextMenu();
        var addItem = new MenuItem { Header = "Add item" };
        addItem.Click += (_, _) => ShowSearchableNodeMenu(position);
        menu.Items.Add(addItem);
        _contextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_contextMenu, menu)) _contextMenu = null;
        };
        menu.Open(this);
    }

    private void ShowSearchableNodeMenu(Point position)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        HideNodeMenu();

        var search = new TextBox
        {
            Watermark = "Search block...",
            Margin = new Thickness(8, 8, 8, 4),
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            Foreground = Brushes.White
        };
        var list = new ListBox
        {
            Height = 190,
            Margin = new Thickness(8, 0, 8, 8),
            Background = new SolidColorBrush(Color.Parse("#202033")),
            Foreground = Brushes.White
        };

        void Refresh(string? query = null)
        {
            list.Items.Clear();
            foreach (var template in vm.BlockTemplates.Where(t => string.IsNullOrWhiteSpace(query) || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                list.Items.Add(new ListBoxItem { Content = $"{template.IconText}  {template.Name}", Tag = template.Type });
            }
        }

        search.TextChanged += (_, _) => Refresh(search.Text);
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: BlockType type })
            {
                AddBlockAtPosition(vm, type, position);
                HideNodeMenu();
                RenderBlocks();
            }
        };

        _nodeMenu = new Border
        {
            Width = 240,
            Background = new SolidColorBrush(Color.Parse("#28283A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4A90D9")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = "node-menu",
            Child = new StackPanel { Children = { search, list } }
        };
        Canvas.SetLeft(_nodeMenu, position.X);
        Canvas.SetTop(_nodeMenu, position.Y);
        _canvas.Children.Add(_nodeMenu);
        Refresh();
        search.Focus();
    }

    private void HideNodeMenu()
    {
        if (_canvas != null && _nodeMenu != null) _canvas.Children.Remove(_nodeMenu);
        _nodeMenu = null;
    }

    private void CloseContextMenu()
    {
        if (_contextMenu != null)
        {
            _contextMenu.Close();
            _contextMenu = null;
        }
    }

    private void ShowMarquee(Point start, Point end)
    {
        if (_canvas == null) return;
        _marquee ??= new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(35, 74, 144, 217)),
            BorderBrush = new SolidColorBrush(Color.Parse("#4A90D9")),
            BorderThickness = new Thickness(1),
            Tag = "marquee"
        };
        if (!_canvas.Children.Contains(_marquee)) _canvas.Children.Add(_marquee);
        Canvas.SetLeft(_marquee, Math.Min(start.X, end.X));
        Canvas.SetTop(_marquee, Math.Min(start.Y, end.Y));
        _marquee.Width = Math.Abs(end.X - start.X);
        _marquee.Height = Math.Abs(end.Y - start.Y);
    }

    private void HideMarquee()
    {
        if (_canvas != null && _marquee != null) _canvas.Children.Remove(_marquee);
    }

    private void SelectBlocksInRectangle(Point start, Point end)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        foreach (var block in vm.Blocks)
        {
            block.IsSelected = block.X < right && block.X + block.Width > left && block.Y < bottom && block.Y + block.Height > top;
        }
    }

    private void DeleteSelection(MainWindowViewModel vm)
    {
        var selectedEdges = vm.Edges.Where(e => e.IsSelected || e.IsHighlighted).ToList();
        foreach (var edge in selectedEdges) vm.Edges.Remove(edge);

        var selectedBlocks = vm.Blocks.Where(b => b.IsSelected).ToList();
        foreach (var block in selectedBlocks)
        {
            foreach (var edge in vm.Edges.Where(e => e.SourceBlockId == block.Id || e.TargetBlockId == block.Id).ToList()) vm.Edges.Remove(edge);
            vm.Blocks.Remove(block);
        }
        vm.SelectedBlock = null;
        RenderBlocks();
    }

    private IEnumerable<BlockNodeViewModel> GetSelectedBlocks()
        => DataContext is MainWindowViewModel vm ? vm.Blocks.Where(b => b.IsSelected) : Enumerable.Empty<BlockNodeViewModel>();

    private void PasteClipboard(MainWindowViewModel vm)
    {
        if (_clipboard.Count == 0) return;
        PasteBlocks(vm, _clipboard, new Point(_lastCanvasMenuPosition.X + 24, _lastCanvasMenuPosition.Y + 24));
        RenderBlocks();
    }

    private static void PasteBlocks(MainWindowViewModel vm, IEnumerable<BlockNodeViewModel> sourceBlocks, Point position)
    {
        var blocks = sourceBlocks.Select(CloneBlock).ToList();
        if (blocks.Count == 0) return;
        var minX = blocks.Min(b => b.X);
        var minY = blocks.Min(b => b.Y);
        foreach (var b in vm.Blocks) b.IsSelected = false;
        foreach (var block in blocks)
        {
            block.Id = Guid.NewGuid().ToString("N");
            block.Name = $"{block.Name}_copy";
            block.X = position.X + (block.X - minX);
            block.Y = position.Y + (block.Y - minY);
            block.IsSelected = true;
            vm.Blocks.Add(block);
        }
        vm.SelectedBlock = blocks.LastOrDefault();
    }

    private static BlockNodeViewModel CloneBlock(BlockNodeViewModel block)
    {
        var clone = new BlockNodeViewModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = block.Type,
            Name = block.Name,
            DisplayName = block.DisplayName,
            X = block.X,
            Y = block.Y,
            Width = block.Width,
            Height = block.Height
        };
        foreach (var p in block.InputPorts) clone.InputPorts.Add(new PortViewModel { Name = p.Name, DataType = p.DataType });
        foreach (var p in block.OutputPorts) clone.OutputPorts.Add(new PortViewModel { Name = p.Name, DataType = p.DataType });
        return clone;
    }

    private static void AddBlockAtPosition(MainWindowViewModel vm, BlockType type, Point position)
    {
        foreach (var b in vm.Blocks) b.IsSelected = false;
        var block = new BlockNodeViewModel
        {
            Type = type,
            Name = $"block_{vm.Blocks.Count + 1}",
            DisplayName = type switch
            {
                BlockType.Seed => "Training Data",
                BlockType.Llm => "LLM / Model",
                BlockType.Expression => "Expression",
                BlockType.Validator => "Validator",
                BlockType.Sampler => "Sampler",
                BlockType.ToolProfile => "Tool Profile",
                _ => $"New {type}"
            },
            X = position.X,
            Y = position.Y,
            IsSelected = true
        };
        block.InputPorts.Add(new PortViewModel { Name = "input", DataType = "any" });
        block.OutputPorts.Add(new PortViewModel { Name = "output", DataType = "any" });
        vm.Blocks.Add(block);
        vm.SelectedBlock = block;
    }

    private static void ClearEdgeSelection(MainWindowViewModel vm)
    {
        foreach (var edge in vm.Edges)
        {
            edge.IsSelected = false;
            edge.IsHighlighted = false;
        }
    }

    private void RenderBlocks()
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        _canvas.Children.Clear();
        foreach (var edge in vm.Edges) RenderEdge(edge);
        foreach (var block in vm.Blocks)
        {
            var node = CreateBlockNode(block);
            Canvas.SetLeft(node, block.X);
            Canvas.SetTop(node, block.Y);
            _canvas.Children.Add(node);
        }
    }

    private void RenderEdge(EdgeViewModel edge)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var sourceBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.SourceBlockId);
        var targetBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.TargetBlockId);
        if (sourceBlock == null || targetBlock == null) return;
        var start = GetOutputPortCenter(sourceBlock, sourceBlock.OutputPorts.FirstOrDefault(p => p.Id == edge.SourcePortId));
        var end = GetInputPortCenter(targetBlock, targetBlock.InputPorts.FirstOrDefault(p => p.Id == edge.TargetPortId));
        var path = CreateBezierEdge(start.X, start.Y, end.X, end.Y, edge.IsHighlighted || edge.IsSelected, edge);
        _canvas.Children.Add(path);
        if (!string.IsNullOrEmpty(edge.Label))
        {
            var label = new TextBlock
            {
                Text = edge.Label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.Parse(edge.IsSelected ? "#FFFFFF" : "#888888")),
                Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
                Padding = new Thickness(3, 1),
                Tag = "edge-label"
            };
            Canvas.SetLeft(label, (start.X + end.X) / 2 - 20);
            Canvas.SetTop(label, (start.Y + end.Y) / 2 - 8);
            _canvas.Children.Add(label);
        }
    }

    private static Path CreateBezierEdge(double startX, double startY, double endX, double endY, bool isHighlighted, EdgeViewModel? edge)
        => new()
        {
            Data = CreateBezierGeometry(startX, startY, endX, endY),
            Stroke = new SolidColorBrush(Color.Parse(isHighlighted ? "#4A90D9" : "#AAAAAA")),
            StrokeThickness = isHighlighted ? 4 : 2,
            StrokeDashArray = isHighlighted ? null : new Avalonia.Collections.AvaloniaList<double> { 4, 2 },
            Tag = edge
        };

    private static Geometry CreateBezierGeometry(double startX, double startY, double endX, double endY)
    {
        var controlOffset = Math.Max(60, Math.Abs(endX - startX) * 0.4);
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(startX, startY), IsClosed = false };
        figure.Segments!.Add(new BezierSegment
        {
            Point1 = new Point(startX + controlOffset, startY),
            Point2 = new Point(endX - controlOffset, endY),
            Point3 = new Point(endX, endY)
        });
        geometry.Figures!.Add(figure);
        return geometry;
    }

    private Border CreateBlockNode(BlockNodeViewModel block)
    {
        var headerColor = block.Type switch
        {
            BlockType.Seed => "#4A90D9",
            BlockType.Llm => "#7B61FF",
            BlockType.Expression => "#F5A623",
            BlockType.Validator => "#D0021B",
            BlockType.Sampler => "#417505",
            BlockType.ToolProfile => "#9B9B9B",
            _ => "#4A90D9"
        };

        var body = new StackPanel
        {
            Margin = new Thickness(10, 6, 10, 8),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = $"Name: {{{{ {block.Name} }}}}", FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#AAAAAA")) },
                new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#3A3A4E")), Margin = new Thickness(0, 2) }
            }
        };

        foreach (var port in block.OutputPorts)
        {
            body.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Children = { new TextBlock { Text = port.Name, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#888888")) }, CreatePortEllipse("output-port") }
            });
        }

        foreach (var port in block.InputPorts)
        {
            body.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Children = { CreatePortEllipse("input-port"), new TextBlock { Text = port.Name, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#888888")) } }
            });
        }

        return new Border
        {
            Width = block.Width,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#2A2A3E")),
            BorderBrush = new SolidColorBrush(Color.Parse(block.IsSelected ? "#4A90D9" : "#3A3A4E")),
            BorderThickness = new Thickness(block.IsSelected ? 2 : 1),
            Tag = block,
            Child = new StackPanel
            {
                Children =
                {
                    new Border
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
                                new TextBlock { Text = block.DisplayName, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }
                            }
                        }
                    },
                    body
                }
            }
        };
    }

    private static Ellipse CreatePortEllipse(string tag)
        => new()
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.Parse(tag == "output-port" ? "#4A90D9" : "#F5A623")),
            Stroke = new SolidColorBrush(Color.Parse(tag == "output-port" ? "#6AB0F9" : "#FFD080")),
            StrokeThickness = 1,
            Tag = tag
        };

    private static Point GetOutputPortCenter(BlockNodeViewModel block, PortViewModel? port)
    {
        var index = port == null ? 0 : Math.Max(0, block.OutputPorts.IndexOf(port));
        return new Point(block.X + block.Width - 15, block.Y + 55 + index * 18);
    }

    private static Point GetInputPortCenter(BlockNodeViewModel block, PortViewModel? port)
    {
        var index = port == null ? 0 : Math.Max(0, block.InputPorts.IndexOf(port));
        return new Point(block.X + 15, block.Y + 55 + block.OutputPorts.Count * 18 + index * 18);
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return Distance(p, a);
        var t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy)));
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }
}
