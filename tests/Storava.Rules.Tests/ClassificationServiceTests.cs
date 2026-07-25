using Storava.Domain.Enums;

namespace Storava.Rules.Tests;

public class ClassificationServiceTests
{
    [Fact]
    public void Classify_KnownCache_IsActionable()
    {
        var result = TestFixtures.Classifier().Classify(
            TestFixtures.Folder(@"C:\Users\a\.nuget\packages"));

        Assert.Equal(StorageCategory.PackageCaches, result.Category);
        Assert.Equal("NuGet", result.Technology);
        Assert.Equal(RiskLevel.Low, result.RiskLevel);
        Assert.True(result.CanDelete);
        Assert.True(result.CanMove);
        Assert.True(result.CanRegenerate);
        Assert.Equal(MigrationMethod.OfficialSetting, result.OfficialMigrationMethod);
    }

    [Fact]
    public void Classify_ProtectedPath_IsNeverActionable()
    {
        var result = TestFixtures.Classifier().Classify(
            TestFixtures.Folder(@"C:\Windows\System32"));

        Assert.Equal(RiskLevel.Protected, result.RiskLevel);
        Assert.False(result.CanDelete);
        Assert.False(result.CanMove);
        Assert.Equal(MigrationMethod.None, result.OfficialMigrationMethod);
    }

    [Fact]
    public void Classify_ProtectionOverridesMatchingRule()
    {
        // A Temp folder inside Windows matches system.temp, but protection must win.
        var result = TestFixtures.Classifier().Classify(
            TestFixtures.Folder(@"C:\Windows\Temp"));

        Assert.Equal(RiskLevel.Protected, result.RiskLevel);
        Assert.False(result.CanDelete);
    }

    [Fact]
    public void Classify_HonoursIsProtectedFlagFromScan()
    {
        var item = TestFixtures.Folder(@"D:\anything\bin", isProtected: true);

        var result = TestFixtures.Classifier().Classify(item);

        Assert.Equal(RiskLevel.Protected, result.RiskLevel);
        Assert.False(result.CanDelete);
    }

    [Fact]
    public void Classify_UnknownFolder_IsNotActionable()
    {
        var result = TestFixtures.Classifier().Classify(
            TestFixtures.Folder(@"D:\Clients\Acme\SecretProject"));

        Assert.Equal(StorageCategory.Unknown, result.Category);
        Assert.Null(result.RuleId);
        Assert.False(result.CanDelete);
        Assert.False(result.CanMove);
    }

    [Fact]
    public void Classify_GitRepository_IsHighRiskAndNotDeletable()
    {
        var result = TestFixtures.Classifier().Classify(
            TestFixtures.Folder(@"D:\src\proj\.git"));

        Assert.Equal(RiskLevel.High, result.RiskLevel);
        Assert.False(result.CanDelete);
        Assert.False(result.CanRegenerate);
    }

    [Theory]
    [InlineData(@"D:\Videos\movie.mp4", StorageCategory.Media)]
    [InlineData(@"D:\Docs\report.pdf", StorageCategory.PersonalFiles)]
    [InlineData(@"D:\Setup\installer.exe", StorageCategory.Applications)]
    public void Classify_UnmatchedFiles_GetCategoryButStayNonActionable(string path, StorageCategory expected)
    {
        var result = TestFixtures.Classifier().Classify(TestFixtures.File(path));

        Assert.Equal(expected, result.Category);
        Assert.False(result.CanDelete);
        Assert.False(result.CanMove);
    }

    [Fact]
    public void Apply_WritesClassificationOntoItem()
    {
        var item = TestFixtures.Folder(@"C:\Users\a\.gradle");

        TestFixtures.Classifier().Apply(item);

        Assert.Equal(StorageCategory.PackageCaches, item.Category);
        Assert.Equal("Gradle", item.DetectedTechnology);
        Assert.Equal("gradle.home", item.KnownRuleId);
        Assert.Equal(RiskLevel.Low, item.RiskLevel);
        Assert.True(item.CanMove);
        Assert.True(item.Confidence > 0.9);
    }

    [Fact]
    public void Apply_ProtectedItem_LeavesEverythingDisabled()
    {
        var item = TestFixtures.Folder(@"C:\Program Files\App");

        TestFixtures.Classifier().Apply(item);

        Assert.Equal(RiskLevel.Protected, item.RiskLevel);
        Assert.False(item.CanDelete);
        Assert.False(item.CanMove);
        Assert.False(item.CanRegenerate);
    }
}
