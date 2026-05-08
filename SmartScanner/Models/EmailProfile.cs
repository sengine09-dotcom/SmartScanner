namespace SmartScanner.Models;

public class EmailProfile
{
    public string Name           { get; set; } = string.Empty;
    public string SenderUsername { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject        { get; set; } = string.Empty;
    public string Body           { get; set; } = string.Empty;
    public override string ToString() => Name;
}
