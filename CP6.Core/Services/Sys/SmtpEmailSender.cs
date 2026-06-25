using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace CP6.Core.Services.Sys;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _cfg;
    public SmtpEmailSender(IConfiguration cfg) => _cfg = cfg;

    public async Task SendAsync(string to, string subject, string body)
    {
        var smtp = _cfg.GetSection("Email:Smtp");
        var host = smtp["Host"] ?? throw new InvalidOperationException("Email:Smtp:Host 未配置");
        var port = int.TryParse(smtp["Port"], out var p) ? p : 587;
        var from = smtp["From"] ?? "no-reply@cp6.local";
        var user = smtp["User"];
        var pass = smtp["Pass"];
        var enableSsl = !bool.TryParse(smtp["EnableSsl"], out var ssl) || ssl;

        using var msg = new MailMessage(from, to) { Subject = subject, Body = body };
        using var client = new SmtpClient(host, port) { EnableSsl = enableSsl };
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, pass ?? string.Empty);

        await client.SendMailAsync(msg);
    }
}
