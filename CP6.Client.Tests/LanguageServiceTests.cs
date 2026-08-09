using System.Globalization;
using CP6.Client.Core;

namespace CP6.Client.Tests;

public sealed class LanguageServiceTests
{
    private static readonly string[] RequiredNativeKeys =
    [
        "login.username",
        "login.password",
        "login.button",
        "layout.logout",
        "wms.mobile.title",
        "wms.mobile.scan.ph",
        "wms.common.refresh",
        "wms.common.qty",
        "client.deviceActivation",
        "client.activateWarehouseDevice",
        "client.sharedQuickSwitch",
        "client.taskDetail",
        "client.moveScanTitle",
        "client.camera",
        "client.partialReason",
        "client.timeoutRetryGuidance",
        "client.productionControls",
        "client.loadProductionOverview",
        "client.productionSummary",
    ];

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("ko")]
    public void Native_Fallback_Covers_Critical_Wms_Client_Keys(string language)
    {
        var values = LanguageService.BuiltIn(language);

        Assert.All(RequiredNativeKeys, key =>
        {
            Assert.True(values.TryGetValue(key, out var value), $"Missing {key} for {language}");
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.NotEqual(key, value);
            Assert.DoesNotContain('\uFFFD', value);
        });
    }

    [Fact]
    public void Native_Formats_Accept_Their_Runtime_Arguments()
    {
        var values = LanguageService.BuiltIn("en");

        Assert.Equal(
            "Device activated: RF-01 (Shared)",
            string.Format(
                CultureInfo.InvariantCulture,
                values["client.activatedDevice"],
                "RF-01",
                "Shared"));
        Assert.Equal(
            "Created 10 · Completed 8 · Partial 1 · Exceptions 1 · Overdue 0 · Avg 2.5 min",
            string.Format(
                CultureInfo.InvariantCulture,
                values["client.productionSummary"],
                10,
                8,
                1,
                1,
                0,
                2.5));
    }
}
