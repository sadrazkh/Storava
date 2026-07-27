namespace Storava.Agent;

/// <summary>
/// A deliberately small argument reader. The Agent has three verbs and a handful of options, and
/// pulling in a parsing framework for that would be more surface than it saves.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine(string verb)
    {
        Verb = verb;
    }

    public string Verb { get; }

    public static CommandLine Parse(string[] args)
    {
        string verb = args.Length > 0 && !args[0].StartsWith('-')
            ? args[0].ToLowerInvariant()
            : "help";

        var parsed = new CommandLine(verb);

        for (int index = verb == "help" ? 0 : 1; index < args.Length; index++)
        {
            string current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
                continue;

            string name = current[2..];

            // "--name value" and "--name=value" both work; a bare "--flag" is a flag.
            int equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                parsed._options[name[..equals]] = name[(equals + 1)..];
                continue;
            }

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed._options[name] = args[index + 1];
                index++;
                continue;
            }

            parsed._flags.Add(name);
        }

        return parsed;
    }

    public string? Option(string name) =>
        _options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public bool HasFlag(string name) => _flags.Contains(name);
}
