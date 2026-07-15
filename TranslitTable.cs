namespace Translit;

internal static class TranslitTable
{
    private static readonly Dictionary<string, string> _map = new() {
        ["a"] = "а",
        ["b"] = "б",
        ["c"] = "ц",
        ["d"] = "д",
        ["e"] = "е",
        ["f"] = "ф",
        ["g"] = "г",
        ["h"] = "х",
        ["i"] = "і",
        ["j"] = "й",
        ["k"] = "к",
        ["l"] = "л",
        ["m"] = "м",
        ["n"] = "н",
        ["o"] = "о",
        ["p"] = "п",
        ["r"] = "р",
        ["s"] = "с",
        ["t"] = "т",
        ["u"] = "у",
        ["v"] = "в",
        ["y"] = "и",
        ["z"] = "з",
        ["q"] = "щ",
        ["w"] = "ш",
        ["x"] = "х",

        ["'"] = "ь",
        ["`"] = "'",

        ["сh"] = "ш",
        ["шh"] = "щ",
        ["цh"] = "ч",
        ["зh"] = "ж",
        ["йe"] = "є",
        ["йa"] = "я",
        ["йu"] = "ю",
        ["йi"] = "ї",
        ["г'"] = "ґ",
        ["ь'"] = "Ь",
    };

    public static IReadOnlyDictionary<string, string> Map => _map;

    private static readonly HashSet<char> _resetAfter = new() {
        '\'',
    };

    public static IReadOnlySet<char> ResetAfter => _resetAfter;
}