using CP6.Core.Services.ErpIntegration;
using CP6.Platform.Messaging;

namespace CP6.Tests.ErpIntegration;

public sealed class ErpRuntimeOptionsTests
{
    private static readonly ErpEventValidator Validator = new(Cp6ContractBundle.Load(
        Path.Combine(AppContext.BaseDirectory, "contracts/events/erp")));

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(4096)]
    public void Independent_valid_callback_and_api_tokens_enable_runtime(int length)
    {
        var options = Options();
        options.DaprAppToken = new string('a', length);
        options.DaprApiToken = new string('b', length);
        Assert.NotNull(new ErpIntegrationRuntime(options, Validator));
    }

    [Fact]
    public void Callback_and_api_tokens_must_be_independent()
    {
        var options = Options();
        options.DaprApiToken = options.DaprAppToken;
        Assert.Throws<ArgumentException>(() => new ErpIntegrationRuntime(options, Validator));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\u00a0")]
    [InlineData("\u0000")]
    [InlineData("\u001f")]
    [InlineData("\u007f")]
    public void Either_token_rejects_whitespace_or_control_characters(string invalid)
    {
        foreach (var appToken in new[] { true, false })
        {
            var options = Options();
            var value = new string('c', 16) + invalid + new string('d', 16);
            if (appToken) options.DaprAppToken = value;
            else options.DaprApiToken = value;
            Assert.Throws<ArgumentException>(() => new ErpIntegrationRuntime(options, Validator));
        }
    }

    private static ErpIntegrationOptions Options() => new()
    {
        Enabled = true,
        Tenants = new() { [Guid.Parse("11111111-1111-4111-8111-111111111111")] = "local" },
        ReaderClientIds = ["erp-reader"], DaprAppToken = new string('a', 32), DaprApiToken = new string('b', 32)
    };
}
