using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Sys;

public class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;
    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;

    public Task SendAsync(string to, string subject, string body)
    {
        _log.LogWarning("[DEV-EMAIL→{To}] {Subject}: {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
