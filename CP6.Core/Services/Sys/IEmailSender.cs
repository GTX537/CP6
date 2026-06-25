namespace CP6.Core.Services.Sys;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body);
}
