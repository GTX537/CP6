using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    [InlineData("+1.0.0", "1.0.0", -1)]
    [InlineData("1.0.0-beta.2147483648", "1.0.0-beta.99", 1)]
    [InlineData("1.0.0-beta.10000000000", "1.0.0-beta.9999999999", 1)]
    [InlineData("2147483648.0.0", "2147483647.0.0", 1)]
    [InlineData("not-a-version", "1.0.0", -1)]
    public void MinimumVersionComparison_IsReleaseSafe(
        string current,
        string minimum,
        int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(ClientBootstrapController.Compare(current, minimum)));
    }

    [Theory]
    [InlineData("bad-minimum", "1.2.0")]
    [InlineData("1.3.0", "1.2.0")]
    [InlineData("1.0.0", "1.2.0-beta.01")]
    public async Task Invalid_release_policy_reports_unavailable_before_using_language_storage(string minimum, string latest)
    {
        var options = new SecurityOptions();
        options.NativeClient.Android.MinimumVersion = minimum;
        options.NativeClient.Android.LatestVersion = latest;
        var controller = new ClientBootstrapController(Options.Create(options), null!);

        var response = await controller.Get("android", "1.2.0");

        var error = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(503, error.StatusCode);
        Assert.Equal("E-CLIENT-VERSION-POLICY", error.Value!.GetType().GetProperty("code")!.GetValue(error.Value));
    }

    [Fact]
    public async Task Wrong_channel_reports_no_package_before_using_language_storage()
    {
        var options = new SecurityOptions();
        options.NativeClient.Android.Channel = "pilot";
        var controller = new ClientBootstrapController(Options.Create(options), null!);

        var response = await controller.Get("android", "1.0.0", "stable");

        var error = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal("E-CLIENT-CHANNEL-MISMATCH", error.Value!.GetType().GetProperty("code")!.GetValue(error.Value));
    }
}
