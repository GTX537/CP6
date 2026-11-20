using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private const int ExportLimit = 5000;
    private static readonly UTF8Encoding CsvEncoding = new(true, true);
    private static readonly string[] ExportCoreFields = ["BpCd", "BpName", "BaseCd", "Roles"];
    private static readonly string[] ExportIdentityColumns = ["BpCd", "BpName", "BaseCd", "LegalEntity"];
    private static readonly string[] ExportStateColumns = ["Registration", "Lifecycle", "Hold", "Revision", "CreatedAt"];
    private static readonly BusinessPartnerProfileFieldsV1.Field[] ExportSensitiveFields =
        BusinessPartnerProfileFieldsV1.Writable.Where(field => field.Sensitive).ToArray();
    internal static readonly HashSet<string> ExportPermissionFields =
        ExportCoreFields.Concat(ExportSensitiveFields.Select(field => field.Original)).ToHashSet(StringComparer.Ordinal);

    public sealed record ExportFile(byte[] Bytes, string PolicyVersion, DateTimeOffset AsOf);

    private async Task<ExportFile> Export(OperationsAccessV1 access, JsonElement body, CancellationToken ct)
    {
        if (!ExportCoreFields.All(access.CanExportPartnerField)) throw Denied("FIELD_READ_FORBIDDEN");
        var readable = await Readable(access, ct);
        // 历史银行等原列属于 Finance。正式下载只追加本 Owner 可披露的敏感资料列。
        var columns = ExportSensitiveFields.Where(field =>
            readable.Original.Contains(field.Name) && access.CanExportPartnerField(field.Original)).ToArray();
        var matches = await FindWorkspaceRows(access, body, ExportLimit, ct);
        var asOf = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT transaction_timestamp() AS \"Value\"").SingleAsync(ct);
        var csv = new StringBuilder();
        CsvLine(csv, [.. ExportIdentityColumns, .. Roles, .. ExportStateColumns, .. columns.Select(field => field.Original)]);
        foreach (var (row, head) in matches.Rows)
        {
            ct.ThrowIfCancellationRequested();
            CsvLine(csv, [
                Code(row), Name(row), Base(row), head.Legal,
                .. Roles.Select(role => HasRole(row, role) ? "true" : "false"),
                head.Registration, Lifecycle(row), Hold(row), Revision(head.Revision), Instant(row.CreateDate),
                .. columns.Select(field => Convert.ToString(NormalizeFixedColumn(field.Value(row)), CultureInfo.InvariantCulture))
            ]);
        }
        var prefix = CsvEncoding.GetPreamble();
        var bytes = CsvEncoding.GetBytes(csv.ToString());
        return new([.. prefix, .. bytes], "B03/1/" + access.ReadScopeVersion, asOf);
    }

    private static void CsvLine(StringBuilder csv, IEnumerable<string?> values) =>
        csv.AppendJoin(',', values.Select(CsvCell)).Append("\r\n");

    private static string CsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t') value = "'" + value;
        return value.AsSpan().ContainsAny(",\"\r\n")
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
