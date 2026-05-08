using SmartScanner.Models;

namespace SmartScanner.Services;

public interface IEmailService
{
    Task SendAsync(EmailSettings settings, string to, string subject, string body, string attachmentPath);
}
