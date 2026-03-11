namespace ChessCrm.Bot.Services;

/// <summary>
/// Maps Russian day-of-week strings to English day names and <see cref="DayOfWeek"/> enum.
/// Supports exact and prefix-based fuzzy matching.
/// </summary>
public static class DayOfWeekMapper
{
    /// <summary>
    /// Dictionary of Russian prefixes → (English day name for DB, DayOfWeek enum).
    /// Keys are ordered from shortest to longest for each day.
    /// Lookup: exact match first, then StartsWith by longest matching prefix.
    /// </summary>
    private static readonly (string Prefix, string English, DayOfWeek Dow)[] Entries =
    [
        // понедельник
        ("пн",          "monday",    DayOfWeek.Monday),
        ("пон",         "monday",    DayOfWeek.Monday),
        ("понед",       "monday",    DayOfWeek.Monday),
        ("понедельник", "monday",    DayOfWeek.Monday),
        // вторник
        ("вт",          "tuesday",   DayOfWeek.Tuesday),
        ("вто",         "tuesday",   DayOfWeek.Tuesday),
        ("вторн",       "tuesday",   DayOfWeek.Tuesday),
        ("вторник",     "tuesday",   DayOfWeek.Tuesday),
        // среда
        ("ср",          "wednesday", DayOfWeek.Wednesday),
        ("сре",         "wednesday", DayOfWeek.Wednesday),
        ("серед",       "wednesday", DayOfWeek.Wednesday),
        ("среда",       "wednesday", DayOfWeek.Wednesday),
        // четверг
        ("чт",          "thursday",  DayOfWeek.Thursday),
        ("чет",         "thursday",  DayOfWeek.Thursday),
        ("четв",        "thursday",  DayOfWeek.Thursday),
        ("четверг",     "thursday",  DayOfWeek.Thursday),
        // пятница
        ("пт",          "friday",    DayOfWeek.Friday),
        ("пят",         "friday",    DayOfWeek.Friday),
        ("пятн",        "friday",    DayOfWeek.Friday),
        ("пятница",     "friday",    DayOfWeek.Friday),
        // суббота
        ("сб",          "saturday",  DayOfWeek.Saturday),
        ("суб",         "saturday",  DayOfWeek.Saturday),
        ("суббот",      "saturday",  DayOfWeek.Saturday),
        ("суббота",     "saturday",  DayOfWeek.Saturday),
        // воскресенье
        ("вс",          "sunday",    DayOfWeek.Sunday),
        ("вос",         "sunday",    DayOfWeek.Sunday),
        ("воскр",       "sunday",    DayOfWeek.Sunday),
        ("воскресенье", "sunday",    DayOfWeek.Sunday),
    ];

    /// <summary>
    /// Maps a Russian day-of-week string to English name (e.g. "monday").
    /// Returns null if no match found.
    /// </summary>
    public static string? MapToEnglish(string? input)
    {
        var result = Resolve(input);
        return result?.English;
    }

    /// <summary>
    /// Maps a Russian day-of-week string to <see cref="DayOfWeek"/> enum.
    /// Returns null if no match found.
    /// </summary>
    public static DayOfWeek? MapToDayOfWeek(string? input)
    {
        var result = Resolve(input);
        return result?.Dow;
    }

    private static (string English, DayOfWeek Dow)? Resolve(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = input.Trim().ToLowerInvariant();

        // 1. Exact match
        foreach (var entry in Entries)
        {
            if (entry.Prefix == normalized)
                return (entry.English, entry.Dow);
        }

        // 2. Prefix match — find the longest prefix that matches the input
        (string English, DayOfWeek Dow)? best = null;
        var bestLen = 0;
        foreach (var entry in Entries)
        {
            if (normalized.StartsWith(entry.Prefix) && entry.Prefix.Length > bestLen)
            {
                best = (entry.English, entry.Dow);
                bestLen = entry.Prefix.Length;
            }
        }

        return best;
    }
}
