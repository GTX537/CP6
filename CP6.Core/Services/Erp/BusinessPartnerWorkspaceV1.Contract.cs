using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Erp;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    public const string DirectoryOwner = "Main.Erp.BusinessPartnerDirectory.v1";
    public const string ProfileOwner = "Main.Erp.BusinessPartnerProfile.v1";
    private const string Resource = "erp-business-partner", View = "profile-query", CommandRead = "command-result.read";
    private const string CommandAuditRead = "command-result.audit.read";
    private const string PreRegistered = "PRE_REGISTERED", Registered = "REGISTERED", UnknownLegacy = "UNKNOWN_LEGACY";
    private const string Active = "ACTIVE", Retired = "RETIRED", Clear = "CLEAR", Frozen = "FROZEN";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex BusinessCode = new("^[A-Z0-9-]{1,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly string[] CoreEditFields = ["bpName", "bpAbbrev", "baseCd", "commercialCurrency"];
    private static readonly string[] CoreIdentityFields = ["bpCd", "BpId", "bpId", "legalEntityKey", "roles", "tenantId"];
    private static readonly string[] ReadOnlyFields = ["status", "registration", "hold", "isFrozen", "rowVersion", "crmAccountId", "creditLimit", "isDeleted", "gifuInterfaceFlg", "mcTransferFlg"];
    private static readonly string[] BankKinds = ["CREDIT_MASTER", "PAYMENT_BANK", "RECEIPT_BANK"];

    private static readonly (string Key, string Source, string[] Targets, bool Required)[] Slots =
    [
        ("AP_SCHEDULE", "AccountsPayable", ["PaymentSchedule"], true),
        ("AR_BILLING", "AccountsReceivable", ["Billing"], true),
        ("BILLING_CREDIT_AR", "Billing", ["AccountsReceivable", "CreditMgmt"], false),
        ("BILLING_RECEIPT", "Billing", ["Receipt"], true),
        ("CUSTOMER_AR", "Customer", ["AccountsReceivable"], true),
        ("CUSTOMER_CREDIT", "Customer", ["CreditMgmt"], false),
        ("PARENT_CUSTOMER", "Customer", ["Customer"], false),
        ("SCHEDULE_PAYEE", "PaymentSchedule", ["Payment"], true),
        ("SUPPLIER_AP", "Supplier", ["AccountsPayable"], true)
    ];

    private static readonly string[] Roles =
    [
        "Customer", "AccountsReceivable", "Billing", "Receipt", "Delivery", "Supplier", "AccountsPayable",
        "PaymentSchedule", "Payment", "CreditMgmt", "Maker", "PaidSupply", "RebuyObligation"
    ];
    private static readonly Dictionary<string, System.Reflection.PropertyInfo> RoleColumns =
        Roles.ToDictionary(role => role, role => typeof(BusinessPartner).GetProperty(role + "Flg")!, StringComparer.Ordinal);
    public static IReadOnlyList<CP6.Core.Services.Sys.FieldRegistry.Field> PermissionFields => BusinessPartnerProfileFieldsV1.Fields;

    internal static OrderPriceCorrectionException Invalid(string code = "FIELD_INVALID") => new(code, 400);
    private static OrderPriceCorrectionException Rejected(string code, int status = 422) => new(code, status);
    private static OrderPriceCorrectionException Missing() => new("BUSINESS_PARTNER_NOT_FOUND", 404);
    private static OrderPriceCorrectionException Stale() => new("PREVIEW_STALE", 412);
    private static OrderPriceCorrectionException Denied(string code = "ACTION_FORBIDDEN") => new(code, 403);

    private sealed record Issue(string Field, string Code, string? RequiredRole = null, BusinessPartnerFinanceSourceRefV1? Reference = null);
    public sealed class IssuesException : Exception
    {
        public object Details { get; }
        internal IssuesException(object details) : base("交易伙伴必填资料或引用尚未核实。") => Details = details;
    }

    private static void RejectIssues(Issue[] issues)
    {
        if (issues.Length == 0) return;
        var code = issues.Any(x => x.Code is "REFERENCE_UNPROVEN" or "OWNER_PARTICIPANT_UNPROVEN")
            ? "REFERENCE_UNPROVEN" : issues[0].Code;
        throw new OrderPriceCorrectionException(code, code is "DUPLICATE_REVIEW_REQUIRED" or "BP_CODE_EXISTS" ? 409 : 422, new IssuesException(issues));
    }

    private static Dictionary<string, JsonElement> Object(JsonElement input, string[] allowed, params string[] required)
    {
        if (input.ValueKind != JsonValueKind.Object) throw Invalid();
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in input.EnumerateObject())
            if (!allowed.Contains(member.Name, StringComparer.Ordinal) || !members.TryAdd(member.Name, member.Value))
                throw Invalid("UNKNOWN_FIELD");
        if (required.Any(field => !members.ContainsKey(field))) throw Invalid("FIELD_REQUIRED");
        return members;
    }

    private static Dictionary<string, JsonElement> Exact(JsonElement input, params string[] fields)
        => Object(input, fields, fields);

    private static JsonElement[] Array(JsonElement value, int max)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > max) throw Invalid();
        return value.EnumerateArray().ToArray();
    }

    internal static string? OptionalText(JsonElement value, int length)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString()!.Trim();
        if (text.Length > length || text.Any(char.IsControl)) throw Invalid();
        _ = Utf8.GetByteCount(text);
        return text.Length == 0 ? null : text;
    }

    private static string Text(JsonElement value, int length)
        => OptionalText(value, length) ?? throw Invalid("FIELD_REQUIRED");

    private static string Token(JsonElement value, int length)
    {
        var text = Text(value, length);
        if (text != value.GetString()) throw Invalid();
        return text;
    }

    private static bool Flag(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? value.GetBoolean() : throw Invalid();

    private static int Positive(JsonElement value, int maximum)
        => value.ValueKind == JsonValueKind.Number &&
           int.TryParse(value.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var result) &&
           result > 0 && result <= maximum ? result : throw Invalid();

    private static Guid Uuid(JsonElement value) => ParseId(Token(value, 36));
    private static Guid ParseId(string? text)
        => Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && id.ToString("D") == text
            ? id : throw Invalid();

    private static string Revision(long revision) => revision.ToString(CultureInfo.InvariantCulture);
    private static long Version(JsonElement value)
    {
        var text = Token(value, 19);
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1 || Revision(revision) != text)
            throw Invalid();
        return revision;
    }

    private static string Digest(JsonElement item)
    {
        var text = Token(item, 64);
        return HashPattern.IsMatch(text) ? text : throw Invalid();
    }

    internal static byte[] Canonical<T>(T value)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) WriteCanonical(writer, JsonSerializer.SerializeToElement(value, Json));
        return output.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var field in element.EnumerateObject().OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(field.Name);
                WriteCanonical(writer, field.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var member in element.EnumerateArray()) WriteCanonical(writer, member);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    private static byte[] Bytes(string value) => Utf8.GetBytes(value);
    private static byte[] Sha(byte[] value) => SHA256.HashData(value);
    private static string Hash<T>(T value) => Convert.ToHexString(Sha(Canonical(value))).ToLowerInvariant();
    private static string Instant(DateTimeOffset time) => time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string Instant(DateTime time) => Instant(new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Local)).ToUniversalTime());

    private static string RowTag(Guid id, long version, byte[] rowVersion) => $"\"BP-CAS/1:{id:D}:{version}:{Convert.ToBase64String(rowVersion)}\"";
    private static string PreviewTag(Guid id, string hash) => $"\"BP-PREVIEW/1:{id:D}:{hash}\"";

    private static void RequireStrongTag(string? value, string expected, bool stale = false)
    {
        if (value is null) throw Rejected("PRECONDITION_REQUIRED", 428);
        if (value == expected) return;
        if (value.Length < 3 || value[0] != '"' || value[^1] != '"' || value.Contains(',') || value.Contains('*'))
            throw Invalid("PRECONDITION_SYNTAX");
        throw stale ? Stale() : Rejected("BP_VERSION_STALE", 412);
    }

    private static string Key(JsonElement value, int length)
        => Token(value, length);

    private static string LegalKey(string? value)
        => value is { Length: >= 1 and <= 64 } && value.Trim() == value && !value.Any(char.IsControl)
            ? value : throw Invalid();

    private static string Name(BusinessPartner row) => row.BpName.TrimEnd();
    private static string Code(BusinessPartner row) => row.BpCd.TrimEnd();
    private static string Base(BusinessPartner row) => row.BaseCd.TrimEnd();
    private static string Lifecycle(BusinessPartner row) => row.IsDeleted || row.Status == 9 ? Retired : Active;
    private static string Hold(BusinessPartner row) => row.IsFrozen ? Frozen : Clear;
    private static string RegistrationOfLegacy(BusinessPartner row)
        => Lifecycle(row) == Retired ? UnknownLegacy : row.Status switch { 0 => PreRegistered, 1 => Registered, _ => UnknownLegacy };

    private static Dictionary<string, bool> RoleSet(BusinessPartner row)
        => Roles.ToDictionary(role => role, role => HasRole(row, role), StringComparer.Ordinal);

    private static bool HasRole(BusinessPartner row, string role) => (bool)RoleColumns[role].GetValue(row)!;
    private static void SetRoles(BusinessPartner row, Dictionary<string, bool> roles)
    {
        foreach (var role in Roles) RoleColumns[role].SetValue(row, roles[role]);
    }

    private static Dictionary<string, object?> Profile(BusinessPartner row)
        => BusinessPartnerProfileFieldsV1.Writable.ToDictionary(field => field.Name, field => NormalizeFixedColumn(field.Value(row)), StringComparer.Ordinal);

    private static object? NormalizeFixedColumn(object? value)
        => value is string text ? text.TrimEnd() : value;

    private sealed record Reference(Guid BpId, string BpCd, string? LegalEntityKey, string Revision, string? ContentHash);
    private sealed record State(string Registration, string Lifecycle, string Hold, string Revision, string LegacyScopeState);
    private sealed record RelationTarget(string Kind, Guid? BpId);
    private sealed record Related(string Slot, RelationTarget? Target);
    private sealed record FinanceLink(string Kind, BusinessPartnerFinanceSourceRefV1? Reference);
    private sealed record FinanceView(string Kind, BusinessPartnerFinanceSourceRefV1? Reference, string Availability,
        string? DisplayName = null, string? MaskedAccount = null, string? CreditLimit = null, string? Currency = null);
    private sealed record PreviewReference(Guid PreviewId, string IntentHash, string ExpiresAt);
    private sealed record Receipt(Guid CommandId, string Operation, string Outcome, Reference? Bp, MappingReference? Mapping, ReconcileReceipt? Reconcile,
        string? ErrorCode, string CompletedAt, string ReceiptHash);
    private sealed record CommandResponse(Receipt Receipt, bool Replayed);
    private sealed record Patch(string Field, JsonElement Value);
    private sealed record CandidateReference(Guid BpId, string Revision, string CandidateHash);
    private sealed record CandidateItem(CandidateReference Reference, string BpCd, string DisplayName, Dictionary<string, bool> Roles,
        string Registration, string Lifecycle, string Hold, string Availability, string[] MatchReasons);
    private sealed record CandidateSet(string Visibility, CandidateItem[] Items, string SetHash, string ScopeRevision);
    private sealed record IdentityCriteria(string? BpCd, string BpName, string? StdCoCd, string? EinType, string? Ein);
    private sealed record DuplicateDecision(string Kind, CandidateReference[] Acknowledged, string? Reason, CandidateReference? Existing);
    private sealed record StateRequest(Guid Id, long Revision, string Reason, string? RequestedHold, bool ConfirmRegistration);
}
