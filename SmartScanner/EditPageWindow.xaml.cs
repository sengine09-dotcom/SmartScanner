using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SmartScanner;

public partial class EditPageWindow : Window
{
    public byte[]? ResultBytes { get; private set; }

    private enum EditMode { None, Text, Erase }
    private EditMode _mode = EditMode.None;

    private readonly BitmapImage _pageImage;
    private TextBox? _activeTextBox;
    private int _fontSize = 20;
    private Brush _textBrush = Brushes.Black;

    // Erase-rectangle state
    private bool _drawingRect;
    private Point _rectStart;
    private Rectangle? _currentRect;

    public EditPageWindow(byte[] imageBytes)
    {
        InitializeComponent();
        _pageImage = BytesToBitmapImage(imageBytes);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        var imgW = (double)_pageImage.PixelWidth;
        var imgH = (double)_pageImage.PixelHeight;

        var scale = Math.Min(1.0, Math.Min((work.Width - 120) / imgW, (work.Height - 160) / imgH));
        var dispW = Math.Max(200, imgW * scale);
        var dispH = Math.Max(200, imgH * scale);

        EditCanvas.Width  = dispW;
        EditCanvas.Height = dispH;

        var bg = new Image { Source = _pageImage, Width = dispW, Height = dispH, Stretch = Stretch.Fill };
        Canvas.SetLeft(bg, 0);
        Canvas.SetTop(bg, 0);
        Panel.SetZIndex(bg, 0);
        EditCanvas.Children.Add(bg);

        Width  = Math.Min(dispW + 80,  work.Width  - 40);
        Height = Math.Min(dispH + 120, work.Height - 40);

        if (Owner != null)
        {
            Left = Owner.Left + (Owner.Width  - Width)  / 2;
            Top  = Owner.Top  + (Owner.Height - Height) / 2;
        }
    }

    // ── Mode toggles ──────────────────────────────────────────────────────────

