namespace Storava.AI.Tests;

/// <summary>
/// The prompt is the only place the model is told what shape to answer in, so the report toggle
/// has to be visible there — otherwise it would silently cost tokens the user asked not to spend.
/// </summary>
public class PromptBuilderTests
{
    [Fact]
    public void AsksForTheReportSection_ByDefault()
    {
        string prompt = PromptBuilder.BuildSystemPrompt("en");

        Assert.Contains("\"report\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"nextSteps\"", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void OmitsTheReportSection_WhenItIsTurnedOff()
    {
        string prompt = PromptBuilder.BuildSystemPrompt("en", includeReport: false);

        Assert.Contains("Do not include a \"report\" object", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"nextSteps\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"overview\"", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fa", "Persian")]
    [InlineData("fa-IR", "Persian")]
    [InlineData("en", "English")]
    public void NamesTheLanguageTheAnswerMustBeWrittenIn(string language, string expected)
    {
        Assert.Contains(expected, PromptBuilder.BuildSystemPrompt(language), StringComparison.Ordinal);
    }

    [Fact]
    public void StatesThatStoravaExecutesNothing()
    {
        // The validator is the real guarantee, but the model should never claim otherwise either.
        Assert.Contains("advisory only", PromptBuilder.BuildSystemPrompt("en"), StringComparison.Ordinal);
    }
}
