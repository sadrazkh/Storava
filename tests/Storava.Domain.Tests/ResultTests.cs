using Storava.Domain.Common;

namespace Storava.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_CarriesError()
    {
        var error = Error.Validation("bad");
        var result = Result.Failure(error);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        Result<int> result = 42;
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_ThrowsOnValueAccess()
    {
        var result = Result.Failure<int>(Error.NotFound("missing"));
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_WithError_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure(Error.None));
    }
}
