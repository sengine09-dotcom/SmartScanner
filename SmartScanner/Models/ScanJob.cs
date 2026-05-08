namespace SmartScanner.Models;

public class ScanJob
{
    public string FileName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string ScannerName { get; set; } = string.Empty;
    public int Dpi { get; set; } = 300;
    public string ColorMode { get; set; } = "Color";
    public List<byte[]> ScannedPages { get; set; } = new();
}
