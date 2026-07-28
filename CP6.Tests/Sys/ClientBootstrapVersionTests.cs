using CP6.WebApi.Controllers;

namespace CP6.Tests.Sys;

public sealed class ClientBootstrapVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.2", 1)]
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0+build.2", "1.0.0+build.1", 0)]
    [InlineData("01.0.0", "1.0.0", -1)]
    [InlineData("not-a-version", "1.0.0", -1)]
    public void MinimumVersionComparison_IsReleaseSafe(
        string current,
        string minimum,
        int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(ClientBootstrapController.Compare(current, minimum)));
    }
}
