using CP6.Core.Services.Space.Observability;
using Xunit;

namespace CP6.Tests.Space;

public class SpaceErrorSanitizerTests
{
    [Fact]
    public void Classify_does_not_copy_exception_message_or_stack()
    {
        var ex = new InvalidOperationException("Bearer secret-token request-body");
        var safe = SpaceErrorSanitizer.Classify(ex, "SPACE_ADAPTER_FAILURE");
        var serialized = $"{safe.ReasonCode}|{safe.ExceptionType}|{safe.Fingerprint}";

        Assert.Equal("SPACE_ADAPTER_FAILURE", safe.ReasonCode);
        Assert.Equal(nameof(InvalidOperationException), safe.ExceptionType);
        Assert.Matches("^[A-F0-9]{64}$", safe.Fingerprint);
        Assert.DoesNotContain("secret-token", serialized);
        Assert.DoesNotContain("request-body", serialized);
    }

    [Fact]
    public void Classify_is_deterministic_for_exception_type_and_hresult()
    {
        var first = new InvalidOperationException("first secret");
        var second = new InvalidOperationException("different secret");

        var firstSafe = SpaceErrorSanitizer.Classify(first, "SPACE_ADAPTER_FAILURE");
        var secondSafe = SpaceErrorSanitizer.Classify(second, "SPACE_ADAPTER_FAILURE");

        Assert.Equal(first.HResult, second.HResult);
        Assert.Equal(firstSafe.Fingerprint, secondSafe.Fingerprint);
    }

    [Fact]
    public void ToStorageCode_contains_only_safe_classification()
    {
        const string secret = "database-password";
        var code = SpaceErrorSanitizer.ToStorageCode(
            new InvalidOperationException(secret),
            "SPACE_ADAPTER_FAILURE");

        Assert.Matches(
            "^SPACE_ADAPTER_FAILURE:InvalidOperationException:[A-F0-9]{64}$",
            code);
        Assert.DoesNotContain(secret, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lowercase")]
    [InlineData("SPACE:FAILURE")]
    [InlineData("SPACE_FAILURE\nsecret-token")]
    [InlineData("1_SPACE_FAILURE")]
    public void Classify_rejects_unstable_reason_codes_without_echoing_input(string reasonCode)
    {
        var error = Assert.Throws<ArgumentException>(
            () => SpaceErrorSanitizer.Classify(
                new InvalidOperationException("exception-secret"),
                reasonCode));

        if (reasonCode.Length > 0)
            Assert.DoesNotContain(reasonCode, error.Message);
        Assert.DoesNotContain("secret-token", error.Message);
    }

    [Fact]
    public void Classify_rejects_reason_code_longer_than_128_characters()
    {
        var reasonCode = $"S{new string('A', 128)}";

        var error = Assert.Throws<ArgumentException>(
            () => SpaceErrorSanitizer.Classify(
                new InvalidOperationException("exception-secret"),
                reasonCode));

        Assert.DoesNotContain(reasonCode, error.Message);
    }
}
