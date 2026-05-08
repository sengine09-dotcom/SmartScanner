using SmartScanner.Models;
using SmartScanner.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SmartScanner;

public partial class SentItemsWindow : Window
{
    private readonly ISentItemsService _svc;
    private readonly string            _archivePath;

    public SentItemsWindow(ISentItemsService svc, string archivePath)
    {
        InitializeComponent();
        _svc         = svc;
        _archivePath = archivePath;
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        var items = _svc.GetAll();
        DgSentItems.ItemsSource = items;

        TbCount.Text = items.Count == 0
            ? "ยังไม่มีรายการที่ส่ง"
            : $"รวม {items.Count} รายการ";

        BtnClearAll.IsEnabled = items.Count > 0;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadData();

    private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int id) return;

        var items = _svc.GetAll();
        var item  = items.FirstOrDefault(x => x.Id == id);
        if (item != null) DeleteFile(item.FileName);

        _svc.Delete(id);
        LoadData();
    }

    private void BtnOpenRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not SentItem item) return;

        if (string.IsNullOrEmpty(_archivePath))
        {
            MessageBox.Show("ยังไม่ได้ตั้งค่าโฟลเดอร์จัดเก็บไฟล์\nกรุณาตั้งค่าในเมนู ตั้งค่า → จัดเก็บไฟล์",
                "ไม่พบโฟลเดอร์", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = Path.Combine(_archivePath, item.FileName);
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else
            MessageBox.Show($"ไม่พบไฟล์:\n{path}", "ไม่พบไฟล์", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BtnClearAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "ต้องการลบรายการทั้งหมดและไฟล์ PDF ที่เกี่ยวข้องใช่หรือไม่?", "ยืนยันการลบ",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        foreach (var item in _svc.GetAll())
            DeleteFile(item.FileName);

        _svc.ClearAll();
        LoadData();
    }

    private void DeleteFile(string fileName)
    {
        if (string.IsNullOrEmpty(_archivePath) || string.IsNullOrEmpty(fileName)) return;
        try
        {
            var path = Path.Combine(_archivePath, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
