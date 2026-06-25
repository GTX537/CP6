using CP6.Core.Services.Sys;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CP6.Tests.Sys;

public class EmailSenderTests
{
    [Fact]
    public async Task Log_sender_does_not_throw()
    {
        var s = new LogEmailSender(NullLogger<LogEmailSender>.Instance);
        await s.SendAsync("a@b.c", "sub", "body with 123456");
    }
}
