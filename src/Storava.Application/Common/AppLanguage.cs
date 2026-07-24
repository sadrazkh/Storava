namespace Storava.Application.Common;

/// <summary>UI language. Persian is RTL, English is LTR.</summary>
public enum AppLanguage
{
    Persian = 0,
    English = 1
}

public static class AppLanguageExtensions
{
    /// <summary>The BCP-47 culture name for this language.</summary>
    public static string ToCultureName(this AppLanguage language) => language switch
    {
        AppLanguage.Persian => "fa-IR",
        _ => "en-US"
    };

    public static bool IsRightToLeft(this AppLanguage language) => language == AppLanguage.Persian;
}
