using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;
using static CP6.Core.Services.Mes.ResourceSourceStoreV1;

namespace CP6.Core.Services.Mes;

internal static class ResourceCapabilityValuesV1
{
    internal const string Owner = "Main.Mes.ResourceCapability.v1";
    internal const string Contract = "Main.Mes.ResourceCapability.commands.v1";
    internal static string Journal(string kind) { Resource(kind); return Owner + "." + kind; }
    private static readonly Regex Number = new(@"\A-?(0|[1-9][0-9]{0,12})\.[0-9]{8}\z", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
    private static readonly Regex Instant = new(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,6})?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
    internal static void Text(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value != value.Trim() || value != value.Normalize(NormalizationForm.FormC)
            || value.Any(char.IsControl)) throw Error("RESOURCE_CAPABILITY_TEXT_INVALID", 400);
    }
    private static DateTimeOffset Time(string value)
        => value != null && Instant.IsMatch(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time : throw Error("RESOURCE_CAPABILITY_TIME_INVALID", 400);
    internal static void Reference(ResourceCapabilityRefV1 value)
    {
        if (value == null || value.Owner != Owner || value.Id == Guid.Empty || !Digest(value.Digest)) throw Error("RESOURCE_CAPABILITY_REFERENCE_INVALID", 400);
        Resource(value.Kind); Revision(value.Version);
    }
    private static bool Digest(string? value) => value?.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static void Validate(string kind, ResourceCapabilityBodyV1 body)
    {
        Resource(kind);
        if (body == null || body.Source == null || body.Source.Owner != ResourceSourceStoreV1.Owner || body.Source.Kind != kind
            || body.Source.Id == Guid.Empty || !Digest(body.Source.Digest) || body.Capabilities == null || body.Capabilities.Length > 100
            || kind == "WORK_CENTER" && (body.Capabilities.Length != 0 || body.OutputUnit != null)) throw Error("RESOURCE_CAPABILITY_INPUT_INVALID", 400);
        Revision(body.Source.Version); Text(body.Basis, 2000);
        if ((body.TimeUnit == null) != (body.TimeBasis == null)) throw Error("RESOURCE_CAPABILITY_TIME_BASIS_REQUIRED", 400);
        if (body.TimeUnit != null) UomProfileValuesV1.Reference(body.TimeUnit);
        if (body.TimeBasis is { } basis)
        {
            if (basis.Owner != UomTimeBasisStoreV1.BasisOwner || basis.Id == Guid.Empty || !Digest(basis.Digest)) throw Error("RESOURCE_CAPABILITY_TIME_BASIS_INVALID", 400);
            Revision(basis.Version);
        }
        if (body.OutputUnit != null) UomProfileValuesV1.Reference(body.OutputUnit);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in body.Capabilities)
        {
            if (item == null || !keys.Add(item.Key)) throw Error("RESOURCE_CAPABILITY_KEY_INVALID", 400);
            Text(item.Key, 64); Text(item.Dimension, 32); Text(item.Basis, 2000);
            var from = Time(item.ValidFrom);
            if (item.ValidTo != null && Time(item.ValidTo) <= from) throw Error("RESOURCE_CAPABILITY_PERIOD_INVALID", 400);
            if (item.ValueKind == "DECIMAL")
            {
                if (item.DecimalValue == null || !Number.IsMatch(item.DecimalValue) || item.EnumValue != null || item.Unit == null)
                    throw Error("RESOURCE_CAPABILITY_DECIMAL_INVALID", 400);
                UomProfileValuesV1.Reference(item.Unit);
            }
            else if (item.ValueKind == "ENUM")
            {
                if (item.DecimalValue != null || item.Unit != null) throw Error("RESOURCE_CAPABILITY_ENUM_INVALID", 400);
                Text(item.EnumValue, 64);
            }
            else throw Error("RESOURCE_CAPABILITY_VALUE_KIND_INVALID", 400);
        }
    }
    internal static UomProfileRefV1[] Units(ResourceCapabilityBodyV1 body) => body.Capabilities.Select(x => x.Unit)
        .Concat([body.TimeUnit, body.OutputUnit]).OfType<UomProfileRefV1>().Distinct().ToArray();
}
