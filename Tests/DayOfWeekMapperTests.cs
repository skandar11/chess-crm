using ChessCrm.Bot.Services;

namespace Tests;

/// <summary>Group A: DayOfWeekMapper unit tests (no DB needed).</summary>
public class DayOfWeekMapperTests
{
    // A1: Точное совпадение — "понедельник" → monday
    [Theory]
    [InlineData("понедельник", "monday")]
    [InlineData("вторник",     "tuesday")]
    [InlineData("среда",       "wednesday")]
    [InlineData("четверг",     "thursday")]
    [InlineData("пятница",     "friday")]
    [InlineData("суббота",     "saturday")]
    [InlineData("воскресенье", "sunday")]
    public void A1_ExactFullName(string input, string expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToEnglish(input));
    }

    // A2: Короткое сокращение — "пн" → monday, "вт" → tuesday, ...
    [Theory]
    [InlineData("пн", "monday")]
    [InlineData("вт", "tuesday")]
    [InlineData("ср", "wednesday")]
    [InlineData("чт", "thursday")]
    [InlineData("пт", "friday")]
    [InlineData("сб", "saturday")]
    [InlineData("вс", "sunday")]
    public void A2_ShortAbbreviation(string input, string expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToEnglish(input));
    }

    // A3: Промежуточные префиксы — "пон", "вто", "сре", "чет", "пят", "суб", "вос"
    [Theory]
    [InlineData("пон",   "monday")]
    [InlineData("вто",   "tuesday")]
    [InlineData("сре",   "wednesday")]
    [InlineData("чет",   "thursday")]
    [InlineData("пят",   "friday")]
    [InlineData("суб",   "saturday")]
    [InlineData("вос",   "sunday")]
    public void A3_IntermediatePrefix(string input, string expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToEnglish(input));
    }

    // A4: Длинные префиксы — "понед", "вторн", "серед", "четв", "пятн", "суббот", "воскр"
    [Theory]
    [InlineData("понед",  "monday")]
    [InlineData("вторн",  "tuesday")]
    [InlineData("серед",  "wednesday")]
    [InlineData("четв",   "thursday")]
    [InlineData("пятн",   "friday")]
    [InlineData("суббот", "saturday")]
    [InlineData("воскр",  "sunday")]
    public void A4_LongerPrefix(string input, string expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToEnglish(input));
    }

    // A5: Case-insensitive — "Понедельник", "ВТОРНИК"
    [Theory]
    [InlineData("Понедельник", "monday")]
    [InlineData("ВТОРНИК",     "tuesday")]
    [InlineData("Ср",          "wednesday")]
    [InlineData("СуБбОтА",    "saturday")]
    public void A5_CaseInsensitive(string input, string expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToEnglish(input));
    }

    // A6: Пробелы вокруг — "  пн  " → monday
    [Theory]
    [InlineData("  пн  ", "monday")]
    [InlineData(" среда ", "wednesday")]
    public void A6_TrimWhitespace(string input, string expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToEnglish(input));
    }

    // A7: null / пустая строка → null
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A7_NullOrEmpty_ReturnsNull(string? input)
    {
        Assert.Null(DayOfWeekMapper.MapToEnglish(input));
    }

    // A8: Мусор → null
    [Theory]
    [InlineData("лунный день")]
    [InlineData("xyz")]
    [InlineData("123")]
    public void A8_Garbage_ReturnsNull(string input)
    {
        Assert.Null(DayOfWeekMapper.MapToEnglish(input));
    }

    // A9: MapToDayOfWeek — возвращает enum
    [Theory]
    [InlineData("понедельник", DayOfWeek.Monday)]
    [InlineData("вт",          DayOfWeek.Tuesday)]
    [InlineData("суб",         DayOfWeek.Saturday)]
    public void A9_MapToDayOfWeek(string input, DayOfWeek expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToDayOfWeek(input));
    }

    // A10: Fuzzy prefix — "понедельн" (не в словаре, но StartsWith "понед") → monday
    [Theory]
    [InlineData("понедельн", "monday")]
    [InlineData("вторни",    "tuesday")]
    [InlineData("средо",     "wednesday")]
    [InlineData("субботн",   "saturday")]
    public void A10_FuzzyPrefixNotInDict(string input, string expected)
    {
        Assert.Equal(expected, DayOfWeekMapper.MapToEnglish(input));
    }
}
