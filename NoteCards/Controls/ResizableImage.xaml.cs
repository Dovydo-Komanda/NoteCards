using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NoteCards.Controls
{
    public partial class ResizableImage : UserControl
    {
        private bool _isDragging;
        private Point _clickPosition;

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register("IsSelected", typeof(bool), typeof(ResizableImage),
            new PropertyMetadata(false, OnIsSelectedChanged));

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ResizableImage control)
            {
                control.ResizeHandlesGrid.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public static readonly DependencyProperty ImageDataProperty =
            DependencyProperty.Register("ImageData", typeof(string), typeof(ResizableImage),
            new PropertyMetadata(null, OnImageDataChanged));

        public string ImageData
        {
            get => (string)GetValue(ImageDataProperty);
            set => SetValue(ImageDataProperty, value);
        }

        private static void OnImageDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ResizableImage control && e.NewValue is string base64 && !string.IsNullOrEmpty(base64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(base64);
                    using var ms = new System.IO.MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    control.InnerImage.Source = bmp;
                }
                catch { }
            }
        }

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register("Source", typeof(ImageSource), typeof(ResizableImage), 
            new PropertyMetadata(null, OnSourceChanged));

        public ImageSource Source
        {
            get => (ImageSource)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ResizableImage control)
            {
                control.InnerImage.Source = e.NewValue as ImageSource;
            }
        }

        public ResizableImage()
        {
            InitializeComponent();

            this.Focusable = true;
            this.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
            this.MouseMove += OnMouseMove;
            this.MouseLeftButtonUp += OnMouseLeftButtonUp;
            this.LostFocus += ResizableImage_LostFocus;

            TopLeftThumb.DragDelta += TopLeft_DragDelta;
            TopThumb.DragDelta += Top_DragDelta;
            TopRightThumb.DragDelta += TopRight_DragDelta;
            RightThumb.DragDelta += Right_DragDelta;
            BottomRightThumb.DragDelta += BottomRight_DragDelta;
            BottomThumb.DragDelta += Bottom_DragDelta;
            BottomLeftThumb.DragDelta += BottomLeft_DragDelta;
            LeftThumb.DragDelta += Left_DragDelta;

            // Make sure the UserControl itself can receive focus when clicked
            FocusManager.SetIsFocusScope(this, true);
        }

        private void ResizableImage_LostFocus(object sender, RoutedEventArgs e)
        {
            IsSelected = false;
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.Focus();
            IsSelected = true;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If dragging started from a Thumb, this bubbling event wouldn't fire because Thumb marks it Handled.
            // Ergo, this is the main image body being clicked.
            _isDragging = true;
            _clickPosition = e.GetPosition(this.Parent as UIElement);
            this.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && this.Parent is UIElement parent && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(parent);
                var delta = currentPosition - _clickPosition;

                var left = Canvas.GetLeft(this);
                if (double.IsNaN(left)) left = 0;

                var top = Canvas.GetTop(this);
                if (double.IsNaN(top)) top = 0;

                var newLeft = left + delta.X;
                var newTop = top + delta.Y;

                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);

                // Dynamically update the parent canvas size if we drag out of bounds
                if (parent is Canvas canvas)
                {
                    double currentCanvasHeight = double.IsNaN(canvas.Height) ? canvas.ActualHeight : canvas.Height;
                    double requiredHeight = newTop + this.Height + 50;
                    if (requiredHeight > currentCanvasHeight)
                    {
                        canvas.Height = requiredHeight;
                    }
                }

                _clickPosition = currentPosition;
                e.Handled = true;
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                this.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void Resize(double dX, double dY, bool adjustLeft, bool adjustTop)
        {
            if (Parent is not Canvas) return;

            var newWidth = Width + dX;
            var newHeight = Height + dY;

            if (newWidth < MinWidth)
            {
                dX = MinWidth - Width;
                newWidth = MinWidth;
            }
            if (newHeight < MinHeight)
            {
                dY = MinHeight - Height;
                newHeight = MinHeight;
            }

            if (adjustLeft)
            {
                var left = Canvas.GetLeft(this);
                Canvas.SetLeft(this, double.IsNaN(left) ? -dX : left - dX);
            }
            if (adjustTop)
            {
                var top = Canvas.GetTop(this);
                Canvas.SetTop(this, double.IsNaN(top) ? -dY : top - dY);
            }

            Width = newWidth;
            Height = newHeight;

            // Expand canvas height if resized downwards
            if (Parent is Canvas canvas)
            {
                double currentTop = Canvas.GetTop(this);
                if (double.IsNaN(currentTop)) currentTop = 0;

                double requiredHeight = currentTop + newHeight + 50;
                double currentCanvasHeight = double.IsNaN(canvas.Height) ? canvas.ActualHeight : canvas.Height;
                if (requiredHeight > currentCanvasHeight)
                {
                    canvas.Height = requiredHeight;
                }
            }
        }

        private void TopLeft_DragDelta(object sender, DragDeltaEventArgs e) => Resize(-e.HorizontalChange, -e.VerticalChange, true, true);
        private void Top_DragDelta(object sender, DragDeltaEventArgs e) => Resize(0, -e.VerticalChange, false, true);
        private void TopRight_DragDelta(object sender, DragDeltaEventArgs e) => Resize(e.HorizontalChange, -e.VerticalChange, false, true);
        private void Right_DragDelta(object sender, DragDeltaEventArgs e) => Resize(e.HorizontalChange, 0, false, false);
        private void BottomRight_DragDelta(object sender, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, false, false);
        private void Bottom_DragDelta(object sender, DragDeltaEventArgs e) => Resize(0, e.VerticalChange, false, false);
        private void BottomLeft_DragDelta(object sender, DragDeltaEventArgs e) => Resize(-e.HorizontalChange, e.VerticalChange, true, false);
        private void Left_DragDelta(object sender, DragDeltaEventArgs e) => Resize(-e.HorizontalChange, 0, true, false);
    }
}
