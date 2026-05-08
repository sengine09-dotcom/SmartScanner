using SmartScanner.Services;
using SmartScanner.ViewModels;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SmartScanner;

public class ZoomLevelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double zoom)
            return zoom / 100.0;
        return 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DatabaseService _db = new();

    // Drag-to-reorder state
    private PagePreviewItem? _dragItem;
    private Point _dragStartPoint;
    private bool _isDragging;

    // ── Inline canvas edit state ─────────────────────────────────────────────
    private enum EditMode { None, Text, Erase }
    private EditMode _editMode = EditMode.None;
    private TextBox? _activeEditTextBox;
    private int _editFontSize = 20;
    private Brush _editTextBrush = Brushes.Black;
    private bool _editDrawingRect;
    private Point _editRectStart;
    private Rectangle? _editCurrentRect;

    // Per-page overlay storage (keyed by PagePreviewItem reference)
    private readonly Dictionary<PagePreviewItem, List<UIElement>> _pageOverlays = new();
    private PagePreviewItem? _currentPageItem;

    private Canvas ActiveEditCanvas => _vm.FitToWindow ? MainEditCanvas : ZoomEditCanvas;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new ScannerService(), new PdfService(), new EmailService(), _db, _db);
        DataContext = _vm;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _vm.SetWindowHandle(handle);
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.RequestCommitTextEdit += CommitEditTextBox;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage))
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm.SelectedPage != null)
                    LoadPageToCanvas(_vm.SelectedPage);
                else
                {
                    CommitEditTextBox();
                    MainEditCanvas.Children.Clear();
                    ZoomEditCanvas.Children.Clear();
                    _pageOverlays.Clear();
                    _currentPageItem = null;
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.FitToWindow))
        {
            Dispatcher.Invoke(SyncCanvasContent);
        }
    }

    // ── Canvas page loading ───────────────────────────────────────────────────

    private void LoadPageToCanvas(BitmapImage bmp)
    {
        CommitEditTextBox();

        // Save overlays for the page we're leaving
        if (_currentPageItem != null)
            SavePageOverlays(_currentPageItem);

        _editMode = EditMode.None;
        BtnMainTextMode.IsChecked  = false;
        BtnMainEraseMode.IsChecked = false;
        MainEditCanvas.Children.Clear();
        ZoomEditCanvas.Children.Clear();

        // Track the new page
        _currentPageItem = _vm.PreviewPages.FirstOrDefault(p => p.IsSelected);

        // Canvas at a fixed reference width; Viewbox stretches it to fill the panel
        var imgW  = (double)bmp.PixelWidth;
        var imgH  = (double)bmp.PixelHeight;
        var scale = Math.Min(1.0, 900.0 / imgW);
        var dispW = imgW * scale;
        var dispH = imgH * scale;

        MainEditCanvas.Width  = dispW;
        MainEditCanvas.Height = dispH;
        ZoomEditCanvas.Width  = imgW;
        ZoomEditCanvas.Height = imgH;

        var bgFit = new Image { Source = bmp, Width = dispW, Height = dispH, Stretch = Stretch.Fill };
        Canvas.SetLeft(bgFit, 0);
        Canvas.SetTop(bgFit, 0);
        Panel.SetZIndex(bgFit, 0);
        MainEditCanvas.Children.Add(bgFit);

        var bgZoom = new Image { Source = bmp, Width = imgW, Height = imgH, Stretch = Stretch.Fill };
        Canvas.SetLeft(bgZoom, 0);
        Canvas.SetTop(bgZoom, 0);
        Panel.SetZIndex(bgZoom, 0);
        ZoomEditCanvas.Children.Add(bgZoom);

        // Restore overlays for the new page (if any)
        if (_currentPageItem != null)
            RestorePageOverlays(_currentPageItem);

        UpdateEditHint();
        UpdateEditCursor();
    }

    private void SyncCanvasContent()
    {
        CommitEditTextBox();

        // Clear both canvases except background
        MainEditCanvas.Children.RemoveRange(1, MainEditCanvas.Children.Count - 1);
        ZoomEditCanvas.Children.RemoveRange(1, ZoomEditCanvas.Children.Count - 1);

        // Reload the current page to both canvases
        if (_vm.SelectedPage != null)
        {
            LoadPageToCanvas(_vm.SelectedPage);
        }
    }

    private void SavePageOverlays(PagePreviewItem page)
    {
        var activeCanvas = _vm.FitToWindow ? MainEditCanvas : ZoomEditCanvas;
        if (activeCanvas.Children.Count <= 1)
        {
            _pageOverlays.Remove(page);
            return;
        }
        var overlays = new List<UIElement>();
        for (var i = 1; i < activeCanvas.Children.Count; i++)
            overlays.Add((UIElement)activeCanvas.Children[i]);
        _pageOverlays[page] = overlays;
    }

    private void RestorePageOverlays(PagePreviewItem page)
    {
        if (!_pageOverlays.TryGetValue(page, out var overlays)) return;
        var activeCanvas = _vm.FitToWindow ? MainEditCanvas : ZoomEditCanvas;
        foreach (var el in overlays)
            activeCanvas.Children.Add(el);
    }

    // ── Toolbar button handlers ───────────────────────────────────────────────

    private void BtnMainTextMode_Click(object sender, RoutedEventArgs e)
    {
        if (_editMode == EditMode.Text)
        {
            _editMode = EditMode.None;
            BtnMainTextMode.IsChecked = false;
        }
        else
        {
            CommitEditTextBox();
            _editMode = EditMode.Text;
            BtnMainEraseMode.IsChecked = false;
        }
        UpdateEditHint();
        UpdateEditCursor();
    }

    private void BtnMainEraseMode_Click(object sender, RoutedEventArgs e)
    {
        CommitEditTextBox();
        if (_editMode == EditMode.Erase)
        {
            _editMode = EditMode.None;
            BtnMainEraseMode.IsChecked = false;
        }
        else
        {
            _editMode = EditMode.Erase;
            BtnMainTextMode.IsChecked = false;
        }
        UpdateEditHint();
        UpdateEditCursor();
    }

    private void CbMainFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbMainFontSize.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var size))
            _editFontSize = size;
    }

    private void MainSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b) return;
        _editTextBrush = b.Tag?.ToString() switch
        {
            "Black" => Brushes.Black,
            "Blue"  => new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            "Red"   => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            _       => Brushes.Black,
        };
        SwatchMainBlack.BorderBrush = Brushes.Transparent;
        SwatchMainBlue.BorderBrush  = Brushes.Transparent;
        SwatchMainRed.BorderBrush   = Brushes.Transparent;
        b.BorderBrush = Brushes.White;
    }

    private void UpdateEditCursor()
    {
        MainEditCanvas.Cursor = _editMode switch
        {
            EditMode.Text  => Cursors.IBeam,
            EditMode.Erase => Cursors.Cross,
            _              => Cursors.Arrow,
        };
    }

    private void UpdateEditHint()
    {
        EditHintText.Text = _editMode switch
        {
            EditMode.Text  => "คลิกบนเอกสารเพื่อวางข้อความ  •  Enter ยืนยัน  •  Esc ยกเลิก",
            EditMode.Erase => "คลิกแล้วลากเพื่อครอบคลุมพื้นที่ที่ต้องการลบ",
            _              => "เลือกโหมดแล้วคลิกบนเอกสาร",
        };
    }

    // ── Canvas mouse events ───────────────────────────────────────────────────

    private void EditCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var canvas = ActiveEditCanvas;
        if (_editMode == EditMode.Text)
        {
            if (_activeEditTextBox != null &&
                IsEditDescendantOf(_activeEditTextBox, e.OriginalSource as DependencyObject))
                return;

            CommitEditTextBox();

            if (IsOverCommittedEditOverlay(e.OriginalSource as DependencyObject))
                return;

            PlaceEditTextBox(e.GetPosition(canvas));
            e.Handled = true;
        }
        else if (_editMode == EditMode.Erase)
        {
            if (IsOverCommittedEditOverlay(e.OriginalSource as DependencyObject))
                return;

            if (e.OriginalSource is Rectangle r && r.IsHitTestVisible)
                return;

            CommitEditTextBox();
            _editRectStart   = e.GetPosition(canvas);
            _editDrawingRect = true;
            _editCurrentRect = new Rectangle
            {
                Fill             = Brushes.White,
                Stroke           = Brushes.Gray,
                StrokeDashArray  = new DoubleCollection { 4, 2 },
                StrokeThickness  = 1,
                Width            = 1,
                Height           = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(_editCurrentRect, _editRectStart.X);
            Canvas.SetTop(_editCurrentRect,  _editRectStart.Y);
            Panel.SetZIndex(_editCurrentRect, 5);
            canvas.Children.Add(_editCurrentRect);
            canvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void EditCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_editDrawingRect || _editCurrentRect == null) return;
        var canvas = ActiveEditCanvas;
        var pos = e.GetPosition(canvas);
        var x   = Math.Min(pos.X, _editRectStart.X);
        var y   = Math.Min(pos.Y, _editRectStart.Y);
        var w   = Math.Abs(pos.X - _editRectStart.X);
        var h   = Math.Abs(pos.Y - _editRectStart.Y);
        Canvas.SetLeft(_editCurrentRect, x);
        Canvas.SetTop(_editCurrentRect,  y);
        _editCurrentRect.Width  = Math.Max(1, w);
        _editCurrentRect.Height = Math.Max(1, h);
    }

    private void EditCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_editDrawingRect) return;
        var canvas = ActiveEditCanvas;
        _editDrawingRect = false;
        canvas.ReleaseMouseCapture();
        if (_editCurrentRect != null)
        {
            if (_editCurrentRect.Width < 4 || _editCurrentRect.Height < 4)
                canvas.Children.Remove(_editCurrentRect);
            else
            {
                _editCurrentRect.Stroke = null;
                MakeEraseRectInteractive(_editCurrentRect);
            }
        }
        _editCurrentRect = null;
    }

    private void MakeEraseRectInteractive(Rectangle rect)
    {
        rect.IsHitTestVisible = true;
        rect.Cursor = Cursors.Hand;

        rect.MouseEnter += (s, e) =>
        {
            rect.Stroke = new SolidColorBrush(Color.FromArgb(160, 239, 68, 68));
            rect.StrokeDashArray = new DoubleCollection { 4, 2 };
        };
        rect.MouseLeave += (s, e) => rect.Stroke = null;

        var removeItem = new MenuItem { Header = "🗑 ลบพื้นที่ลบข้อความ" };
        removeItem.Click += (s, e) => MainEditCanvas.Children.Remove(rect);
        rect.ContextMenu = new ContextMenu { Items = { removeItem } };
    }

    // ── Text placement ────────────────────────────────────────────────────────

    private void PlaceEditTextBox(Point pos)
    {
        var canvas = ActiveEditCanvas;
        var tb = new TextBox
        {
            FontSize        = _editFontSize,
            Foreground      = _editTextBrush,
            Background      = Brushes.Transparent,
            BorderBrush     = new SolidColorBrush(Color.FromArgb(140, 30, 80, 220)),
            BorderThickness = new Thickness(1),
            MinWidth        = 60,
            MaxWidth        = canvas.Width - pos.X - 4,
            Padding         = new Thickness(3, 1, 3, 1),
            AcceptsReturn   = false,
            CaretBrush      = _editTextBrush,
        };
        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb,  pos.Y);
        Panel.SetZIndex(tb, 10);
        canvas.Children.Add(tb);
        tb.KeyDown   += EditTextBox_KeyDown;
        tb.LostFocus += EditTextBox_LostFocus;
        _activeEditTextBox = tb;
        tb.Focus();
        Keyboard.Focus(tb);
    }

    private void ReEditLabel(TextBlock label)
    {
        var canvas = ActiveEditCanvas;
        var left = Canvas.GetLeft(label);
        var top  = Canvas.GetTop(label);
        canvas.Children.Remove(label);

        var tb = new TextBox
        {
            Text            = label.Text,
            FontSize        = label.FontSize,
            Foreground      = label.Foreground,
            Background      = Brushes.Transparent,
            BorderBrush     = new SolidColorBrush(Color.FromArgb(140, 30, 80, 220)),
            BorderThickness = new Thickness(1),
            MinWidth        = 60,
            MaxWidth        = canvas.Width - left - 4,
            Padding         = new Thickness(3, 1, 3, 1),
            AcceptsReturn   = false,
            CaretBrush      = label.Foreground,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb,  top);
        Panel.SetZIndex(tb, 10);
        canvas.Children.Add(tb);
        tb.KeyDown   += EditTextBox_KeyDown;
        tb.LostFocus += EditTextBox_LostFocus;
        _activeEditTextBox = tb;
        tb.Focus();
        Keyboard.Focus(tb);
        tb.SelectAll();
    }

    private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CommitEditTextBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_activeEditTextBox != null)
            {
                MainEditCanvas.Children.Remove(_activeEditTextBox);
                _activeEditTextBox = null;
            }
            e.Handled = true;
        }
    }

    private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender == _activeEditTextBox)
            CommitEditTextBox();
    }

    private void CommitEditTextBox()
    {
        if (_activeEditTextBox == null) return;
        var canvas = ActiveEditCanvas;
        var tb   = _activeEditTextBox;
        var left = Canvas.GetLeft(tb);
        var top  = Canvas.GetTop(tb);
        _activeEditTextBox = null;
        canvas.Children.Remove(tb);
        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        var label = new TextBlock
        {
            Text      = tb.Text,
            FontSize  = tb.FontSize,
            Foreground = tb.Foreground,
            Background = Brushes.Transparent,
            Padding   = new Thickness(3, 1, 3, 1),
            Cursor    = Cursors.SizeAll,
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label,  top);
        Panel.SetZIndex(label, 8);

        Point dragOffset   = default;
        bool  draggingLabel = false;

        label.MouseEnter += (s, e) =>
            label.Background = new SolidColorBrush(Color.FromArgb(30, 30, 80, 220));
        label.MouseLeave += (s, e) =>
            label.Background = Brushes.Transparent;

        var removeItem = new MenuItem { Header = "🗑 ลบข้อความ" };
        removeItem.Click += (s, e) => canvas.Children.Remove(label);
        label.ContextMenu = new ContextMenu { Items = { removeItem } };

        label.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                draggingLabel = false;
                label.ReleaseMouseCapture();
                ReEditLabel(label);
                e.Handled = true;
                return;
            }
            dragOffset    = e.GetPosition(label);
            draggingLabel = true;
            label.CaptureMouse();
            e.Handled = true;
        };
        label.MouseMove += (s, e) =>
        {
            if (!draggingLabel) return;
            var pos = e.GetPosition(canvas);
            Canvas.SetLeft(label, pos.X - dragOffset.X);
            Canvas.SetTop(label,  pos.Y - dragOffset.Y);
        };
        label.MouseLeftButtonUp += (s, e) =>
        {
            if (!draggingLabel) return;
            draggingLabel = false;
            label.ReleaseMouseCapture();
            e.Handled = true;
        };

        canvas.Children.Add(label);
    }

    // ── Save edits ────────────────────────────────────────────────────────────

    private void BtnSaveEdits_Click(object sender, RoutedEventArgs e)
    {
        CommitEditTextBox();

        var item = _vm.PreviewPages.FirstOrDefault(p => p.IsSelected);
        if (item == null) return;

        var canvas = ActiveEditCanvas;
        canvas.UpdateLayout();

        var origW  = item.Image.PixelWidth;
        var origH  = item.Image.PixelHeight;
        var scaleX = origW / canvas.ActualWidth;
        var scaleY = origH / canvas.ActualHeight;

        var rtb = new RenderTargetBitmap(origW, origH, 96 * scaleX, 96 * scaleY, PixelFormats.Pbgra32);
        rtb.Render(canvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);

        while (canvas.Children.Count > 1)
            canvas.Children.RemoveAt(1);
        if (_currentPageItem != null)
            _pageOverlays.Remove(_currentPageItem);

        MainEditCanvas.Children.RemoveRange(1, MainEditCanvas.Children.Count - 1);
        ZoomEditCanvas.Children.RemoveRange(1, ZoomEditCanvas.Children.Count - 1);

        _vm.ReplacePage(item, ms.ToArray());
        _vm.Status = "บันทึกการแก้ไขเรียบร้อย";
    }

    // ── Canvas helpers ────────────────────────────────────────────────────────

    private static bool IsEditDescendantOf(DependencyObject ancestor, DependencyObject? node)
    {
        var el = node;
        while (el != null)
        {
            if (el == ancestor) return true;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
    }

    private bool IsOverCommittedEditOverlay(DependencyObject? node)
    {
        var canvas = ActiveEditCanvas;
        var el = node;
        while (el != null)
        {
            if (el == canvas) return false;
            if (el is TextBlock) return true;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
    }

    // ── Window events ─────────────────────────────────────────────────────────

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_vm) { Owner = this };
        win.ShowDialog();

        if (_vm.SelectedSender != null && !_vm.SenderProfiles.Contains(_vm.SelectedSender))
            _vm.SelectedSender = _vm.SenderProfiles.FirstOrDefault();
        else if (_vm.SelectedSender == null && _vm.SenderProfiles.Count > 0)
            _vm.SelectedSender = _vm.SenderProfiles[0];
    }

    private void BtnSentItems_Click(object sender, RoutedEventArgs e)
    {
        new SentItemsWindow(_db, _vm.PdfArchivePath) { Owner = this }.ShowDialog();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.SaveSettings();
    }

    // ── Thumbnail drag-to-reorder ─────────────────────────────────────────────

    private void ThumbStrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(ThumbList, e.GetPosition(ThumbList))?.VisualHit;

        if (IsTagHit(hit, "rotate-cw"))
        {
            var item = WalkToThumbItem(hit);
            if (item != null) _vm.RotatePage(item, 90);
            e.Handled = true;
            return;
        }

        if (IsTagHit(hit, "rotate-ccw"))
        {
            var item = WalkToThumbItem(hit);
            if (item != null) _vm.RotatePage(item, -90);
            e.Handled = true;
            return;
        }

        if (IsTagHit(hit, "delete"))
        {
            var item = WalkToThumbItem(hit);
            if (item != null)
            {
                _pageOverlays.Remove(item);
                if (_currentPageItem == item) _currentPageItem = null;
                _vm.DeletePage(item);
            }
            e.Handled = true;
            return;
        }

        _dragStartPoint = e.GetPosition(ThumbStrip);
        _dragItem = WalkToThumbItem(hit);
        _isDragging = false;
    }

    private void ThumbStrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;

        var pos = e.GetPosition(ThumbStrip);
        if (!_isDragging)
        {
            if (Math.Abs(pos.Y - _dragStartPoint.Y) < 8) return;
            _isDragging = true;
            _dragItem.IsDragging = true;
            Mouse.Capture(ThumbStrip);
        }

        var target = HitTestThumbItem(e.GetPosition(ThumbList));
        if (target != null && target != _dragItem)
            _vm.MovePage(_vm.PreviewPages.IndexOf(_dragItem), _vm.PreviewPages.IndexOf(target));
    }

    private void ThumbStrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            if (_dragItem != null) _dragItem.IsDragging = false;
            Mouse.Capture(null);
        }
        _dragItem = null;
        _isDragging = false;
    }

    private void ThumbStrip_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;
        _dragItem = null;
    }

    private PagePreviewItem? HitTestThumbItem(Point pointInThumbList)
        => WalkToThumbItem(VisualTreeHelper.HitTest(ThumbList, pointInThumbList)?.VisualHit);

    private static PagePreviewItem? WalkToThumbItem(DependencyObject? visual)
    {
        var el = visual;
        while (el != null)
        {
            if (el is FrameworkElement { DataContext: PagePreviewItem item }) return item;
            if (el is ItemsControl) break;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    private static bool IsTagHit(DependencyObject? visual, string tag)
    {
        var el = visual;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.Tag is string t && t == tag) return true;
            if (el is ItemsControl) break;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
    }

    // ── Zoom event handlers ───────────────────────────────────────────────────

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => _vm.ZoomInCommand.Execute(null);
    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => _vm.ZoomOutCommand.Execute(null);
    private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => _vm.ZoomResetCommand.Execute(null);
    private void BtnFitToWindow_Click(object sender, RoutedEventArgs e) => _vm.FitToWindowCommand.Execute(null);

    private void ZoomScroller_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.FitToWindow) return;
        e.Handled = true;
        if (e.Delta > 0)
            _vm.ZoomInCommand.Execute(null);
        else
            _vm.ZoomOutCommand.Execute(null);
    }
}
