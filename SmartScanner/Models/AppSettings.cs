namespace SmartScanner.Models;

public class AppSettings
{
    public string LastScanner { get; set; } = string.Empty;
    public int SelectedDpi { get; set; } = 300;
    public string SelectedColorMode { get; set; } = "Color";
    public string SmtpHost { get; set; } = "mail.asianonlinegroup.co.th";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public List<SenderProfile>   Senders    { get; set; } = new();
    public string LastSenderUsername { get; set; } = string.Empty;
    public List<RecipientProfile> Recipients     { get; set; } = new();
    public List<EmailProfile>    EmailProfiles  { get; set; } = new();
    public string                PdfArchivePath { get; set; } = string.Empty;
    public bool                  IsDarkMode     { get; set; } = false;
}
