using System.Globalization;
using Storava.Domain.ValueObjects;

namespace Storava.Domain.Tests;

public class ByteSizeTests
{
    [Fact]
    public void NegativeSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteSize(-1));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1073741824, "1 GB")]
    public void Humanize_FormatsWithInvariantCulture(long bytes, string expected)
    {
        var size = new ByteSize(bytes);
        Assert.Equal(expected, size.Humanize(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Addition_SumsBytes()
    {
        var result = new ByteSize(1000) + new ByteSize(24);
        Assert.Equal(1024, result.Bytes);
    }

    [Fact]
    public void FromGigabytes_RoundTrips()
    {
        var size = ByteSize.FromGigabytes(2);
        Assert.Equal(2 * 1024L * 1024 * 1024, size.Bytes);
    }

    [Fact]
    public void Humanize_UsesPersianDecimalSeparator()
    {
        var persian = CultureInfo.GetCultureInfo("fa-IR");
        string text = new ByteSize(1536).Humanize(persian);

        Assert.Contains(persian.NumberFormat.NumberDecimalSeparator, text);
        Assert.Contains("KB", text);
    }

    [Fact]
    public void Humanize_IsolatesUnitForRightToLeftCultures()
    {
        var persian = CultureInfo.GetCultureInfo("fa-IR");
        string text = new ByteSize(1536).Humanize(persian);

        // LRM marks keep the number before the unit when rendered inside RTL text.
        const char Lrm = '‎';
        Assert.StartsWith(Lrm.ToString(), text);
        Assert.EndsWith(Lrm.ToString(), text);

        string visible = text.Trim(Lrm);
        Assert.EndsWith("KB", visible);
        Assert.StartsWith("1", visible);
    }

    [Fact]
    public void Humanize_LeavesLeftToRightTextUnmarked()
    {
        string text = new ByteSize(1536).Humanize(CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("1.5 KB", text);
    }
}
