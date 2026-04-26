using NoteCards.Localization;
using NoteCards.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    private MindMapNode? _selectedNode;
    private readonly List<MindMapNode> _searchMatches = [];
    private string _searchQuery = string.Empty;
    private int _currentSearchMatchIndex = -1;

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
        UpdateSearchResultsText();

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
        RefreshSearchMatches(resetIndex: false);

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

        UpdateMapCanvasSize();

        DrawConnections(_root);
        DrawNodes(_root);

        if (CurrentSearchMatch is not null)
            CenterViewOnCurrentSearchMatch();

        QueueInitialCenterOnRoot();
    }

    private MindMapNode? CurrentSearchMatch
        => _currentSearchMatchIndex >= 0 && _currentSearchMatchIndex < _searchMatches.Count
            ? _searchMatches[_currentSearchMatchIndex]
            : null;

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchTextBox.Text ?? string.Empty;
        RefreshSearchMatches(resetIndex: true);
        RebuildMap();
    }

    private void SearchPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchMatches.Count == 0)
            return;

        _currentSearchMatchIndex = (_currentSearchMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        UpdateSearchResultsText();
        CenterViewOnCurrentSearchMatch();
        RebuildMap();
    }

    private void SearchNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchMatches.Count == 0)
            return;

        _currentSearchMatchIndex = (_currentSearchMatchIndex + 1) % _searchMatches.Count;
        UpdateSearchResultsText();
        CenterViewOnCurrentSearchMatch();
        RebuildMap();
    }

    private void RefreshSearchMatches(bool resetIndex)
    {
        var previousCurrent = CurrentSearchMatch;

        _searchMatches.Clear();
        var tokens = GetSearchTokens(_searchQuery);
        if (tokens.Count > 0)
        {
            foreach (var node in EnumerateVisibleNodes(_root))
            {
                if (IsSearchMatch(node, tokens))
                    _searchMatches.Add(node);
            }
        }

        if (_searchMatches.Count == 0)
        {
            _currentSearchMatchIndex = -1;
            UpdateSearchResultsText();
            return;
        }

        if (resetIndex)
        {
            _currentSearchMatchIndex = 0;
            UpdateSearchResultsText();
            return;
        }

        if (previousCurrent is not null)
        {
            var existingIndex = _searchMatches.IndexOf(previousCurrent);
            if (existingIndex >= 0)
            {
                _currentSearchMatchIndex = existingIndex;
                UpdateSearchResultsText();
                return;
            }
        }

        _currentSearchMatchIndex = Math.Clamp(_currentSearchMatchIndex, 0, _searchMatches.Count - 1);
        UpdateSearchResultsText();
    }

    private static List<string> GetSearchTokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return query
            .Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
    }

    private static bool IsSearchMatch(MindMapNode node, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return false;

        var text = node.Text ?? string.Empty;
        return tokens.All(token => text.Contains(token, StringComparison.CurrentCultureIgnoreCase));
    }

    private static IEnumerable<MindMapNode> EnumerateVisibleNodes(MindMapNode root)
    {
        yield return root;

        if (!root.IsExpanded)
            yield break;

        foreach (var child in root.Children)
        {
            foreach (var descendant in EnumerateVisibleNodes(child))
                yield return descendant;
        }
    }

    private void UpdateSearchResultsText()
    {
        if (SearchResultsTextBlock is null)
            return;

        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            SearchResultsTextBlock.Text = LocalizationService.GetString("MindMapSearchNoResults");
            return;
        }

        if (_searchMatches.Count == 0)
        {
            SearchResultsTextBlock.Text = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.GetString("MindMapSearchResultsFormat"),
                0,
                0);
            return;
        }

        SearchResultsTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.GetString("MindMapSearchResultsFormat"),
            _currentSearchMatchIndex + 1,
            _searchMatches.Count);
    }

    private void CenterViewOnCurrentSearchMatch()
    {
        if (CurrentSearchMatch is null)
            return;

        if (_layouts.TryGetValue(CurrentSearchMatch, out var layout))
            CenterViewOnLayout(layout);
    }

    private void UpdateMapCanvasSize()
    {
        if (_layouts.Count == 0)
            return;

        var zoom = Math.Max(ZoomSlider.Value, 0.1);
        var maxRight = _layouts.Values.Max(layout => layout.X + layout.Width);
        var maxBottom = _layouts.Values.Max(layout => layout.Y + layout.Height);

        MapCanvas.Width = Math.Max(MapScrollViewer.ViewportWidth / zoom, maxRight + CanvasPadding);
        MapCanvas.Height = Math.Max(MapScrollViewer.ViewportHeight / zoom, maxBottom + CanvasPadding);
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
        var targetX = (layout.X + layout.Width / 2) * zoom - MapScrollViewer.ViewportWidth / 2;
        var targetY = (layout.Y + layout.Height / 2) * zoom - MapScrollViewer.ViewportHeight / 2;

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
            CornerRadius = GetCornerRadius(node.NodeShape, isRoot),
            BorderThickness = new Thickness(node.BorderThickness > 0 ? node.BorderThickness : 1),
            BorderBrush = new SolidColorBrush(
            !string.IsNullOrWhiteSpace(node.BorderColor)
                ? (Color)ColorConverter.ConvertFromString(node.BorderColor)
                : isRoot ? Color.FromRgb(47, 92, 208) : Color.FromRgb(194, 203, 220)),
            Background = GetNodeBackground(node, layout.Depth),
            Padding = new Thickness(12, 8, 12, 8),
            Cursor = node.HasChildren ? Cursors.Hand : Cursors.Arrow,
            ToolTip = node.HasChildren
            ? LocalizationService.GetString(node.IsExpanded ? "MindMapCollapseNode" : "MindMapExpandNode")
            : null
        };

        if (_searchMatches.Contains(node))
        {
            border.BorderBrush = GetThemeBrush("MindMapSearchMatchBorderBrush", Color.FromRgb(245, 158, 11));
            border.BorderThickness = new Thickness(2);
        }

        if (ReferenceEquals(node, CurrentSearchMatch))
        {
            border.BorderBrush = GetThemeBrush("MindMapSearchActiveBorderBrush", Color.FromRgb(245, 114, 11));
            border.BorderThickness = new Thickness(3);
            border.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.2,
                BlurRadius = 14,
                ShadowDepth = 0
            };
        }

        if (ReferenceEquals(node, _selectedNode))
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 111, 232));
            border.BorderThickness = new Thickness(3);
        }

        var mainPanel = new Grid();

        // Node text
        var text = new TextBlock
        {
            Text = node.HasChildren ? $"{node.Text} {(node.IsExpanded ? "−" : "+")}" : node.Text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = isRoot ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isRoot && string.IsNullOrWhiteSpace(node.BackgroundColor)
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(31, 41, 55)),
            Margin = !string.IsNullOrWhiteSpace(node.Icon) ? new Thickness(0, 0, 28, 0) : new Thickness(0)
        };
        mainPanel.Children.Add(text);

        if (!string.IsNullOrWhiteSpace(node.Icon))
        {
            var iconBadgeColor = !string.IsNullOrWhiteSpace(node.IconBadgeColor)
            ? (Color)ColorConverter.ConvertFromString(node.IconBadgeColor)
            : Color.FromRgb(245, 158, 11); // Default amber

            var iconBadge = new Border
            {
                Background = new SolidColorBrush(iconBadgeColor),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -8, -8, 0), // Position outside the node
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.2,
                    BlurRadius = 8,
                    ShadowDepth = 2
                }
            };

            var iconText = new TextBlock
            {
                Text = node.Icon,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            iconBadge.Child = iconText;
            mainPanel.Children.Add(iconBadge);
        }

        border.Child = mainPanel;

        border.MouseLeftButtonUp += (_, e) =>
        {
            if (node.HasChildren)
            {
                node.IsExpanded = !node.IsExpanded;
                RebuildMap();
            }

            _selectedNode = node;
            RebuildMap();
            e.Handled = true;
        };

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                EditNodeText(node);
                e.Handled = true;
            }
        };

        border.ContextMenu = new ContextMenu();

        var addMenuItem = new MenuItem
        {
            Header = LocalizationService.GetString("Add"),
            Tag = node
        };
        addMenuItem.Click += (s, e) => AddNodeToParent(node);
        border.ContextMenu.Items.Add(addMenuItem);

        var editMenuItem = new MenuItem
        {
            Header = LocalizationService.GetString("Edit"),
            Tag = node
        };
        editMenuItem.Click += (s, e) => EditNodeText(node);
        border.ContextMenu.Items.Add(editMenuItem);

        var styleMenuItem = new MenuItem
        {
            Header = LocalizationService.GetString("Style-Oraganize"),
            Tag = node
        };
        styleMenuItem.Click += (s, e) => StyleNode(node);
        border.ContextMenu.Items.Add(styleMenuItem);

        border.ContextMenu.Items.Add(new Separator());

        var deleteMenuItem = new MenuItem
        {
            Header = LocalizationService.GetString("Delete"),
            Tag = node
        };
        deleteMenuItem.Click += (s, e) => DeleteNodeWithConfirmation(node);
        border.ContextMenu.Items.Add(deleteMenuItem);

        Canvas.SetLeft(border, layout.X);
        Canvas.SetTop(border, layout.Y);
        MapCanvas.Children.Add(border);

        if (!node.IsExpanded)
            return;

        foreach (var child in node.Children)
            DrawNodes(child);
    }

    private static CornerRadius GetCornerRadius(string? nodeShape, bool isRoot)
    {
        return nodeShape switch
        {
            "Circle" => new CornerRadius(95), // Approximate circle for 190x52 node
            "Ellipse" => new CornerRadius(26),
            "Rounded" => new CornerRadius(isRoot ? 14 : 10),
            _ => new CornerRadius(0) // Rectangle
        };
    }

    private Brush GetNodeBackground(MindMapNode node, int depth)
    {
        if (!string.IsNullOrWhiteSpace(node.BackgroundColor))
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(node.BackgroundColor));
        }

        return GetDefaultNodeBackground(depth);
    }

    private Brush GetDefaultNodeBackground(int depth)
    {
        if (depth <= 0)
            return _nodeBackgrounds[0];

        return _nodeBackgrounds[((depth - 1) % (_nodeBackgrounds.Length - 1)) + 1];
    }

    private void StyleNode(MindMapNode node)
    {
        var dialog = new StyleNodeDialog { Owner = this };
        dialog.LoadFromNode(node);

        if (dialog.ShowDialog() == true)
        {
            dialog.ApplyToNode(node);
            RebuildMap();
        }
    }

    private void AddNodeToParent(MindMapNode parentNode)
    {
        var dialog = new SimpleInputDialog(
            LocalizationService.GetString("Add"),
            LocalizationService.GetString("MindMapAddNodePrompt"),
            string.Empty)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var newNode = new MindMapNode
            {
                Text = NormalizeNodeText(dialog.InputText)
            };

            parentNode.Children.Add(newNode);
            parentNode.IsExpanded = true;
            RebuildMap();
        }
    }


    private void EditNodeText(MindMapNode node)
    {
        var dialog = new SimpleInputDialog(
            LocalizationService.GetString("Edit"),
            LocalizationService.GetString("MindMapEditNodePrompt"),
            node.Text)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            node.Text = NormalizeNodeText(dialog.InputText);
            RebuildMap();
        }
    }

    private void DeleteNodeWithConfirmation(MindMapNode node)
    {
        if (ReferenceEquals(node, _root))
        {
            MessageBox.Show(
                LocalizationService.GetString("MindMapCannotDeleteRoot"),
                LocalizationService.GetString("Delete"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            LocalizationService.GetString("MindMapDeleteNodeConfirmation"),
            LocalizationService.GetString("Delete"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            // Find parent and reparent children before deleting
            var parent = FindParentNode(_root, node);
            if (parent is not null)
            {
                // Move all children of the deleted node to the parent
                foreach (var child in node.Children.ToList())
                {
                    parent.Children.Add(child);
                }

                // Now remove the selected node
                parent.Children.Remove(node);

                if (ReferenceEquals(_selectedNode, node))
                    _selectedNode = null;

                RebuildMap();
            }
        }
    }

    private void AddNodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null)
        {
            MessageBox.Show(
                LocalizationService.GetString("MindMapSelectNodeToAdd"),
                LocalizationService.GetString("Add"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AddNodeToParent(_selectedNode);
    }

    private void DeleteNodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null)
        {
            MessageBox.Show(
                LocalizationService.GetString("MindMapSelectNodeToDelete"),
                LocalizationService.GetString("Delete"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DeleteNodeWithConfirmation(_selectedNode);

        if (ReferenceEquals(_selectedNode, _root))
        {
            MessageBox.Show(
                LocalizationService.GetString("MindMapCannotDeleteRoot"),
                LocalizationService.GetString("Delete"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            LocalizationService.GetString("MindMapDeleteNodeConfirmation"),
            LocalizationService.GetString("Delete"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            //  Find parent and reparent children before deleting
            var parent = FindParentNode(_root, _selectedNode);
            if (parent is not null)
            {
                // Move all children of the deleted node to the parent
                foreach (var child in _selectedNode.Children.ToList())
                {
                    parent.Children.Add(child);
                }

                // Now remove the selected node
                parent.Children.Remove(_selectedNode);
                _selectedNode = null;
                RebuildMap();
            }
        }
    }

    private MindMapNode? FindParentNode(MindMapNode root, MindMapNode target)
    {
        if (ReferenceEquals(root, target))
            return null;

        foreach (var child in root.Children)
        {
            if (ReferenceEquals(child, target))
                return root;

            var found = FindParentNode(child, target);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string NormalizeNodeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

        const int maxNodeTextLength = 110;
        if (normalized.Length > maxNodeTextLength)
            normalized = normalized[..maxNodeTextLength].TrimEnd() + "...";

        return normalized;
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
        if (MapScaleTransform is null || MapScrollViewer is null || MapCanvas is null)
            return;

        var oldZoom = e.OldValue > 0 ? e.OldValue : 1;
        var newZoom = Math.Max(e.NewValue, 0.1);
        var centerX = (MapScrollViewer.HorizontalOffset + MapScrollViewer.ViewportWidth / 2) / oldZoom;
        var centerY = (MapScrollViewer.VerticalOffset + MapScrollViewer.ViewportHeight / 2) / oldZoom;

        MapScaleTransform.ScaleX = newZoom;
        MapScaleTransform.ScaleY = newZoom;
        UpdateMapCanvasSize();
        MapCanvas.InvalidateMeasure();
        MapScrollViewer.InvalidateScrollInfo();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            MapCanvas.UpdateLayout();
            MapScrollViewer.UpdateLayout();

            var targetX = centerX * newZoom - MapScrollViewer.ViewportWidth / 2;
            var targetY = centerY * newZoom - MapScrollViewer.ViewportHeight / 2;
            MapScrollViewer.ScrollToHorizontalOffset(Math.Clamp(targetX, 0, Math.Max(0, MapScrollViewer.ScrollableWidth)));
            MapScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetY, 0, Math.Max(0, MapScrollViewer.ScrollableHeight)));
        }), DispatcherPriority.Loaded);
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

    private Brush GetThemeBrush(string resourceKey, Color fallbackColor)
    {
        if (TryFindResource(resourceKey) is Brush brush)
            return brush;

        return new SolidColorBrush(fallbackColor);
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
