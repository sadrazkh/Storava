using Storava.App.Models;
using Storava.Domain.Enums;

namespace Storava.App.Services;

/// <summary>
/// Decides which rows the cleanup list shows.
/// <para>
/// Pulled out of the view model so it can be exercised on its own. The rules here are small but
/// they interact — three filters that each narrow the list, where the wrong combination silently
/// shows nothing — and that is exactly the kind of thing worth pinning down away from a page that
/// needs half the application composed before it will start.
/// </para>
/// </summary>
public static class CleanupFilter
{
    /// <param name="suggestedOnly">Limit to what the rule catalog proposed.</param>
    /// <param name="selectedOnly">
    /// Limit to what is ticked. A selection can be spread over thousands of rows and several
    /// filters, and until now the only way to see what was actually in it was to remember. That
    /// matters most when one item is refusing to run: finding it again in the full list, to take it
    /// out, meant hunting for it.
    /// </param>
    /// <param name="search">Matched against the name and the path, case-insensitively.</param>
    /// <param name="risks">
    /// Which risk levels to keep. An empty set means no opinion and keeps everything — a filter
    /// nobody set should never be the reason a list is empty.
    /// </param>
    public static IEnumerable<CleanupItemModel> Apply(
        IEnumerable<CleanupItemModel> items,
        bool suggestedOnly,
        string? search,
        IReadOnlySet<RiskLevel> risks,
        bool selectedOnly = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(risks);

        var term = search?.Trim() ?? string.Empty;

        foreach (var item in items)
        {
            if (selectedOnly && !item.IsSelected)
                continue;

            if (suggestedOnly && !item.IsSuggested)
                continue;

            if (risks.Count > 0 && !risks.Contains(item.Risk))
                continue;

            if (term.Length > 0 &&
                !item.Title.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !item.Path.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return item;
        }
    }
}
