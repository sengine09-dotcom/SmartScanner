using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SmartScanner.Models;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace SmartScanner.Services;

public class EmailService : IEmailService
{
    private static readonly string SignaturePath = @"C:\Signature\signature.html";

    public async Task SendAsync(EmailSettings settings, string to, string subject, string body, string attachmentPath)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.DisplayName, settings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = BuildHtmlBody(body) };
        builder.Attachments.Add(attachmentPath);
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort,
            settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);
        await client.AuthenticateAsync(settings.Username, settings.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static string BuildHtmlBody(string plainBody)
    {
        // Encode user text so <, &, etc. are safe inside HTML
        var bodyHtml = WebUtility.HtmlEncode(plainBody).Replace("\n", "<br>");

        var signatureHtml = string.Empty;
        if (File.Exists(SignaturePath))
        {
            try
            {
                var raw = File.ReadAllText(SignaturePath);
                var m = Regex.Match(raw, @"<body[^>]*>([\s\S]*?)</body>", RegexOptions.IgnoreCase);
                signatureHtml = m.Success ? m.Groups[1].Value.Trim() : string.Empty;
            }
            catch { }
        }

        var sigBlock = signatureHtml.Length > 0
            ? $"<hr style=\"border:none;border-top:1px solid #CBD5E1;margin:20px 0\">{signatureHtml}"
            : string.Empty;

        return "<html><body style=\"font-family:sans-serif;font-size:14px;color:#111827;\">" +
               $"<p>{bodyHtml}</p>" +
               sigBlock +
               "</body></html>";
    }
}
