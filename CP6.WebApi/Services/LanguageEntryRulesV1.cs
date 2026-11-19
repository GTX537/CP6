using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.Services;

internal static partial class LanguageEntryRulesV1
{
    internal static FieldRegistry.Field[] Fields { get; } =
    [
        new("ZhCN", "简体中文"), new("ZhTW", "繁体中文"), new("En", "英语"),
        new("Ja", "日语"), new("Ko", "韩语")
    ];

    [GeneratedRegex(@"(?<!\{)\{([A-Za-z_][A-Za-z0-9_.-]*|[0-9]+)(?:\s*,\s*-?[0-9]+)?(?::[^{}]+)?\}(?!\})",
        RegexOptions.CultureInvariant)]
    private static partial Regex Placeholders();

    internal static void ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200 || key.Any(char.IsControl) ||
            key.Split('.').Any(part => part is "__proto__" or "prototype" or "constructor"))
            throw new LanguageProblemV1(400, "LANG_KEY_INVALID");
    }

    internal static void ValidatePlaceholders(IEnumerable<string?> translations)
    {
        string[]? expected = null;
        foreach (var value in translations.Where(value => !string.IsNullOrEmpty(value)))
        {
            var actual = Placeholders().Matches(value!).Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            expected ??= actual;
            if (!expected.SequenceEqual(actual)) throw new LanguageProblemV1(422, "LANG_PLACEHOLDER_MISMATCH");
        }
    }
}
