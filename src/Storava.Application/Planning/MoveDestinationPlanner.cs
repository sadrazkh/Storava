namespace Storava.Application.Planning;

/// <summary>
/// Works out where each moved item lands when the user names one folder for all of them.
/// <para>
/// Choosing a destination per step is exact but tedious; choosing one folder for everything is what
/// people actually want when they are clearing a drive. The cost of the second is collisions —
/// <c>projects\api\node_modules</c> and <c>projects\web\node_modules</c> both want to be called
/// <c>node_modules</c> under the same root, and the executor refuses a destination that already
/// holds anything. Without this, the first move would succeed and every later one would fail with
/// something that reads like a bug.
/// </para>
/// </summary>
public static class MoveDestinationPlanner
{
    /// <summary>
    /// Where <paramref name="sourcePath"/> should land under <paramref name="root"/>, avoiding
    /// anything in <paramref name="taken"/>.
    /// <para>
    /// A colliding name gains the folder it came from — <c>node_modules</c> becomes
    /// <c>api-node_modules</c> — because that is the piece of context that tells the two apart. If
    /// that is still not unique it keeps borrowing from further up the path, and only falls back to
    /// a number once the path itself has run out.
    /// </para>
    /// </summary>
    public static string Resolve(string root, string sourcePath, ISet<string> taken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(taken);

        var segments = Segments(sourcePath);
        if (segments.Count == 0)
            return Claim(Path.Combine(root, "moved"), taken);

        // Grow the name leftwards through the path: "node_modules", then "api-node_modules", then
        // "projects-api-node_modules".
        for (int depth = 1; depth <= segments.Count; depth++)
        {
            string name = string.Join('-', segments.TakeLast(depth));
            string candidate = Path.Combine(root, name);

            if (!taken.Contains(candidate))
            {
                taken.Add(candidate);
                return candidate;
            }
        }

        // The whole path is spoken for, so a number is all that is left. It is appended to the
        // longest borrowed name rather than the bare leaf: "data-cache-2" still says where the
        // thing came from, where "cache-2" would leave the user guessing which of two it is.
        return Claim(Path.Combine(root, string.Join('-', segments)), taken);
    }

    /// <summary>
    /// Resolves a whole set at once, in the order given, so the result is stable: the same
    /// selection always produces the same destinations rather than depending on which step the
    /// user happened to open first.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ResolveAll(string root, IEnumerable<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source) || resolved.ContainsKey(source))
                continue;

            resolved[source] = Resolve(root, source, taken);
        }

        return resolved;
    }

    /// <summary>Last resort: a numeric suffix, for when the path gave nothing left to borrow.</summary>
    private static string Claim(string preferred, ISet<string> taken)
    {
        if (taken.Add(preferred))
            return preferred;

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{preferred}-{suffix}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// The path's own segments, without the volume. The drive letter is dropped because every
    /// source in one selection usually shares it, so it would disambiguate nothing while making
    /// every name longer.
    /// </summary>
    private static List<string> Segments(string path)
    {
        var trimmed = path.Trim().TrimEnd('\\', '/');

        return [.. trimmed
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !segment.EndsWith(':'))];
    }
}
