using Storava.Domain.Enums;
using Storava.Rules.Catalog;

namespace Storava.Rules.Tests;

public class RuleEngineTests
{
    [Fact]
    public void Catalog_HasNoDuplicateIds()
    {
        // BuiltInRuleProvider throws on duplicates; this asserts the catalog loads cleanly.
        var rules = new BuiltInRuleProvider().GetRules();

        Assert.NotEmpty(rules);
        Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalog_EveryRuleHasBothLanguages()
    {
        foreach (var rule in new BuiltInRuleProvider().GetRules())
        {
            Assert.True(rule.Titles.ContainsKey("en"), $"{rule.Id} is missing an English title.");
            Assert.True(rule.Titles.ContainsKey("fa"), $"{rule.Id} is missing a Persian title.");
            Assert.True(rule.Descriptions.ContainsKey("en"), $"{rule.Id} is missing an English description.");
            Assert.True(rule.Descriptions.ContainsKey("fa"), $"{rule.Id} is missing a Persian description.");
            Assert.NotEmpty(rule.Patterns);
        }
    }

    [Fact]
    public void Catalog_DeletableRulesAreNotHighRisk()
    {
        // Anything the catalog marks deletable must not simultaneously be high risk;
        // that combination would let a risky item look safe to remove.
        foreach (var rule in new BuiltInRuleProvider().GetRules().Where(r => r.CanDelete))
            Assert.True(rule.RiskLevel <= RiskLevel.Medium, $"{rule.Id} is deletable but {rule.RiskLevel}.");
    }

    [Theory]
    [InlineData(@"C:\Users\a\.nuget\packages", "nuget.global-packages")]
    [InlineData(@"C:\Users\a\source\proj\bin", "dotnet.build-output")]
    [InlineData(@"C:\Users\a\source\proj\obj", "dotnet.build-output")]
    [InlineData(@"C:\Users\a\source\proj\.vs", "visualstudio.solution-cache")]
    [InlineData(@"C:\Users\a\source\proj\node_modules", "npm.node-modules")]
    [InlineData(@"C:\Users\a\.gradle", "gradle.home")]
    [InlineData(@"C:\Users\a\.m2\repository", "maven.repository")]
    [InlineData(@"C:\Users\a\.ollama\models", "ollama.models")]
    [InlineData(@"C:\Users\a\.cache\huggingface", "huggingface.cache")]
    [InlineData(@"C:\Users\a\.android\avd", "android.avd")]
    [InlineData(@"C:\proj\DerivedDataCache", "unreal.ddc")]
    [InlineData(@"C:\Games\steamapps", "games.steam-library")]
    [InlineData(@"C:\Users\a\AppData\Local\Temp", "system.temp")]
    [InlineData(@"C:\Users\a\source\proj\.git", "git.repository")]
    public void Match_IdentifiesKnownFolders(string path, string expectedRuleId)
    {
        var match = TestFixtures.Engine().Match(TestFixtures.Folder(path));

        Assert.NotNull(match);
        Assert.Equal(expectedRuleId, match!.Rule.Id);
    }

    [Fact]
    public void Match_PathPatternBeatsGenericNamePattern()
    {
        // "pip\Cache" is far more specific than a bare folder-name rule would be.
        var match = TestFixtures.Engine().Match(
            TestFixtures.Folder(@"C:\Users\a\AppData\Local\pip\Cache"));

        Assert.NotNull(match);
        Assert.Equal("pip.cache", match!.Rule.Id);
    }

    [Fact]
    public void Match_RequiresSegmentBoundaryForPathSuffix()
    {
        // "...\notpip\Cache" must not match the "pip\Cache" rule.
        var match = TestFixtures.Engine().Match(TestFixtures.Folder(@"C:\Users\a\notpip\Cache"));

        Assert.True(match is null || match.Rule.Id != "pip.cache");
    }

    [Fact]
    public void Match_ReturnsNullForUnknownFolder()
    {
        var match = TestFixtures.Engine().Match(TestFixtures.Folder(@"D:\Clients\Acme\Contracts"));

        Assert.Null(match);
    }

    [Fact]
    public void Match_DoesNotApplyFolderRulesToFiles()
    {
        // A *file* named node_modules must not match the folder rule.
        var match = TestFixtures.Engine().Match(TestFixtures.File(@"C:\tmp\node_modules"));

        Assert.True(match is null || match.Rule.Id != "npm.node-modules");
    }

    [Theory]
    [InlineData(@"D:\VMs\dev.vhdx", "vm.disk-images")]
    [InlineData(@"D:\Downloads\ubuntu.iso", "archive.files")]
    [InlineData(@"D:\Backups\db.zip", "archive.files")]
    public void Match_IdentifiesFilesByExtension(string path, string expectedRuleId)
    {
        var match = TestFixtures.Engine().Match(TestFixtures.File(path));

        Assert.NotNull(match);
        Assert.Equal(expectedRuleId, match!.Rule.Id);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var match = TestFixtures.Engine().Match(TestFixtures.Folder(@"C:\Users\a\source\proj\NODE_MODULES"));

        Assert.NotNull(match);
        Assert.Equal("npm.node-modules", match!.Rule.Id);
    }
}
