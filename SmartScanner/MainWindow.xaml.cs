using SmartScanner.Services;
using SmartScanner.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmartScanner;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DatabaseService _db = new();

    // Drag-to-reorder state
    private PagePreviewItem? _dragItem;
    private Point _dragStartPoint;
    private bool _isDragging;

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
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_vm) { Owner = this };
        win.ShowDialog();

        // Ensure selected sender is still valid after editing
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
            if (item != null) _vm.DeletePage(item);
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
        if (_isDragging) return;  // captured — leave is spurious, keep drag alive
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
}
