namespace SmartScanner.Models;

public class SentItem
{
    public int      Id        { get; set; }
    public DateTime SentAt    { get; set; }
    public string   FromName  { get; set; } = string.Empty;
    public string   FromEmail { get; set; } = string.Empty;
    public string   ToEmail   { get; set; } = string.Empty;
    public string   Subject   { get; set; } = string.Empty;
    public string   FileName  { get; set; } = string.Empty;

    public string FromDisplay =>
        string.IsNullOrEmpty(FromName) ? FromEmail : $"{FromName} <{FromEmail}>";

    public string SentAtDisplay => SentAt.ToString("dd/MM/yyyy  HH:mm:ss");
}
