namespace SmartScanner.Models;

public class SenderProfile
{
    public string DisplayName { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public override string ToString() =>
        string.IsNullOrEmpty(DisplayName) ? SenderEmail : $"{DisplayName}  <{SenderEmail}>";
}
