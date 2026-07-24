namespace Storava.Domain.Enums;

/// <summary>
/// The action proposed for an item. The default for every item is <see cref="NoAction"/>;
/// nothing is ever acted upon unless the user explicitly changes this and confirms.
/// </summary>
public enum SuggestedAction
{
    NoAction = 0,
    Review = 1,
    Move = 2,
    Delete = 3,
    Ignore = 4
}
