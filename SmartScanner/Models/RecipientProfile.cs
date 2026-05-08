namespace SmartScanner.Models;

public class RecipientProfile
{
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? Email : $"{Name} <{Email}>";
}
