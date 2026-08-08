using System.Text.RegularExpressions;

namespace CP6.Client.Core;

public static partial class SensitiveDataRedactor
{
    [GeneratedRegex(
        "(?i)(access[_-]?token|refresh[_-]?token|password|otp|authorization)\\s*[:=]\\s*([^\\s,;]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    public static string Redact(string text)
        => SecretPattern().Replace(text, "$1=[REDACTED]");
}
