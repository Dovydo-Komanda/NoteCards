using NoteCards.Localization;
using NoteCards.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

namespace NoteCards.Views;

public partial class MindMapPreviewWindow : Window
{
    private const double NodeWidth = 190;
    private const double HorizontalGap = 150;
    private const double VerticalGap = 18;
    private const double CanvasPadding = 52;
    private readonly MindMapNode _root;
    private readonly Dictionary<MindMapNode, NodeLayout> _layouts = new();
    private string _modelDisplayName = string.Empty;
    private bool _hasCenteredOnRootInitially;

    private readonly Brush[] _nodeBackgrounds =
    [
        new SolidColorBrush(Color.FromRgb(63, 111, 232)),
        new SolidColorBrush(Color.FromRgb(236, 253, 245)),
        new SolidColorBrush(Color.FromRgb(239, 246, 255)),
        new SolidColorBrush(Color.FromRgb(245, 243, 255)),
        new SolidColorBrush(Color.FromRgb(255, 251, 235))
    ];

    public MindMapPreviewWindow(
        MindMapNode root,
        string? modelDisplayName = null,
        string? title = null,
        IEnumerable<string>? tags = null)
    {
        _root = root;
        InitializeComponent();

        TitleTextBox.Text = string.IsNullOrWhiteSpace(title)
            ? (string.IsNullOrWhiteSpace(root.Text) ? LocalizationService.GetString("MindMapUntitled") : root.Text.Trim())
            : title.Trim();
        TagsTextBox.Text = tags is null
            ? string.Empty
            : string.Join(", ", tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()));
        ConfigureAiGeneratedIndicator(modelDisplayName);

