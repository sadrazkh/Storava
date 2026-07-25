namespace Storava.Rules.Model;

/// <summary>What part of a path a pattern is matched against.</summary>
public enum RuleMatchTarget
{
    /// <summary>The item's own name (folder or file name).</summary>
    Name = 0,

    /// <summary>A path segment sequence, e.g. "AppData\Local\npm-cache".</summary>
    PathSuffix = 1,

    /// <summary>Anywhere within the full path.</summary>
    PathContains = 2
}
