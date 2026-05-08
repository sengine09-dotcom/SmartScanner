using Microsoft.Win32;
using SmartScanner.Models;
using SmartScanner.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartScanner.ViewModels;

public class PagePreviewItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isDragging;
    private int _pageNumber;
    private BitmapImage _image = null!;

    public BitmapImage Image
    {
        get => _image;
        set { _image = value; Notify(nameof(Image)); }
    }

    public int PageNumber
    {
        get => _pageNumber;
        set { _pageNumber = value; Notify(nameof(PageNumber)); Notify(nameof(PageNumberLabel)); }
    }

    public string PageNumberLabel => $"หน้าที่ {PageNumber}";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; Notify(nameof(IsSelected)); }
    }

    public bool IsDragging
    {
        get => _isDragging;
        set { _isDragging = value; Notify(nameof(IsDragging)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => canExecute?.Invoke(p) ?? true;
    public void Execute(object? p) => execute(p);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IScannerService   _scannerService;
    private readonly IPdfService       _pdfService;
    private readonly IEmailService     _emailService;
    private readonly ISettingsService  _settingsService;
    private readonly ISentItemsService _sentItemsService;

    private string _status = "Ready";
    private bool _isBusy;
    private string _selectedScanner = string.Empty;
    private string _fileName = "Scan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
    private string _recipient = string.Empty;
    private string _subject = "Scanned Document";
    private string _body = "Please find the scanned document attached.";
    private int _selectedDpi = 300;
    private string _selectedColorMode = "Color";
    private SenderProfile?    _selectedSender;
    private RecipientProfile? _selectedRecipient;
    private EmailProfile?     _selectedEmailProfile;
    private IntPtr _windowHandle;
    private List<byte[]> _scannedBytes = new();
    private BitmapImage? _selectedPage;
    private double _zoomLevel = 100;
    private bool _fitToWindow = true;

    // SMTP shared settings (edited in SettingsWindow only)
    public string SmtpHost      { get; set; } = "mail.asianonlinegroup.co.th";
    public int    SmtpPort      { get; set; } = 587;
    public bool   SmtpUseSsl   { get; set; } = true;
    public string PdfArchivePath { get; set; } = string.Empty;

    public ObservableCollection<string> Scanners { get; } = new();
    public ObservableCollection<int> DpiOptions { get; } = new([75, 150, 200, 300, 600]);
    public ObservableCollection<string> ColorModes { get; } = new(["Color", "Grayscale", "BlackWhite"]);
    public ObservableCollection<PagePreviewItem> PreviewPages { get; } = new();
    public ObservableCollection<SenderProfile>   SenderProfiles    { get; } = new();
    public ObservableCollection<RecipientProfile> RecipientProfiles { get; } = new();
    public ObservableCollection<EmailProfile>    EmailProfiles     { get; } = new();

    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsBusy { get => _isBusy; set { Set(ref _isBusy, value); RaiseAll(); } }
    public bool HasPreview => PreviewPages.Count > 0;
    public bool NoPreview => PreviewPages.Count == 0;
    public string PageCount => PreviewPages.Count == 0 ? "" : $"{PreviewPages.Count} page(s) scanned";
    public BitmapImage? SelectedPage { get => _selectedPage; set => Set(ref _selectedPage, value); }

    public string SelectedScanner { get => _selectedScanner; set => Set(ref _selectedScanner, value); }
    public string FileName { get => _fileName; set => Set(ref _fileName, value); }
    public string Recipient { get => _recipient; set => Set(ref _recipient, value); }
    public string Subject { get => _subject; set => Set(ref _subject, value); }
    public string Body { get => _body; set => Set(ref _body, value); }
    public int SelectedDpi { get => _selectedDpi; set => Set(ref _selectedDpi, value); }
    public string SelectedColorMode { get => _selectedColorMode; set => Set(ref _selectedColorMode, value); }
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            Set(ref _zoomLevel, value);
            OnPropertyChanged(nameof(ZoomPercentage));
        }
    }
    public bool FitToWindow { get => _fitToWindow; set => Set(ref _fitToWindow, value); }
    public string ZoomPercentage => $"{ZoomLevel:F0}%";
    public SenderProfile? SelectedSender
    {
        get => _selectedSender;
        set { Set(ref _selectedSender, value); SendCommand.RaiseCanExecuteChanged(); }
    }

    public RecipientProfile? SelectedRecipient
    {
        get => _selectedRecipient;
        set { Set(ref _selectedRecipient, value); if (value != null) Recipient = value.Email; }
    }

    public EmailProfile? SelectedEmailProfile
    {
        get => _selectedEmailProfile;
        set
        {
            Set(ref _selectedEmailProfile, value);
            if (value == null) return;
            // Auto-fill fields from profile
            var sender = SenderProfiles.FirstOrDefault(s => s.Username == value.SenderUsername);
            if (sender != null) SelectedSender = sender;
            // Match recipient in list (updates both SelectedRecipient and Recipient)
            var rec = RecipientProfiles.FirstOrDefault(r => r.Email == value.RecipientEmail);
            if (rec != null) SelectedRecipient = rec;
            else Recipient = value.RecipientEmail;
            Subject = value.Subject;
            Body    = value.Body;
        }
    }

    public bool IsDarkMode { get => _isDarkMode; private set => Set(ref _isDarkMode, value); }
    private bool _isDarkMode;

    public RelayCommand ScanCommand { get; }
    public RelayCommand InsertPdfCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand SavePdfCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand RefreshScannersCommand { get; }
    public RelayCommand SelectPageCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ZoomResetCommand { get; }
    public RelayCommand FitToWindowCommand { get; }

    public MainViewModel(IScannerService scannerService, IPdfService pdfService, IEmailService emailService, ISettingsService settingsService, ISentItemsService sentItemsService)
    {
        _scannerService   = scannerService;
        _pdfService       = pdfService;
        _emailService     = emailService;
        _settingsService  = settingsService;
        _sentItemsService = sentItemsService;

        ScanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(SelectedScanner));
        InsertPdfCommand = new RelayCommand(_ => _ = InsertPdfAsync(), _ => !IsBusy);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        SavePdfCommand = new RelayCommand(_ => _ = SavePdfAsync(), _ => !IsBusy && HasPreview);
        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => !IsBusy && HasPreview && SelectedSender != null);
        ClearCommand = new RelayCommand(_ => ClearPreview(), _ => !IsBusy && HasPreview);
        RefreshScannersCommand = new RelayCommand(_ => RefreshScanners(), _ => !IsBusy);
        SelectPageCommand = new RelayCommand(p => SelectPage(p as PagePreviewItem));
        ZoomInCommand = new RelayCommand(_ => ZoomIn(), _ => HasPreview);
        ZoomOutCommand = new RelayCommand(_ => ZoomOut(), _ => HasPreview);
        ZoomResetCommand = new RelayCommand(_ => ZoomReset(), _ => HasPreview);
        FitToWindowCommand = new RelayCommand(_ => SetFitToWindow(), _ => HasPreview);

        LoadSettings();
        RefreshScanners();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        _selectedDpi = s.SelectedDpi;
        _selectedColorMode = s.SelectedColorMode;
        _selectedScanner = s.LastScanner;

        SmtpHost       = s.SmtpHost;
        SmtpPort       = s.SmtpPort;
        SmtpUseSsl     = s.SmtpUseSsl;
        PdfArchivePath = s.PdfArchivePath;

        SenderProfiles.Clear();
        foreach (var p in s.Senders)
            SenderProfiles.Add(p);

        _selectedSender = SenderProfiles.FirstOrDefault(p => p.Username == s.LastSenderUsername)
                          ?? SenderProfiles.FirstOrDefault();

        RecipientProfiles.Clear();
        foreach (var r in s.Recipients)
            RecipientProfiles.Add(r);

        EmailProfiles.Clear();
        foreach (var ep in s.EmailProfiles)
            EmailProfiles.Add(ep);

        _isDarkMode = s.IsDarkMode;
        var uri = new Uri(_isDarkMode ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dicts = Application.Current.Resources.MergedDictionaries;
        dicts.Clear();
        dicts.Add(new System.Windows.ResourceDictionary { Source = uri });
    }

    public void SaveSettings()
    {
        _settingsService.Save(new AppSettings
        {
            LastScanner = SelectedScanner,
            SelectedDpi = SelectedDpi,
            SelectedColorMode = SelectedColorMode,
            SmtpHost       = SmtpHost,
            SmtpPort       = SmtpPort,
            SmtpUseSsl     = SmtpUseSsl,
            PdfArchivePath = PdfArchivePath,
            Senders = SenderProfiles.ToList(),
            LastSenderUsername = SelectedSender?.Username ?? string.Empty,
            Recipients    = RecipientProfiles.ToList(),
            EmailProfiles = EmailProfiles.ToList(),
            IsDarkMode    = IsDarkMode,
        });
    }

    public void SetWindowHandle(IntPtr handle) => _windowHandle = handle;

    public void RefreshScanners()
    {
        var last = SelectedScanner;
        Scanners.Clear();
        foreach (var s in _scannerService.GetAvailableScanners())
            Scanners.Add(s);

        if (!string.IsNullOrEmpty(last) && Scanners.Contains(last))
            SelectedScanner = last;
        else if (Scanners.Count > 0)
            SelectedScanner = Scanners[0];

        Status = Scanners.Count > 0 ? $"พบ {Scanners.Count} เครื่องสแกน" : "ไม่พบเครื่องสแกน";
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedScanner)) { Status = "กรุณาเลือกเครื่องสแกน"; return; }
        IsBusy = true;
        try
        {
            Status = "กำลังสแกนเอกสาร...";
            var pages = await _scannerService.ScanAsync(SelectedScanner, SelectedDpi, SelectedColorMode, _windowHandle);
            if (pages.Count == 0) { Status = "ไม่ได้รับข้อมูลจากเครื่องสแกน"; return; }

            var startIndex = PreviewPages.Count;
            _scannedBytes.AddRange(pages);
            var pageNum = startIndex + 1;
            foreach (var page in pages)
                PreviewPages.Add(new PagePreviewItem { Image = BytesToBitmapImage(page), PageNumber = pageNum++ });
            SelectPage(PreviewPages[startIndex]);

            NotifyPreview();
            Status = $"เพิ่ม {pages.Count} หน้า — รวมทั้งหมด {PreviewPages.Count} หน้า";
        }
        catch (Exception ex)
        {
            Status = $"ข้อผิดพลาดการสแกน: {ex.Message}";
            MessageBox.Show(ex.Message, "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        var uri = new Uri(IsDarkMode ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dicts = Application.Current.Resources.MergedDictionaries;
        dicts.Clear();
        dicts.Add(new System.Windows.ResourceDictionary { Source = uri });
        SaveSettings();
    }

    private async Task InsertPdfAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "เลือกไฟล์ PDF",
            Filter = "PDF Document (*.pdf)|*.pdf",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var startIndex = PreviewPages.Count;
            var totalAdded = 0;

            foreach (var filePath in dialog.FileNames)
            {
                Status = $"กำลังโหลด {Path.GetFileName(filePath)}...";
                var pages = await LoadPdfPagesAsync(filePath);
                _scannedBytes.AddRange(pages);
                foreach (var page in pages)
                    PreviewPages.Add(new PagePreviewItem { Image = BytesToBitmapImage(page), PageNumber = PreviewPages.Count + 1 });
                totalAdded += pages.Count;
            }

            if (totalAdded == 0) { Status = "ไม่พบหน้าใน PDF"; return; }
            SelectPage(PreviewPages[startIndex]);
            NotifyPreview();
            Status = $"เพิ่ม {totalAdded} หน้า จาก {dialog.FileNames.Length} ไฟล์ — รวมทั้งหมด {PreviewPages.Count} หน้า";
        }
        catch (Exception ex)
        {
            Status = $"ข้อผิดพลาดโหลด PDF: {ex.Message}";
            MessageBox.Show(ex.Message, "PDF Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private static async Task<List<byte[]>> LoadPdfPagesAsync(string pdfPath)
    {
        var pages = new List<byte[]>();
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

        for (uint i = 0; i < pdfDoc.PageCount; i++)
        {
            using var page = pdfDoc.GetPage(i);
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(ms, new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = 2000
            });
            var bytes = new byte[ms.Size];
            var reader = new Windows.Storage.Streams.DataReader(ms.GetInputStreamAt(0));
            await reader.LoadAsync((uint)ms.Size);
            reader.ReadBytes(bytes);
            pages.Add(bytes);
        }

        return pages;
    }

    private async Task SavePdfAsync()
    {
        if (_scannedBytes.Count == 0) { Status = "ไม่มีเอกสาร กรุณาสแกนก่อน"; return; }

        var dialog = new SaveFileDialog
        {
            Title = "บันทึกเอกสาร",
            Filter = "PDF Document (*.pdf)|*.pdf",
            FileName = FileName,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            Status = "กำลังบันทึก PDF...";
            await _pdfService.CreatePdfAsync(_scannedBytes, dialog.FileName);
            Status = $"บันทึกเรียบร้อย: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Status = $"ข้อผิดพลาดการบันทึก: {ex.Message}";
            MessageBox.Show(ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private async Task SendAsync()
    {
        if (_scannedBytes.Count == 0) { Status = "ไม่มีเอกสาร กรุณาสแกนก่อน"; return; }
        if (string.IsNullOrWhiteSpace(Recipient)) { Status = "กรุณาระบุอีเมลผู้รับ"; return; }
        if (SelectedSender == null) { Status = "กรุณาเลือกอีเมลผู้ส่ง"; return; }

        IsBusy = true;
        var tempPath = Path.Combine(Path.GetTempPath(), $"{FileName}.pdf");
        try
        {
            Status = $"กำลังสร้าง PDF ({_scannedBytes.Count} หน้า)...";
            await _pdfService.CreatePdfAsync(_scannedBytes, tempPath);

            Status = "กำลังส่งอีเมล...";
            var settings = new EmailSettings
            {
                SmtpHost = SmtpHost,
                SmtpPort = SmtpPort,
                UseSsl = SmtpUseSsl,
                Username = SelectedSender.Username,
                Password = SelectedSender.Password,
                SenderEmail = SelectedSender.SenderEmail,
                DisplayName = SelectedSender.DisplayName,
            };
            await _emailService.SendAsync(settings, Recipient, Subject, Body, tempPath);

            // Archive PDF to configured folder with a unique filename
            var archivedName = $"{FileName}.pdf";
            if (!string.IsNullOrWhiteSpace(PdfArchivePath))
            {
                try
                {
                    Directory.CreateDirectory(PdfArchivePath);
                    var destPath = UniqueFilePath(PdfArchivePath, archivedName);
                    File.Copy(tempPath, destPath, overwrite: false);
                    archivedName = Path.GetFileName(destPath);
                }
                catch { /* archive failure must not block the success status */ }
            }

            _sentItemsService.Add(new SentItem
            {
                SentAt    = DateTime.Now,
                FromName  = SelectedSender.DisplayName,
                FromEmail = SelectedSender.SenderEmail,
                ToEmail   = Recipient,
                Subject   = Subject,
                FileName  = archivedName,
            });

            Status = $"ส่ง '{FileName}.pdf' ถึง {Recipient} เรียบร้อยแล้ว";
            FileName = "Scan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            ClearPreview();
        }
        catch (Exception ex)
        {
            Status = $"ข้อผิดพลาดการส่ง: {ex.Message}";
            MessageBox.Show(ex.Message, "Send Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private void SelectPage(PagePreviewItem? item)
    {
        if (item == null) return;
        foreach (var p in PreviewPages) p.IsSelected = false;
        item.IsSelected = true;
        SelectedPage = item.Image;
    }

    public void DeletePage(PagePreviewItem item)
    {
        var index = PreviewPages.IndexOf(item);
        if (index < 0) return;
        var deletedNum = item.PageNumber;

        PreviewPages.RemoveAt(index);
        _scannedBytes.RemoveAt(index);

        for (var i = 0; i < PreviewPages.Count; i++)
            PreviewPages[i].PageNumber = i + 1;

        if (item.IsSelected)
        {
            if (PreviewPages.Count > 0)
                SelectPage(PreviewPages[Math.Min(index, PreviewPages.Count - 1)]);
            else
                SelectedPage = null;
        }

        NotifyPreview();
        Status = PreviewPages.Count > 0
            ? $"ลบหน้า {deletedNum} แล้ว — เหลือ {PreviewPages.Count} หน้า"
            : "ล้างเอกสารเรียบร้อย";
    }

    public byte[] GetPageBytes(int index) => _scannedBytes[index];

    public void ReplacePage(PagePreviewItem item, byte[] newBytes)
    {
        var index = PreviewPages.IndexOf(item);
        if (index < 0) return;
        _scannedBytes[index] = newBytes;
        item.Image = BytesToBitmapImage(newBytes);
        if (item.IsSelected)
            SelectedPage = item.Image;
    }

    public void RotatePage(PagePreviewItem item, double degrees = 90)
    {
        var index = PreviewPages.IndexOf(item);
        if (index < 0) return;

        var rotated = new TransformedBitmap(item.Image, new RotateTransform(degrees));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rotated));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        var rotatedBytes = ms.ToArray();

        _scannedBytes[index] = rotatedBytes;
        item.Image = BytesToBitmapImage(rotatedBytes);

        if (item.IsSelected)
            SelectedPage = item.Image;
    }

    public void MovePage(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex || fromIndex < 0 || toIndex < 0
            || fromIndex >= PreviewPages.Count || toIndex >= PreviewPages.Count) return;

        PreviewPages.Move(fromIndex, toIndex);

        var bytes = _scannedBytes[fromIndex];
        _scannedBytes.RemoveAt(fromIndex);
        _scannedBytes.Insert(toIndex, bytes);

        for (var i = 0; i < PreviewPages.Count; i++)
            PreviewPages[i].PageNumber = i + 1;
    }

    private void ClearPreview()
    {
        _scannedBytes.Clear();
        PreviewPages.Clear();
        SelectedPage = null;
        NotifyPreview();
        Status = "ล้างข้อมูลเรียบร้อย";
    }

    public event Action? RequestCommitTextEdit;

    private void ZoomIn()
    {
        RequestCommitTextEdit?.Invoke();
        if (FitToWindow)
            FitToWindow = false;
        ZoomLevel = Math.Min(400, ZoomLevel + 25);
    }

    private void ZoomOut()
    {
        RequestCommitTextEdit?.Invoke();
        if (FitToWindow)
            FitToWindow = false;
        ZoomLevel = Math.Max(25, ZoomLevel - 25);
    }

    private void ZoomReset()
    {
        RequestCommitTextEdit?.Invoke();
        if (FitToWindow)
            FitToWindow = false;
        ZoomLevel = 100;
    }

    private void SetFitToWindow()
    {
        RequestCommitTextEdit?.Invoke();
        FitToWindow = !FitToWindow;
        if (!FitToWindow)
            ZoomLevel = 100;
        ZoomInCommand.RaiseCanExecuteChanged();
        ZoomOutCommand.RaiseCanExecuteChanged();
        FitToWindowCommand.RaiseCanExecuteChanged();
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(NoPreview));
        OnPropertyChanged(nameof(PageCount));
        SavePdfCommand.RaiseCanExecuteChanged();
        SendCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        ZoomInCommand.RaiseCanExecuteChanged();
        ZoomOutCommand.RaiseCanExecuteChanged();
        ZoomResetCommand.RaiseCanExecuteChanged();
        FitToWindowCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAll()
    {
        ScanCommand.RaiseCanExecuteChanged();
        InsertPdfCommand.RaiseCanExecuteChanged();
        SavePdfCommand.RaiseCanExecuteChanged();
        SendCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        RefreshScannersCommand.RaiseCanExecuteChanged();
    }

    private static string UniqueFilePath(string folder, string desiredName)
    {
        var candidate = Path.Combine(folder, desiredName);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(desiredName);
        var ext  = Path.GetExtension(desiredName);
        for (var i = 1; ; i++)
        {
            candidate = Path.Combine(folder, $"{stem}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static BitmapImage BytesToBitmapImage(byte[] data)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(data);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name!);
    }
}
