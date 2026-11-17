using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Core.Services.Fin;

internal static class FinanceCostCommandCanonicalV1
{
    private static readonly JsonSerializerOptions Input = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static byte[] Bytes(object value)
    {
        var text = new StringBuilder();
        Append(text, JsonSerializer.SerializeToElement(value, Input));
        return Utf8.GetBytes(text.ToString());
    }

    internal static byte[] Hash(byte[] canonical) => SHA256.HashData([.."COST-CANON-1\n"u8, ..canonical]);
    internal static byte[] Digest(object value) => Hash(Bytes(value));
    internal static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    internal static string Time(DateTimeOffset time)
        => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static void Append(StringBuilder target, JsonElement value)
    {
        var separator = "";
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                target.Append('{');
                var properties = value.EnumerateObject()
                    .OrderBy(property => property.Name.Normalize(NormalizationForm.FormC), StringComparer.Ordinal);
                foreach (var property in properties)
                {
                    target.Append(separator);
                    separator = ",";
                    AppendString(target, property.Name);
                    target.Append(':');
                    Append(target, property.Value);
                }
                target.Append('}');
                break;
            case JsonValueKind.Array:
                target.Append('[');
                foreach (var item in value.EnumerateArray())
                {
                    target.Append(separator);
                    separator = ",";
                    Append(target, item);
                }
                target.Append(']');
                break;
            case JsonValueKind.String:
                AppendString(target, value.GetString()!);
                break;
            case JsonValueKind.Number when value.TryGetInt64(out var integer):
                target.Append(integer.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Null:
            case JsonValueKind.True:
            case JsonValueKind.False:
                target.Append(value.GetRawText());
                break;
            default:
                throw new InvalidOperationException("成本命令的金额、数量和版本值使用字符串。");
        }
    }

    private static void AppendString(StringBuilder target, string text)
    {
        target.Append('"');
        foreach (var code in text.Normalize(NormalizationForm.FormC))
        {
            var escape = code switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)code).ToString("x4", CultureInfo.InvariantCulture),
                _ => null
            };
            if (escape == null)
                target.Append(code);
            else
                target.Append(escape);
        }
        target.Append('"');
    }
}
