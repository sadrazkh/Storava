using Storava.AI.Privacy;

namespace Storava.AI.Tests;

public class PathSanitizerTests
{
    private static PathSanitizer Create() => new(@"C:\Users\Ali", "Ali");

    [Fact]
    public void Sanitize_ReplacesUserProfileAndPrivateFolders()
    {
        var sanitizer = Create();

        string result = sanitizer.Sanitize(@"C:\Users\Ali\Documents\ClientA\SecretProject");

        Assert.Equal(@"<UserProfile>\Documents\<PrivateFolder-1>\<PrivateFolder-2>", result);
    }

    [Fact]
    public void Sanitize_NeverLeaksTheUserName()
    {
        var sanitizer = Create();

        string result = sanitizer.Sanitize(@"D:\Backups\Ali\personal");

        Assert.DoesNotContain("Ali", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<User>", result);
    }

    [Fact]
    public void Sanitize_KeepsWellKnownToolingFoldersReadable()
    {
        var sanitizer = Create();

        string result = sanitizer.Sanitize(@"C:\Users\Ali\.nuget\packages");

        // These names carry no personal information and are what makes advice actionable.
        Assert.Equal(@"<UserProfile>\.nuget\packages", result);
    }

    [Fact]
    public void Sanitize_MapsDriveLettersToPlaceholders()
    {
        var sanitizer = Create();

        Assert.StartsWith("<Drive-D>", sanitizer.Sanitize(@"D:\Projects\Acme"));
        Assert.StartsWith("<Drive-E>", sanitizer.Sanitize(@"E:\Media\Home videos"));
    }

    [Fact]
    public void Sanitize_IsStableForTheSameFolderName()
    {
        var sanitizer = Create();

        string first = sanitizer.Sanitize(@"D:\Work\Acme\src");
        string second = sanitizer.Sanitize(@"D:\Work\Acme\tests");

        // The same private name must map to the same placeholder, so structure is comparable.
        Assert.Contains("<PrivateFolder-1>", first);
        Assert.Contains("<PrivateFolder-1>", second);
        Assert.Contains("<PrivateFolder-2>", first);
        Assert.Contains("<PrivateFolder-2>", second);
    }

    [Fact]
    public void Sanitize_DistinguishesDifferentPrivateFolders()
    {
        var sanitizer = Create();

        string a = sanitizer.Sanitize(@"D:\ClientAlpha");
        string b = sanitizer.Sanitize(@"D:\ClientBeta");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_HandlesEmptyInput(string input)
    {
        Assert.Equal(string.Empty, Create().Sanitize(input));
    }

    [Fact]
    public void Sanitize_IsCaseInsensitiveForTheProfilePath()
    {
        var sanitizer = Create();

        string result = sanitizer.Sanitize(@"c:\users\ALI\Downloads");

        Assert.Equal(@"<UserProfile>\Downloads", result);
    }

    [Fact]
    public void Sanitize_DoesNotMatchAProfileLookalikePrefix()
    {
        var sanitizer = Create();

        // "AliBackup" is a different folder and must not be treated as the profile.
        string result = sanitizer.Sanitize(@"C:\Users\AliBackup\data");

        Assert.DoesNotContain("<UserProfile>", result);
        Assert.DoesNotContain("AliBackup", result);
    }

    [Fact]
    public void ContainsPersonalData_DetectsLeakedProfileOrUserName()
    {
        var sanitizer = Create();

        Assert.True(sanitizer.ContainsPersonalData(@"something C:\Users\Ali somewhere"));
        Assert.True(sanitizer.ContainsPersonalData("report by ali"));
        Assert.False(sanitizer.ContainsPersonalData(@"<UserProfile>\Documents\<PrivateFolder-1>"));
    }

    [Fact]
    public void Sanitize_ProducesNothingPersonalForADeepRealisticPath()
    {
        var sanitizer = Create();

        string result = sanitizer.Sanitize(@"C:\Users\Ali\AppData\Local\Temp\ClientReports\Q3\draft");

        Assert.False(sanitizer.ContainsPersonalData(result));
        Assert.Contains(@"AppData\Local\Temp", result);
        Assert.DoesNotContain("ClientReports", result);
        Assert.DoesNotContain("Q3", result);
    }
}
