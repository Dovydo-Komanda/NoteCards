using NoteCards.Localization;
using NoteCards.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class StyleNodeDialog : Window
    {
        public string? BackgroundColor { get; set; }
        public string? BorderColor { get; set; }
        public new double BorderThickness { get; set; } = 1.0;
        public string? NodeShape { get; set; } = "Rectangle";
        public new string? Icon { get; set; }
        public string? IconBadgeColor { get; set; } = "#F59E0B"; // Default amber

        private Button? _selectedIconButton;

    private bool _isClosingAnimationRunning;
    private bool _pendingDialogResult;

    public StyleNodeDialog()
    {
        InitializeComponent();
        Loaded += StyleNodeDialog_Loaded;

        if (FindName("BorderThicknessSlider") is Slider slider)
        {
            slider.ValueChanged += BorderThicknessSlider_ValueChanged;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyOwnerBounds();
    }

    private void ApplyOwnerBounds()
    {
        OverlayDialogBoundsHelper.Apply(this);
    }

    private void StyleNodeDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyOwnerBounds();
        BeginOpenAnimation();
    }

        public void LoadFromNode(MindMapNode node)
        {
            BackgroundColor = node.BackgroundColor;
            BorderColor = node.BorderColor;
            BorderThickness = node.BorderThickness;
            NodeShape = node.NodeShape ?? "Rectangle";
            Icon = node.Icon;
            IconBadgeColor = node.IconBadgeColor ?? "#F59E0B";

            // Update UI for background color
            if (!string.IsNullOrWhiteSpace(BackgroundColor))
            {
                if (FindName("BackgroundColorButton") is Button bgButton)
                    bgButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BackgroundColor));
            }
            else
            {
                if (FindName("BackgroundColorButton") is Button bgButton2)
                    bgButton2.Background = Brushes.Transparent;
            }

            // Update UI for border color
            if (!string.IsNullOrWhiteSpace(BorderColor))
            {
                if (FindName("BorderColorButton") is Button borderButton)
                    borderButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BorderColor));
            }
            else
            {
                if (FindName("BorderColorButton") is Button borderButton2)
                    borderButton2.Background = Brushes.Transparent;
            }

            // Update UI for icon badge color
            if (!string.IsNullOrWhiteSpace(IconBadgeColor))
            {
                if (FindName("IconBadgeColorButton") is Button iconBadgeButton)
                    iconBadgeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IconBadgeColor));
            }
            else
            {
                if (FindName("IconBadgeColorButton") is Button iconBadgeButton2)
                    iconBadgeButton2.Background = Brushes.Transparent;
            }

            // Update UI for border thickness
            if (FindName("BorderThicknessSlider") is Slider thicknessSlider)
            {
                thicknessSlider.Value = BorderThickness;
            }

            if (FindName("BorderThicknessText") is TextBlock thicknessText)
            {
                thicknessText.Text = BorderThickness.ToString();
            }

            // Update UI for shape
            if (FindName("ShapeComboBox") is ComboBox shapeCombo)
            {
                shapeCombo.SelectedValue = NodeShape;
            }

            // Highlight the current icon button if node has an icon
            if (!string.IsNullOrWhiteSpace(Icon))
            {
                HighlightSelectedIconButton(Icon);
            }
        }

        public void ApplyToNode(MindMapNode node)
        {
            node.BackgroundColor = BackgroundColor;
            node.BorderColor = BorderColor;
            node.BorderThickness = BorderThickness;
            node.NodeShape = NodeShape;
            node.Icon = Icon;
            node.IconBadgeColor = IconBadgeColor;
        }

        private void BackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker();
            if (color.HasValue)
            {
                BackgroundColor = color.Value.ToString();
                if (FindName("BackgroundColorButton") is Button button)
                    button.Background = new SolidColorBrush(color.Value);
            }
        }

        private void ClearBackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            BackgroundColor = null;
            if (FindName("BackgroundColorButton") is Button button)
                button.Background = Brushes.Transparent;
        }

        private void BorderColorButton_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker();
            if (color.HasValue)
            {
                BorderColor = color.Value.ToString();
                if (FindName("BorderColorButton") is Button button)
                    button.Background = new SolidColorBrush(color.Value);
            }
        }

        private void ClearBorderColorButton_Click(object sender, RoutedEventArgs e)
        {
            BorderColor = null;
            if (FindName("BorderColorButton") is Button button)
                button.Background = Brushes.Transparent;
        }

        private void IconBadgeColorButton_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker();
            if (color.HasValue)
            {
                IconBadgeColor = color.Value.ToString();
                if (FindName("IconBadgeColorButton") is Button button)
                    button.Background = new SolidColorBrush(color.Value);
            }
        }

        private void ClearIconBadgeColorButton_Click(object sender, RoutedEventArgs e)
        {
            IconBadgeColor = null;
            if (FindName("IconBadgeColorButton") is Button button)
                button.Background = Brushes.Transparent;
        }

        private void BorderThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            BorderThickness = e.NewValue;
            if (FindName("BorderThicknessText") is TextBlock text)
                text.Text = BorderThickness.ToString();
        }

        private void ShapeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FindName("ShapeComboBox") is ComboBox combo && combo.SelectedValue is string shape)
            {
                NodeShape = shape;
            }
        }

        private void IconButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Content is not string icon)
                return;

            Icon = icon;
            ClearIconButtonHighlighting();
            HighlightButtonAsSelected(button);
            _selectedIconButton = button;
        }

        private void CustomIconButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SimpleInputDialog(
                LocalizationService.GetString("SelectIcon"),
                LocalizationService.GetString("EnterCustomIconEmoji"),
                Icon ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                Icon = dialog.InputText.Trim();
                ClearIconButtonHighlighting();
                _selectedIconButton = null;
            }
        }

        private void ClearIconButton_Click(object sender, RoutedEventArgs e)
        {
            Icon = null;
            ClearIconButtonHighlighting();
            _selectedIconButton = null;
        }

        private void HighlightButtonAsSelected(Button button)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(232, 240, 254));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            button.BorderThickness = new Thickness(2);
        }

        private void ClearIconButtonHighlighting()
        {
            if (FindName("IconGrid") is WrapPanel iconGrid)
            {
                foreach (var child in iconGrid.Children)
                {
                    if (child is Button button)
                    {
                        button.Background = Brushes.White;
                        button.BorderBrush = FindResource("BorderColor") as Brush ?? Brushes.Gray;
                        button.BorderThickness = new Thickness(1);
                    }
                }
            }
        }

        private void HighlightSelectedIconButton(string icon)
        {
            if (FindName("IconGrid") is WrapPanel iconGrid)
            {
                foreach (var child in iconGrid.Children)
                {
                    if (child is Button button && button.Content?.ToString() == icon)
                    {
                        HighlightButtonAsSelected(button);
                        _selectedIconButton = button;
                        break;
                    }
                }
            }
        }

        private Color? ShowColorPicker()
        {
            var colors = new[]
            {
                Colors.White, Colors.Black,
                Color.FromRgb(239, 68, 68), Color.FromRgb(249, 115, 22),
                Color.FromRgb(245, 158, 11), Color.FromRgb(34, 197, 94),
                Color.FromRgb(59, 130, 246), Color.FromRgb(139, 92, 246),
                Color.FromRgb(236, 72, 153), Color.FromRgb(244, 63, 94),
                Color.FromRgb(209, 213, 219), Color.FromRgb(107, 114, 128)
            };

            Color? selectedColor = null;

            var dialog = new Window
            {
                Title = LocalizationService.GetString("SelectColorKey"), // Updated localization key
                Width = 320,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.White
            };

            var panel = new WrapPanel { Margin = new Thickness(16) };

            foreach (var color in colors)
            {
                var button = new Button
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(4),
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Tag = color
                };

                button.Click += (s, e) =>
                {
                    selectedColor = color;
                    dialog.DialogResult = true;
                    dialog.Close();
                };

                panel.Children.Add(button);
            }

            dialog.Content = panel;

            if (dialog.ShowDialog() == true)
            {
                return selectedColor;
            }

            return null;
        }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FindName("BorderThicknessSlider") is Slider slider)
            BorderThickness = slider.Value;

        BeginCloseAnimation(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        BeginCloseAnimation(false);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            BeginCloseAnimation(false);
            e.Handled = true;
        }
    }

    private void BeginOpenAnimation()
    {
        Opacity = 0;

        if (DialogCard.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            DialogCard.RenderTransform = translate;
        }

        translate.Y = 12;

        var duration = TimeSpan.FromMilliseconds(190);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

    private void BeginCloseAnimation(bool dialogResult)
    {
        if (_isClosingAnimationRunning)
            return;

        _isClosingAnimationRunning = true;
        _pendingDialogResult = dialogResult;

        if (DialogCard.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            DialogCard.RenderTransform = translate;
        }

        var duration = TimeSpan.FromMilliseconds(150);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fadeOut = new DoubleAnimation(Opacity, 0, duration) { EasingFunction = ease };
        fadeOut.Completed += (_, _) =>
        {
            DialogResult = _pendingDialogResult;
            Close();
        };

        BeginAnimation(OpacityProperty, fadeOut);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 8, duration) { EasingFunction = ease });
    }
    }
}