    private void BtnTextMode_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == EditMode.Text)
        {
            _mode = EditMode.None;
            BtnTextMode.IsChecked = false;
        }
        else
        {
            CommitActiveTextBox();
            _mode = EditMode.Text;
            BtnEraseMode.IsChecked = false;
        }
        UpdateHint();
        UpdateCursor();
    }

    private void BtnEraseMode_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveTextBox();
        if (_mode == EditMode.Erase)
        {
            _mode = EditMode.None;
            BtnEraseMode.IsChecked = false;
        }
        else
        {
            _mode = EditMode.Erase;
            BtnTextMode.IsChecked = false;
        }
        UpdateHint();
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        EditCanvas.Cursor = _mode switch
        {
            EditMode.Text  => Cursors.IBeam,
            EditMode.Erase => Cursors.Cross,
            _              => Cursors.Arrow,
        };
    }

    private void UpdateHint()
    {
        HintText.Text = _mode switch
        {
            EditMode.Text  => "คลิกบนเอกสารเพื่อวางข้อความ  •  กด Enter เพื่อยืนยัน  •  กด Esc เพื่อยกเลิก",
            EditMode.Erase => "คลิกแล้วลากเพื่อครอบคลุมข้อความที่ต้องการลบ",
            _              => "เลือกโหมดแล้วคลิกบนเอกสาร",
        };
    }

    // ── Canvas mouse events ───────────────────────────────────────────────────

    private void Canvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == EditMode.Text)
        {
            // If clicking inside the active TextBox let it handle focus normally
            if (_activeTextBox != null && IsDescendantOf(_activeTextBox, e.OriginalSource as DependencyObject))
                return;

            CommitActiveTextBox();

            // Don't place a new box over a committed TextBlock
            if (IsOverCommittedOverlay(e.OriginalSource as DependencyObject))
                return;

            PlaceTextBox(e.GetPosition(EditCanvas));
            e.Handled = true;
        }
        else if (_mode == EditMode.Erase)
        {
            CommitActiveTextBox();
            _rectStart   = e.GetPosition(EditCanvas);
            _drawingRect = true;
            _currentRect = new Rectangle
            {
                Fill            = Brushes.White,
                Stroke          = Brushes.Gray,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                StrokeThickness = 1,
                Width           = 1,
                Height          = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(_currentRect, _rectStart.X);
            Canvas.SetTop(_currentRect,  _rectStart.Y);
            Panel.SetZIndex(_currentRect, 5);
            EditCanvas.Children.Add(_currentRect);
            EditCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawingRect || _currentRect == null) return;

        var pos = e.GetPosition(EditCanvas);
        var x = Math.Min(pos.X, _rectStart.X);
        var y = Math.Min(pos.Y, _rectStart.Y);
        var w = Math.Abs(pos.X - _rectStart.X);
        var h = Math.Abs(pos.Y - _rectStart.Y);

        Canvas.SetLeft(_currentRect, x);
        Canvas.SetTop(_currentRect,  y);
        _currentRect.Width  = Math.Max(1, w);
        _currentRect.Height = Math.Max(1, h);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawingRect) return;
        _drawingRect = false;
        EditCanvas.ReleaseMouseCapture();

        if (_currentRect != null)
        {
            if (_currentRect.Width < 4 || _currentRect.Height < 4)
                EditCanvas.Children.Remove(_currentRect);
            else
                _currentRect.Stroke = null; // remove dashed outline, keep white fill
        }
        _currentRect = null;
    }

    // ── Text placement ────────────────────────────────────────────────────────

    private void PlaceTextBox(Point pos)
    {
        var tb = new TextBox
        {
            FontSize        = _fontSize,
            Foreground      = _textBrush,
            Background      = Brushes.Transparent,
            BorderBrush     = new SolidColorBrush(Color.FromArgb(140, 30, 80, 220)),
            BorderThickness = new Thickness(1),
            MinWidth        = 60,
            MaxWidth        = EditCanvas.Width - pos.X - 4,
            Padding         = new Thickness(3, 1, 3, 1),
            AcceptsReturn   = false,
            CaretBrush      = _textBrush,
        };
        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb,  pos.Y);
        Panel.SetZIndex(tb, 10);
        EditCanvas.Children.Add(tb);

        tb.KeyDown   += TextBox_KeyDown;
        tb.LostFocus += TextBox_LostFocus;

        _activeTextBox = tb;
        tb.Focus();
        Keyboard.Focus(tb);
    }

    private void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CommitActiveTextBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_activeTextBox != null)
            {
                EditCanvas.Children.Remove(_activeTextBox);
                _activeTextBox = null;
            }
            e.Handled = true;
        }
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Only commit if this is still the active TextBox
        if (sender == _activeTextBox)
            CommitActiveTextBox();
    }

    private void CommitActiveTextBox()
    {
        if (_activeTextBox == null) return;

        var tb   = _activeTextBox;
        var left = Canvas.GetLeft(tb);
        var top  = Canvas.GetTop(tb);
        _activeTextBox = null;

        EditCanvas.Children.Remove(tb);

        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        var label = new TextBlock
        {
            Text            = tb.Text,
            FontSize        = tb.FontSize,
            Foreground      = tb.Foreground,
            Background      = Brushes.Transparent,
            Padding         = new Thickness(3, 1, 3, 1),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label,  top);
        Panel.SetZIndex(label, 8);
        EditCanvas.Children.Add(label);
    }

    // ── Font / color controls ─────────────────────────────────────────────────

    private void CbFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbFontSize.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var size))
            _fontSize = size;
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b) return;

        _textBrush = b.Tag?.ToString() switch
        {
            "Black" => Brushes.Black,
            "Blue"  => new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            "Red"   => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            _       => Brushes.Black,
        };

        SwatchBlack.BorderBrush = Brushes.Transparent;
        SwatchBlue.BorderBrush  = Brushes.Transparent;
        SwatchRed.BorderBrush   = Brushes.Transparent;
        b.BorderBrush = Brushes.White;
    }

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private void BtnDone_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveTextBox();
        EditCanvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(
            (int)EditCanvas.ActualWidth,
            (int)EditCanvas.ActualHeight,
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(EditCanvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ResultBytes  = ms.ToArray();
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject? node)
    {
        var el = node;
        while (el != null)
        {
            if (el == ancestor) return true;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
    }

    private bool IsOverCommittedOverlay(DependencyObject? node)
    {
        var el = node;
        while (el != null)
        {
            if (el == EditCanvas) return false;
            if (el is TextBlock) return true;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
    }

    private static BitmapImage BytesToBitmapImage(byte[] data)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(data);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