        Loaded += (_, _) => RebuildMap();
        SizeChanged += (_, _) => RebuildMap();
    }

    public string EditorTitle => TitleTextBox.Text.Trim();

    public IReadOnlyList<string> Tags => ParseTags(TagsTextBox.Text);

    public string AiModelDisplayName => _modelDisplayName;

    public MindMapDocument ToDocument(MindMapDocument? existingDocument = null)
    {
        return new MindMapDocument
        {
            Id = existingDocument?.Id ?? Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(EditorTitle)
                ? LocalizationService.GetString("MindMapUntitled")
                : EditorTitle,
            Tags = Tags.ToList(),
            Root = _root,
            CreatedAt = existingDocument?.CreatedAt ?? DateTime.UtcNow,
            LastModified = DateTime.Now,
            AiModelDisplayName = string.IsNullOrWhiteSpace(_modelDisplayName)
                ? existingDocument?.AiModelDisplayName ?? string.Empty
                : _modelDisplayName,
            SourceNoteId = existingDocument?.SourceNoteId
        };
    }

    private void ConfigureAiGeneratedIndicator(string? modelDisplayName)
    {
        _modelDisplayName = modelDisplayName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_modelDisplayName))
        {
            AiGeneratedInfoBadge.Visibility = Visibility.Collapsed;
            AiGeneratedInfoBadge.ToolTip = null;
            return;
        }

        AiGeneratedInfoBadge.Visibility = Visibility.Visible;
        AiGeneratedInfoBadge.ToolTip = string.Format(
            LocalizationService.GetString("MindMapGeneratedWithModel"),
            _modelDisplayName);
    }

    private void RebuildMap()
    {
        _layouts.Clear();
        MapCanvas.Children.Clear();

        var step = NodeWidth + HorizontalGap;
        var rootHeight = MeasureNodeHeight(_root);
        IReadOnlyList<MindMapNode> visibleRootChildren = _root.IsExpanded
            ? _root.Children
            : Array.Empty<MindMapNode>();
        var rightChildren = visibleRootChildren
            .Where((_, index) => index % 2 == 0)
            .ToList();
        var leftChildren = visibleRootChildren
            .Where((_, index) => index % 2 == 1)
            .ToList();

        var leftDepth = leftChildren.Count == 0 ? 0 : leftChildren.Max(GetMaxVisibleDepth);
        var rootX = CanvasPadding + leftDepth * step;
        var leftHeight = MeasureChildrenHeight(leftChildren);
        var rightHeight = MeasureChildrenHeight(rightChildren);
        var totalHeight = Math.Max(rootHeight, Math.Max(leftHeight, rightHeight));
        var rootY = CanvasPadding + (totalHeight - rootHeight) / 2;

        _layouts[_root] = new NodeLayout(rootX, rootY, NodeWidth, rootHeight, Direction: 0, Depth: 0);

        AssignChildrenLayout(
            leftChildren,
            rootX,
            direction: -1,
            depth: 1,
            top: CanvasPadding + Math.Max(0, (totalHeight - leftHeight) / 2));
        AssignChildrenLayout(
            rightChildren,
            rootX,
            direction: 1,
            depth: 1,
            top: CanvasPadding + Math.Max(0, (totalHeight - rightHeight) / 2));

        NormalizeLayoutOffset();

        var maxRight = _layouts.Values.Max(layout => layout.X + layout.Width);
        var maxBottom = _layouts.Values.Max(layout => layout.Y + layout.Height);
        MapCanvas.Width = Math.Max(MapScrollViewer.ViewportWidth / Math.Max(ZoomSlider.Value, 0.1), maxRight + CanvasPadding);
        MapCanvas.Height = Math.Max(MapScrollViewer.ViewportHeight / Math.Max(ZoomSlider.Value, 0.1), maxBottom + CanvasPadding);

        DrawConnections(_root);
        DrawNodes(_root);
        QueueInitialCenterOnRoot();
    }

    private void QueueInitialCenterOnRoot()
    {
        if (_hasCenteredOnRootInitially)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_hasCenteredOnRootInitially || !_layouts.TryGetValue(_root, out var rootLayout))
                return;

            if (MapScrollViewer.ViewportWidth <= 0 || MapScrollViewer.ViewportHeight <= 0)
                return;

            CenterViewOnLayout(rootLayout);
            _hasCenteredOnRootInitially = true;
        }), DispatcherPriority.Loaded);
    }

    private void CenterViewOnRoot()
    {
        if (_layouts.TryGetValue(_root, out var rootLayout))
            CenterViewOnLayout(rootLayout);
    }

    private void CenterViewOnLayout(NodeLayout layout)
    {
        var zoom = Math.Max(ZoomSlider.Value, 0.1);
        var visibleWidth = MapScrollViewer.ViewportWidth / zoom;
        var visibleHeight = MapScrollViewer.ViewportHeight / zoom;
        var targetX = layout.X + layout.Width / 2 - visibleWidth / 2;
        var targetY = layout.Y + layout.Height / 2 - visibleHeight / 2;

        MapScrollViewer.ScrollToHorizontalOffset(Math.Clamp(targetX, 0, Math.Max(0, MapScrollViewer.ScrollableWidth)));
        MapScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetY, 0, Math.Max(0, MapScrollViewer.ScrollableHeight)));
    }

    private static double MeasureChildrenHeight(IReadOnlyList<MindMapNode> children)
    {
        if (children.Count == 0)
            return 0;

        return children.Sum(MeasureSubtree)
            + VerticalGap * Math.Max(0, children.Count - 1);
    }

    private static double MeasureSubtree(MindMapNode node)
    {
        var ownHeight = MeasureNodeHeight(node);
        if (!node.IsExpanded || node.Children.Count == 0)
            return ownHeight;

        var childrenHeight = node.Children.Sum(MeasureSubtree)
            + VerticalGap * Math.Max(0, node.Children.Count - 1);

        return Math.Max(ownHeight, childrenHeight);
    }

    private static double MeasureNodeHeight(MindMapNode node)
    {
        var lineCount = Math.Max(1, (int)Math.Ceiling(node.Text.Length / 24.0));
        return Math.Clamp(42 + (lineCount - 1) * 17, 52, 118);
    }

    private static int GetMaxVisibleDepth(MindMapNode node)
    {
        if (!node.IsExpanded || node.Children.Count == 0)
            return 1;

        return 1 + node.Children.Max(GetMaxVisibleDepth);
    }

    private void AssignChildrenLayout(
        IReadOnlyList<MindMapNode> children,
        double rootX,
        int direction,
        int depth,
        double top)
    {
        foreach (var child in children)
        {
            var childHeight = MeasureSubtree(child);
            AssignLayout(child, rootX, direction, depth, top, childHeight);
            top += childHeight + VerticalGap;
        }
    }

    private void AssignLayout(MindMapNode node, double rootX, int direction, int depth, double top, double subtreeHeight)
    {
        var ownHeight = MeasureNodeHeight(node);
        var x = rootX + direction * depth * (NodeWidth + HorizontalGap);
        var y = top + (subtreeHeight - ownHeight) / 2;
        _layouts[node] = new NodeLayout(x, y, NodeWidth, ownHeight, direction, depth);

        if (!node.IsExpanded || node.Children.Count == 0)
            return;

        var childrenHeight = MeasureChildrenHeight(node.Children);
        var childTop = top + Math.Max(0, (subtreeHeight - childrenHeight) / 2);

        foreach (var child in node.Children)
        {
            var childHeight = MeasureSubtree(child);
            AssignLayout(child, rootX, direction, depth + 1, childTop, childHeight);
            childTop += childHeight + VerticalGap;
        }
    }

    private void NormalizeLayoutOffset()
    {
        var minX = _layouts.Values.Min(layout => layout.X);
        var minY = _layouts.Values.Min(layout => layout.Y);
        var offsetX = minX < CanvasPadding ? CanvasPadding - minX : 0;
        var offsetY = minY < CanvasPadding ? CanvasPadding - minY : 0;

        if (offsetX <= 0 && offsetY <= 0)
            return;

        foreach (var (node, layout) in _layouts.ToList())
            _layouts[node] = layout with { X = layout.X + offsetX, Y = layout.Y + offsetY };
    }

    private void DrawConnections(MindMapNode node)
    {
        if (!node.IsExpanded || node.Children.Count == 0 || !_layouts.TryGetValue(node, out var parentLayout))
            return;

        foreach (var child in node.Children)
        {
            if (!_layouts.TryGetValue(child, out var childLayout))
                continue;

            var direction = childLayout.Direction < 0 ? -1 : 1;
            var start = direction < 0
                ? new Point(parentLayout.X, parentLayout.Y + parentLayout.Height / 2)
                : new Point(parentLayout.X + parentLayout.Width, parentLayout.Y + parentLayout.Height / 2);
            var end = direction < 0
                ? new Point(childLayout.X + childLayout.Width, childLayout.Y + childLayout.Height / 2)
                : new Point(childLayout.X, childLayout.Y + childLayout.Height / 2);
            var horizontalDistance = Math.Abs(end.X - start.X);
            var midOffset = Math.Max(42, horizontalDistance * 0.45);

            var figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(direction < 0
                ? new BezierSegment(
                    new Point(start.X - midOffset, start.Y),
                    new Point(end.X + midOffset, end.Y),
                    end,
                    isStroked: true)
                : new BezierSegment(
                    new Point(start.X + midOffset, start.Y),
                    new Point(end.X - midOffset, end.Y),
                    end,
                    isStroked: true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var path = new ShapePath
            {
                Data = geometry,
                Stroke = new SolidColorBrush(Color.FromRgb(145, 158, 183)),
                StrokeThickness = 2,
                Opacity = 0.78
            };

            MapCanvas.Children.Add(path);
            DrawConnections(child);
        }
    }

    private void DrawNodes(MindMapNode node)
    {
        if (!_layouts.TryGetValue(node, out var layout))
            return;

        var isRoot = ReferenceEquals(node, _root);
        var border = new Border
        {
            Width = layout.Width,
            Height = layout.Height,
            CornerRadius = new CornerRadius(isRoot ? 14 : 10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(194, 203, 220)),
            Background = GetNodeBackground(layout.Depth),
            Padding = new Thickness(12, 8, 12, 8),
            Cursor = node.HasChildren ? Cursors.Hand : Cursors.Arrow,
            ToolTip = node.HasChildren
                ? LocalizationService.GetString(node.IsExpanded ? "MindMapCollapseNode" : "MindMapExpandNode")
                : null
        };

        if (isRoot)
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(47, 92, 208));

        var text = new TextBlock
        {
            Text = node.HasChildren ? $"{node.Text} {(node.IsExpanded ? "−" : "+")}" : node.Text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = isRoot ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isRoot
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(31, 41, 55))
        };

        border.Child = text;
        if (node.HasChildren)
        {
            border.MouseLeftButtonUp += (_, e) =>
            {
                node.IsExpanded = !node.IsExpanded;
                RebuildMap();
                e.Handled = true;
            };
        }

        Canvas.SetLeft(border, layout.X);
        Canvas.SetTop(border, layout.Y);
        MapCanvas.Children.Add(border);

        if (!node.IsExpanded)
            return;

        foreach (var child in node.Children)
            DrawNodes(child);
    }

    private Brush GetNodeBackground(int depth)
    {
        if (depth <= 0)
            return _nodeBackgrounds[0];

        return _nodeBackgrounds[((depth - 1) % (_nodeBackgrounds.Length - 1)) + 1];
    }

    private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetExpanded(_root, true);
        RebuildMap();
    }

    private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var child in _root.Children)
            SetExpanded(child, false);

        _root.IsExpanded = true;
        RebuildMap();
    }

    private static void SetExpanded(MindMapNode node, bool isExpanded)
    {
        node.IsExpanded = isExpanded;
        foreach (var child in node.Children)
            SetExpanded(child, isExpanded);
    }

    private void ResetZoomButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 1;
        Dispatcher.BeginInvoke(new Action(CenterViewOnRoot), DispatcherPriority.Loaded);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MapScaleTransform is null)
            return;

        MapScaleTransform.ScaleX = e.NewValue;
        MapScaleTransform.ScaleY = e.NewValue;
    }

    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;

        var delta = e.Delta > 0 ? 0.08 : -0.08;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static IReadOnlyList<string> ParseTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
            return Array.Empty<string>();

        return rawTags
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record NodeLayout(double X, double Y, double Width, double Height, int Direction, int Depth);
}
