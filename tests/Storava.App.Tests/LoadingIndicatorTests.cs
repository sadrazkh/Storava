using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Storava.App.ViewModels;

namespace Storava.App.Tests;

/// <summary>
/// A page that is fetching something has to say so.
/// <para>
/// Once the database work moved off the UI thread the window stopped freezing — and a page reading
/// a scan of several million rows stopped looking frozen and started looking like a page that had
/// decided to stay empty. The user's report was that pressing "bring back my earlier data" appeared
/// to do nothing at all.
/// </para>
/// </summary>
public class LoadingIndicatorTests
{
    /// <summary>The pages that fetch, and the method that does it for each.</summary>
    private static readonly (string Page, string Method)[] LoadingEntryPoints =
    [
        ("CleanupViewModel", "LoadAsync"),
        ("HistoryViewModel", "LoadAsync"),
        ("AnalysisViewModel", "LoadAsync"),
        ("ScanExplorerViewModel", "LoadAsync"),
        ("ReportsViewModel", "LoadAsync"),
        ("DashboardViewModel", "LoadLastScanAsync"),
    ];

    /// <summary>
    /// Read from source rather than run, because what matters is that the call is there on the way
    /// in. Driving these would mean standing up a database, a scan and a rules catalog to observe a
    /// flag that is set on the first line.
    /// </summary>
    [Fact]
    public void EveryPageThatFetchesSaysThatItIsFetching()
    {
        var missing = new List<string>();

        foreach (var (page, method) in LoadingEntryPoints)
        {
            var source = ReadPageSource(page);
            var body = ExtractMethodBody(source, method);

            Assert.False(body is null, $"{page}.{method} was not found — this list is out of date.");

            if (!body!.Contains("BeginLoading(", StringComparison.Ordinal))
                missing.Add($"{page}.{method}");
        }

        Assert.True(
            missing.Count == 0,
            $"These fetch without showing anything: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The indicator has to clear itself on every path out, including the one where something
    /// throws. A page left spinning over a failure it never mentions is worse than no indicator.
    /// </summary>
    [Fact]
    public void TheIndicatorIsScopedRatherThanTurnedOffByHand()
    {
        var offenders = new List<string>();

        foreach (var (page, method) in LoadingEntryPoints)
        {
            var body = ExtractMethodBody(ReadPageSource(page), method)!;

            // `using var` is what makes the early returns and the throws safe. An assignment is a
            // promise to remember, and the whole reason this is a scope is that people do not.
            if (!body.Contains("using var loading = BeginLoading(", StringComparison.Ordinal))
                offenders.Add($"{page}.{method}");
        }

        Assert.True(
            offenders.Count == 0,
            $"These set the indicator without scoping it: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The shell draws one indicator for whichever page is on screen, which only works because the
    /// state is on the base every page derives from.
    /// </summary>
    [Fact]
    public void TheBusyStateBelongsToEveryPage()
    {
        Assert.NotNull(typeof(ViewModelBase).GetProperty("IsLoading"));
        Assert.NotNull(typeof(ViewModelBase).GetProperty("LoadingMessageKey"));
        Assert.NotNull(typeof(ViewModelBase)
            .GetMethod("BeginLoading", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    /// <summary>
    /// A page declaring its own copy would shadow the base's, and the shell binds to the base's —
    /// so the page would look busy to itself and idle to the window around it.
    /// </summary>
    [Fact]
    public void NoPageKeepsItsOwnCopyOfTheBusyState()
    {
        var offenders = Directory
            .EnumerateFiles(PagesDirectory, "*ViewModel.cs")
            .Where(file => File.ReadAllText(file).Contains("private bool _isLoading", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These shadow the shared busy state: {string.Join(", ", offenders)}");
    }

    // --- reading the source ------------------------------------------------------------------

    private static string PagesDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
                directory = directory.Parent;

            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "src", "Storava.App", "ViewModels", "Pages");
        }
    }

    private static string ReadPageSource(string page) =>
        File.ReadAllText(Path.Combine(PagesDirectory, $"{page}.cs"));

    /// <summary>
    /// Everything from the method's opening brace to the matching one, by counting braces. Crude,
    /// and enough: these are ordinary method bodies, not arbitrary text.
    /// <para>
    /// The signature has to be a declaration with a block body. A looser pattern matched
    /// <c>private void OnLanguageChanged(…) => _ = LoadAsync();</c> first and went on to read the
    /// wrong method, reporting a page as missing an indicator it in fact had — a test failing over
    /// its own parsing is worse than no test, because the obvious response is to change the code.
    /// </para>
    /// </summary>
    private static string? ExtractMethodBody(string source, string method)
    {
        var signature = new Regex(
            $@"^[ \t]+(?:private|public|protected|internal)[^\n=]*?\b{Regex.Escape(method)}\s*\([^\n]*\)\s*\r?\n[ \t]*\{{",
            RegexOptions.Multiline);

        var match = signature.Match(source);
        if (!match.Success)
            return null;

        int open = source.IndexOf('{', match.Index);
        if (open < 0)
            return null;

        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[open..index];
        }

        return null;
    }
}
