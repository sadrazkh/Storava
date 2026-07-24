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
}
