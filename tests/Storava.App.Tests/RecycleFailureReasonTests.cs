using Storava.Domain.Entities;

namespace Storava.App.Tests;

/// <summary>
/// A failed removal has to say which kind of failure it was.
/// <para>
/// Removal is the last step of a move, so when it fails the copy has already been made and
/// verified and the recovery is to undo all of it. A user who is told only that the folder "could
/// not be sent to the Recycle Bin" after waiting through a long copy has no way to tell a locked
/// file — close the program and retry — from a permission refusal, which retrying will never fix.
/// </para>
/// </summary>
public class RecycleFailureReasonTests
{
    [Theory]
    [InlineData(0x20)] // ERROR_SHARING_VIOLATION
    [InlineData(0x21)] // ERROR_LOCK_VIOLATION
    public void AFileHeldOpenElsewhereIsReportedAsSuch(int code)
    {
        Assert.Equal(ExecutionErrors.RecycleSourceInUse, Storava.Platform.Storage.WindowsFileActions.RecycleErrorFor(code));
    }

    [Theory]
    [InlineData(0x05)] // ERROR_ACCESS_DENIED
    [InlineData(0x78)] // the shell's own DE_ACCESSDENIEDSRC
    public void APermissionRefusalIsKeptSeparate(int code)
    {
        Assert.Equal(ExecutionErrors.RecycleAccessDenied, Storava.Platform.Storage.WindowsFileActions.RecycleErrorFor(code));
    }

    /// <summary>
    /// Anything unrecognised stays the general failure. Guessing a cause would be worse than not
    /// naming one: it would send someone off closing programs that were never the problem.
    /// </summary>
    [Theory]
    [InlineData(0x7C)] // DE_INVALIDFILES
    [InlineData(0x402)] // the shell's catch-all "unknown error"
    [InlineData(1234)]
    public void AnythingElseStaysTheGeneralFailure(int code)
    {
        Assert.Equal(ExecutionErrors.RecycleFailed, Storava.Platform.Storage.WindowsFileActions.RecycleErrorFor(code));
    }

    /// <summary>The three are distinct, or the mapping above would be decoration.</summary>
    [Fact]
    public void TheThreeReasonsAreDifferentErrors()
    {
        var codes = new[]
        {
            ExecutionErrors.RecycleFailed.Code,
            ExecutionErrors.RecycleSourceInUse.Code,
            ExecutionErrors.RecycleAccessDenied.Code
        };

        Assert.Equal(3, codes.Distinct(StringComparer.Ordinal).Count());
    }
}
