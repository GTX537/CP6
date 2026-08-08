using CP6.Client.Core;

namespace CP6.Client.Tests;

public sealed class ScannerInputProcessorTests
{
    [Theory]
    [InlineData("ABC\r", "ABC")]
    [InlineData("ABC\n", "ABC")]
    [InlineData("ABC\t", "ABC")]
    [InlineData(" ABC\r\n", "ABC")]
    public void Removes_Common_Terminators(string raw, string expected)
    {
        var processor = new ScannerInputProcessor();

        var result = processor.Accept(raw, ScannerInputSource.Hid);

        Assert.Equal(ScannerInputStatus.Accepted, result.Status);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Removes_Configured_Hid_Framing()
    {
        var processor = new ScannerInputProcessor(new ScannerInputOptions
        {
            Prefix = "]C1",
            Suffix = "~",
        });

        var result = processor.Accept("]C1PRODUCT-01~\r", ScannerInputSource.Hid);

        Assert.True(result.IsAccepted);
        Assert.Equal("PRODUCT-01", result.Value);
    }

    [Theory]
    [InlineData("PRODUCT-01~", "WM-SCAN-PREFIX-MISMATCH")]
    [InlineData("]C1PRODUCT-01", "WM-SCAN-SUFFIX-MISMATCH")]
    public void Rejects_Hid_Input_With_Missing_Configured_Framing(
        string raw,
        string expectedError)
    {
        var processor = new ScannerInputProcessor(new ScannerInputOptions
        {
            Prefix = "]C1",
            Suffix = "~",
        });

        var result = processor.Accept(raw, ScannerInputSource.Hid);

        Assert.Equal(ScannerInputStatus.Invalid, result.Status);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Theory]
    [InlineData(ScannerInputSource.Manual)]
    [InlineData(ScannerInputSource.Camera)]
    [InlineData(ScannerInputSource.Broadcast)]
    public void Allows_Unframed_Non_Hid_Input(ScannerInputSource source)
    {
        var processor = new ScannerInputProcessor(new ScannerInputOptions
        {
            Prefix = "]C1",
            Suffix = "~",
        });

        var result = processor.Accept("PRODUCT-01", source);

        Assert.True(result.IsAccepted);
        Assert.Equal("PRODUCT-01", result.Value);
    }

    [Fact]
    public void Suppresses_Same_Normalized_Value_Inside_Window()
    {
        var processor = new ScannerInputProcessor(new ScannerInputOptions
        {
            DuplicateWindow = TimeSpan.FromMilliseconds(750),
        });
        var firstAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");

        var first = processor.Accept("PRODUCT-01\r", ScannerInputSource.Hid, firstAt);
        var duplicate = processor.Accept(
            "PRODUCT-01",
            ScannerInputSource.Camera,
            firstAt.AddMilliseconds(500));
        var later = processor.Accept(
            "PRODUCT-01",
            ScannerInputSource.Broadcast,
            firstAt.AddMilliseconds(751));

        Assert.True(first.IsAccepted);
        Assert.Equal(ScannerInputStatus.Duplicate, duplicate.Status);
        Assert.Equal("WM-SCAN-DUPLICATE-IGNORED", duplicate.ErrorCode);
        Assert.True(later.IsAccepted);
    }

    [Fact]
    public void Accepts_Different_Values_Inside_Window()
    {
        var processor = new ScannerInputProcessor();
        var firstAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");

        var first = processor.Accept("A", ScannerInputSource.Camera, firstAt);
        var second = processor.Accept(
            "B",
            ScannerInputSource.Camera,
            firstAt.AddMilliseconds(1));

        Assert.True(first.IsAccepted);
        Assert.True(second.IsAccepted);
    }

    [Fact]
    public void Concurrent_Duplicates_Accept_Only_Once()
    {
        var processor = new ScannerInputProcessor();
        var receivedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");

        var results = Enumerable.Range(0, 20)
            .AsParallel()
            .Select(_ => processor.Accept(
                "PRODUCT-01",
                ScannerInputSource.Broadcast,
                receivedAt))
            .ToArray();

        Assert.Single(results, result => result.IsAccepted);
        Assert.Equal(
            19,
            results.Count(result => result.Status == ScannerInputStatus.Duplicate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("ABC\u0001DEF")]
    public void Rejects_Invalid_Input(string raw)
    {
        var processor = new ScannerInputProcessor();

        var result = processor.Accept(raw, ScannerInputSource.Manual);

        Assert.Equal(ScannerInputStatus.Invalid, result.Status);
        Assert.Equal("WM-SCAN-INPUT-INVALID", result.ErrorCode);
    }

    [Fact]
    public void Rejects_Input_Over_Maximum_Length()
    {
        var processor = new ScannerInputProcessor(new ScannerInputOptions
        {
            MaxLength = 4,
        });

        var result = processor.Accept("12345", ScannerInputSource.Broadcast);

        Assert.Equal(ScannerInputStatus.Invalid, result.Status);
    }
}
