using Microsoft.Win32;
using SmartScanner.Models;
using SmartScanner.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartScanner;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<SenderProfile>   _localSenders    = new();
    private readonly ObservableCollection<RecipientProfile> _localRecipients = new();
    private readonly ObservableCollection<EmailProfile>    _localProfiles   = new();
    private int  _editingIndex       = -1;
    private int  _recEditingIndex    = -1;
    private int  _profileEditingIndex = -1;
    private bool _suppressSelection;
    private bool _suppressRecSelection;
    private bool _suppressProfileSelection;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TbSmtpHost.Text = _vm.SmtpHost;
        TbSmtpPort.Text = _vm.SmtpPort.ToString();
        ChkSsl.IsChecked = _vm.SmtpUseSsl;

        _localSenders.Clear();
        foreach (var s in _vm.SenderProfiles)
            _localSenders.Add(Clone(s));
        LbSenders.ItemsSource = _localSenders;
        ShowPlaceholder();

        _localRecipients.Clear();
        foreach (var r in _vm.RecipientProfiles)
            _localRecipients.Add(CloneRec(r));
        LbRecipients.ItemsSource = _localRecipients;
        ShowRecPlaceholder();

        _localProfiles.Clear();
        foreach (var ep in _vm.EmailProfiles)
            _localProfiles.Add(CloneProfile(ep));
        LbProfiles.ItemsSource  = _localProfiles;
        PfFrom.ItemsSource    = _localSenders;      // sender picker inside profile form
        PfToCombo.ItemsSource = _localRecipients;
        ShowProfilePlaceholder();

        TbArchivePath.Text = _vm.PdfArchivePath;
        ChkDarkMode.IsChecked = _vm.IsDarkMode;
    }

    private void ChkDarkMode_Changed(object sender, RoutedEventArgs e)
    {
        var wantDark = ChkDarkMode.IsChecked == true;
        if (wantDark != _vm.IsDarkMode)
            _vm.ToggleThemeCommand.Execute(null);
    }

    // ── List events ───────────────────────────────────────────────────────────

    private void LbSenders_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (LbSenders.SelectedItem is SenderProfile p)
        {
            _editingIndex = LbSenders.SelectedIndex;
            PopulateForm(p, isNew: false);
            BtnDelete.IsEnabled = true;
        }
    }

    // ── Sender list buttons ───────────────────────────────────────────────────

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        _suppressSelection = true;
        LbSenders.SelectedItem = null;
        _suppressSelection = false;

        _editingIndex = -1;
        BtnDelete.IsEnabled = false;
        PopulateForm(new SenderProfile(), isNew: true);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (LbSenders.SelectedIndex < 0) return;

        var result = MessageBox.Show("ต้องการลบผู้ส่งนี้ใช่หรือไม่?", "ยืนยันการลบ",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _localSenders.RemoveAt(LbSenders.SelectedIndex);
        BtnDelete.IsEnabled = false;
        ShowPlaceholder();
    }

    // ── Form buttons ──────────────────────────────────────────────────────────

    private void BtnTogglePass_Click(object sender, RoutedEventArgs e)
    {
        if (FPassword.Visibility == Visibility.Visible)
        {
            FPasswordText.Text = FPassword.Password;
            FPassword.Visibility = Visibility.Collapsed;
            FPasswordText.Visibility = Visibility.Visible;
            BtnTogglePass.Content = "🙈";
        }
        else
        {
            FPassword.Password = FPasswordText.Text;
            FPasswordText.Visibility = Visibility.Collapsed;
            FPassword.Visibility = Visibility.Visible;
            BtnTogglePass.Content = "👁";
        }
    }

    private string CurrentPassword =>
        FPasswordText.Visibility == Visibility.Visible
            ? FPasswordText.Text
            : FPassword.Password;

    private void SetPassword(string value)
    {
        FPassword.Password = value;
        FPasswordText.Text = value;
        // Reset to hidden mode
        FPassword.Visibility = Visibility.Visible;
        FPasswordText.Visibility = Visibility.Collapsed;
        BtnTogglePass.Content = "👁";
    }

    private void BtnSaveSender_Click(object sender, RoutedEventArgs e)
    {
        // Validate required fields
        var email = FSenderEmail.Text.Trim();
        if (string.IsNullOrEmpty(email))
        {
            SetFormStatus("กรุณาระบุอีเมลผู้ส่ง", isError: true);
            FSenderEmail.Focus();
            return;
        }

        var user = FUsername.Text.Trim();
        var profile = new SenderProfile
        {
            DisplayName = FDisplayName.Text.Trim(),
            SenderEmail = email,
            Username    = string.IsNullOrEmpty(user) ? email : user,
            Password    = CurrentPassword,
        };

        try
        {
            if (_editingIndex == -1)
            {
                // Add new
                _localSenders.Add(profile);

                _suppressSelection = true;
                LbSenders.SelectedIndex = _localSenders.Count - 1;
                _suppressSelection = false;

                _editingIndex = LbSenders.SelectedIndex;
                BtnDelete.IsEnabled = true;
            }
            else
            {
                // Update existing
                _localSenders[_editingIndex] = profile;

                // Re-select to refresh the ListBox item display
                _suppressSelection = true;
                LbSenders.SelectedIndex = -1;
                LbSenders.SelectedIndex = _editingIndex;
                _suppressSelection = false;
            }

            TbFormTitle.Text = "แก้ไขผู้ส่ง";
            SetFormStatus("บันทึกผู้ส่งเรียบร้อย", isError: false);
        }
        catch (Exception ex)
        {
            SetFormStatus($"ข้อผิดพลาด: {ex.Message}", isError: true);
        }
    }

    // ── Recipient list events ─────────────────────────────────────────────────

    private void LbRecipients_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressRecSelection) return;
        if (LbRecipients.SelectedItem is RecipientProfile r)
        {
            _recEditingIndex = LbRecipients.SelectedIndex;
            PopulateRecForm(r, isNew: false);
            BtnDeleteRec.IsEnabled = true;
        }
    }

    private void BtnAddRec_Click(object sender, RoutedEventArgs e)
    {
        _suppressRecSelection = true;
        LbRecipients.SelectedItem = null;
        _suppressRecSelection = false;

        _recEditingIndex = -1;
        BtnDeleteRec.IsEnabled = false;
        PopulateRecForm(new RecipientProfile(), isNew: true);
    }

    private void BtnDeleteRec_Click(object sender, RoutedEventArgs e)
    {
        if (LbRecipients.SelectedIndex < 0) return;

        var result = MessageBox.Show("ต้องการลบผู้รับนี้ใช่หรือไม่?", "ยืนยันการลบ",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _localRecipients.RemoveAt(LbRecipients.SelectedIndex);
        BtnDeleteRec.IsEnabled = false;
        ShowRecPlaceholder();
    }

    private void BtnSaveRec_Click(object sender, RoutedEventArgs e)
    {
        var email = REmail.Text.Trim();
        if (string.IsNullOrEmpty(email))
        {
            SetRecStatus("กรุณาระบุอีเมลผู้รับ", isError: true);
            REmail.Focus();
            return;
        }

        var profile = new RecipientProfile
        {
            Name  = RName.Text.Trim(),
            Email = email,
        };

        try
        {
            if (_recEditingIndex == -1)
            {
                _localRecipients.Add(profile);

                _suppressRecSelection = true;
                LbRecipients.SelectedIndex = _localRecipients.Count - 1;
                _suppressRecSelection = false;

                _recEditingIndex = LbRecipients.SelectedIndex;
                BtnDeleteRec.IsEnabled = true;
            }
            else
            {
                _localRecipients[_recEditingIndex] = profile;

                _suppressRecSelection = true;
                LbRecipients.SelectedIndex = -1;
                LbRecipients.SelectedIndex = _recEditingIndex;
                _suppressRecSelection = false;
            }

            TbRecFormTitle.Text = "แก้ไขผู้รับ";
            SetRecStatus("บันทึกผู้รับเรียบร้อย", isError: false);
        }
        catch (Exception ex)
        {
            SetRecStatus($"ข้อผิดพลาด: {ex.Message}", isError: true);
        }
    }

    // ── Save all ──────────────────────────────────────────────────────────────

    private void BtnSaveAll_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TbSmtpPort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("Port ต้องเป็นตัวเลข 1–65535", "ข้อมูลไม่ถูกต้อง",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TbSmtpPort.Focus();
            return;
        }

        try
        {
            _vm.SmtpHost       = TbSmtpHost.Text.Trim();
            _vm.SmtpPort       = port;
            _vm.SmtpUseSsl     = ChkSsl.IsChecked == true;
            _vm.PdfArchivePath = TbArchivePath.Text.Trim();

            _vm.SenderProfiles.Clear();
            foreach (var s in _localSenders)
                _vm.SenderProfiles.Add(s);

            if (_vm.SelectedSender != null &&
                !_vm.SenderProfiles.Any(s => s.Username == _vm.SelectedSender.Username))
                _vm.SelectedSender = _vm.SenderProfiles.FirstOrDefault();
            else if (_vm.SelectedSender == null)
                _vm.SelectedSender = _vm.SenderProfiles.FirstOrDefault();

            _vm.RecipientProfiles.Clear();
            foreach (var r in _localRecipients)
                _vm.RecipientProfiles.Add(r);

            _vm.EmailProfiles.Clear();
            foreach (var ep in _localProfiles)
                _vm.EmailProfiles.Add(ep);

            _vm.SaveSettings();

            MessageBox.Show("บันทึกการตั้งค่าเรียบร้อยแล้ว", "สำเร็จ",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"บันทึกไม่สำเร็จ:\n{ex.Message}", "ข้อผิดพลาด",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Archive folder ────────────────────────────────────────────────────────

    private void BtnBrowseArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "เลือกโฟลเดอร์สำหรับเก็บไฟล์ PDF ที่ส่งแล้ว",
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
            TbArchivePath.Text = dialog.FolderName;
    }

    private void BtnOpenArchiveFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = TbArchivePath.Text.Trim();
        if (string.IsNullOrEmpty(path)) { MessageBox.Show("ยังไม่ได้ตั้งค่าโฟลเดอร์", "แจ้งเตือน", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ข้อผิดพลาด", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void PopulateForm(SenderProfile p, bool isNew)
    {
        TbFormTitle.Text  = isNew ? "เพิ่มอีเมลผู้ส่งใหม่" : "แก้ไขผู้ส่ง";
        FDisplayName.Text = p.DisplayName;
        FSenderEmail.Text = p.SenderEmail;
        FUsername.Text    = p.Username;
        SetPassword(p.Password);

        SetFormStatus(string.Empty, isError: false);   // clear status

        PanelPlaceholder.Visibility = Visibility.Collapsed;
        PanelForm.Visibility        = Visibility.Visible;
    }

    private void ShowPlaceholder()
    {
        PanelForm.Visibility        = Visibility.Collapsed;
        PanelPlaceholder.Visibility = Visibility.Visible;
    }

    private void SetFormStatus(string message, bool isError)
    {
        TbFormStatus.Text       = message;
        TbFormStatus.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))   // red
            : new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));   // green
        TbFormStatus.Visibility = string.IsNullOrEmpty(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static SenderProfile Clone(SenderProfile s) => new()
    {
        DisplayName = s.DisplayName,
        SenderEmail = s.SenderEmail,
        Username    = s.Username,
        Password    = s.Password,
    };

    private void PopulateRecForm(RecipientProfile r, bool isNew)
    {
        TbRecFormTitle.Text = isNew ? "เพิ่มผู้รับใหม่" : "แก้ไขผู้รับ";
        RName.Text  = r.Name;
        REmail.Text = r.Email;
        SetRecStatus(string.Empty, isError: false);
        RecPlaceholder.Visibility = Visibility.Collapsed;
        RecForm.Visibility        = Visibility.Visible;
    }

    private void ShowRecPlaceholder()
    {
        RecForm.Visibility        = Visibility.Collapsed;
        RecPlaceholder.Visibility = Visibility.Visible;
    }

    private void SetRecStatus(string message, bool isError)
    {
        TbRecStatus.Text       = message;
        TbRecStatus.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
            : new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
        TbRecStatus.Visibility = string.IsNullOrEmpty(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static RecipientProfile CloneRec(RecipientProfile r) => new()
    {
        Name  = r.Name,
        Email = r.Email,
    };

    // ── Email Profile CRUD ────────────────────────────────────────────────────

    private void PfToCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PfToCombo.SelectedItem is RecipientProfile r)
            PfTo.Text = r.Email;
    }

    private void LbProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileSelection) return;
        if (LbProfiles.SelectedItem is EmailProfile ep)
        {
            _profileEditingIndex = LbProfiles.SelectedIndex;
            PopulateProfileForm(ep, isNew: false);
            BtnDeleteProfile.IsEnabled = true;
        }
    }

    private void BtnAddProfile_Click(object sender, RoutedEventArgs e)
    {
        _suppressProfileSelection = true;
        LbProfiles.SelectedItem = null;
        _suppressProfileSelection = false;

        _profileEditingIndex = -1;
        BtnDeleteProfile.IsEnabled = false;
        PopulateProfileForm(new EmailProfile(), isNew: true);
    }

    private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LbProfiles.SelectedIndex < 0) return;

        var result = MessageBox.Show("ต้องการลบโปรไฟล์นี้ใช่หรือไม่?", "ยืนยันการลบ",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _localProfiles.RemoveAt(LbProfiles.SelectedIndex);
        BtnDeleteProfile.IsEnabled = false;
        ShowProfilePlaceholder();
    }

    private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = PfName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SetProfileStatus("กรุณาระบุชื่อโปรไฟล์", isError: true);
            PfName.Focus();
            return;
        }

        var to = PfTo.Text.Trim();
        if (string.IsNullOrEmpty(to))
        {
            SetProfileStatus("กรุณาระบุอีเมลผู้รับ", isError: true);
            PfTo.Focus();
            return;
        }

        var fromSender = PfFrom.SelectedItem as SenderProfile;
        if (fromSender == null)
        {
            SetProfileStatus("กรุณาเลือกผู้ส่ง", isError: true);
            PfFrom.Focus();
            return;
        }

        var profile = new EmailProfile
        {
            Name           = name,
            SenderUsername = fromSender.Username,
            RecipientEmail = to,
            Subject        = PfSubject.Text.Trim(),
            Body           = PfBody.Text,
        };

        try
        {
            if (_profileEditingIndex == -1)
            {
                _localProfiles.Add(profile);

                _suppressProfileSelection = true;
                LbProfiles.SelectedIndex = _localProfiles.Count - 1;
                _suppressProfileSelection = false;

                _profileEditingIndex = LbProfiles.SelectedIndex;
                BtnDeleteProfile.IsEnabled = true;
            }
            else
            {
                _localProfiles[_profileEditingIndex] = profile;

                _suppressProfileSelection = true;
                LbProfiles.SelectedIndex = -1;
                LbProfiles.SelectedIndex = _profileEditingIndex;
                _suppressProfileSelection = false;
            }

            TbProfileFormTitle.Text = "แก้ไขโปรไฟล์";
            SetProfileStatus("บันทึกโปรไฟล์เรียบร้อย", isError: false);
        }
        catch (Exception ex)
        {
            SetProfileStatus($"ข้อผิดพลาด: {ex.Message}", isError: true);
        }
    }

    private void PopulateProfileForm(EmailProfile ep, bool isNew)
    {
        TbProfileFormTitle.Text = isNew ? "เพิ่มโปรไฟล์ใหม่" : "แก้ไขโปรไฟล์";
        PfName.Text    = ep.Name;
        PfFrom.SelectedItem   = _localSenders.FirstOrDefault(s => s.Username == ep.SenderUsername);
        PfToCombo.SelectedItem = null;
        PfTo.Text             = ep.RecipientEmail;
        PfSubject.Text = ep.Subject;
        PfBody.Text    = ep.Body;
        SetProfileStatus(string.Empty, isError: false);
        ProfilePlaceholder.Visibility = Visibility.Collapsed;
        ProfileForm.Visibility        = Visibility.Visible;
    }

    private void ShowProfilePlaceholder()
    {
        ProfileForm.Visibility        = Visibility.Collapsed;
        ProfilePlaceholder.Visibility = Visibility.Visible;
    }

    private void SetProfileStatus(string message, bool isError)
    {
        TbProfileStatus.Text       = message;
        TbProfileStatus.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
            : new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
        TbProfileStatus.Visibility = string.IsNullOrEmpty(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static EmailProfile CloneProfile(EmailProfile ep) => new()
    {
        Name           = ep.Name,
        SenderUsername = ep.SenderUsername,
        RecipientEmail = ep.RecipientEmail,
        Subject        = ep.Subject,
        Body           = ep.Body,
    };
}
