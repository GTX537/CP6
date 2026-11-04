using System.Reflection;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

public sealed record SubjectExportFieldV1(string Name, string Status, string Reason);
public sealed record SubjectExportSourceV1(string Owner, string Source, string Status, string Reason,
    string Selector, int? RecordCount, DateTime? ReadStartedAtUtc, DateTime? ReadCompletedAtUtc,
    string VersionBasis, SubjectExportFieldV1[] Fields);
public sealed record SubjectExportManifestV1(string Version, Guid TenantId, Guid SubjectId, string State,
    bool Complete, string DirectoryCoverage, string Consistency, SubjectExportSourceV1[] Sources);

/// <summary>Trusted owner projections. New/unclassified properties never enter the subject payload by reflection.</summary>
internal static class SubjectExportCatalogV1
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static readonly string[] UserFields = ["Id", "TenantId", "UserName", "NickName", "Email", "Enable",
        "RoleId", "DeptId", "ManagerId", "PasswordChangedAt", "FailedLoginCount", "LastFailedLoginAt",
        "LockedUntil", "LastLoginTime", "LastLoginIp", "MustChangePassword", "TwoFactorEnabled",
        "TwoFactorEnrolledAt", "ExternalSubject", "ExternalProvider", "AllowPasswordFallback", "IsPlatformAdmin",
        "BadgeNo", "CreateDate", "ModifyDate"];
    internal static readonly string[] SecurityFields = ["Id", "TenantId", "UserId", "UserName", "EventType", "ClientIp", "CreatedAt"];
    internal static readonly string[] RoleFields = ["Id", "TenantId", "UserId", "RoleId", "CreateDate", "ModifyDate"];

    internal static Dictionary<string, object?> Project<T>(T row, string[] fields) where T : class
        => fields.ToDictionary(name => name, name => typeof(T).GetProperty(name)!.GetValue(row));

    private static SubjectExportFieldV1[] Fields(Type type, string[] included)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => included.Contains(p.Name, StringComparer.Ordinal)
                ? new SubjectExportFieldV1(p.Name, "INCLUDED", "OWNER_CLASSIFIED_SUBJECT_FIELD")
                : new SubjectExportFieldV1(p.Name, "EXCLUDED_WITH_REASON", SensitiveFieldPolicy.IsSensitive(p.Name)
                    || p.Name is "AuthenticationEpoch" or "CredentialRevision" ? "AUTHENTICATION_MATERIAL"
                    : p.Name == "ContextJson" ? "UNCLASSIFIED_NESTED_CONTENT" : "OWNER_FIELD_NOT_CLASSIFIED_FOR_SUBJECT_EXPORT")).ToArray();

    internal static SubjectExportSourceV1 Included<T>(string owner, int count, DateTime start, DateTime end, string[] fields,
        string selector = "TARGET_TENANT_AND_STABLE_USER_ID")
        => new(owner, typeof(T).Name, "INCLUDED", "ACTUAL_OWNER_READ", selector, count,
            start, end, "UNVERSIONED_LIVE_ROWS_READ_WINDOW", Fields(typeof(T), fields));

    internal static SubjectExportManifestV1 Build(CP6Context db, Guid tenant, Guid subject, params SubjectExportSourceV1[] actual)
    {
        var sources = actual.ToList();
        var adopted = new HashSet<Type> { typeof(Sys_User), typeof(Sys_SecurityLog), typeof(Sys_UserRole) };
        // Metadata enumerates omitted sources, never their rows. Creator/Modifier/actor identity does not establish subject ownership.
        foreach (var entity in db.Model.GetEntityTypes().Where(e => e.GetTableName() != null && !adopted.Contains(e.ClrType))
                     .OrderBy(e => e.ClrType.FullName, StringComparer.Ordinal))
        {
            var secret = entity.ClrType == typeof(Sys_RefreshToken);
            sources.Add(new(entity.ClrType == typeof(Sys_OperLog) ? "MAIN.AUDIT" : secret ? "MAIN.AUTHENTICATION" : "MAIN.UNCLASSIFIED_OWNER",
                entity.ClrType.Name, secret ? "EXCLUDED_WITH_REASON" : "UNKNOWN_SCOPE",
                secret ? "AUTHENTICATION_MATERIAL" : entity.ClrType == typeof(Sys_OperLog) ? "STABLE_SUBJECT_ID_UNAVAILABLE" : "OWNER_SUBJECT_RELATION_NOT_ADOPTED",
                "NOT_QUERIED", null, null, null, "NOT_CAPTURED", []));
        }
        // These actual non-EF/application owners are outside the EF model. No cross-application identity inference or raw table sweep.
        foreach (var (owner, source) in new (string, string)[] {
            ("MAIN.OA", "Native forms, workflows, tasks and original commands"),
            ("MAIN.PLM", "Engineering sources, documents, requirements, sets and iterations"),
            ("MAIN.OPERATIONS", "Native order, warehouse and business operation originals"),
            ("CRM", "CRM business data and command history"),
            ("FILE", "File content, derivatives and external copies"),
            ("BACKUP", "Backups and offline replicas"),
            ("UNREGISTERED_OWNERS", "Sources outside the registered directory") })
            sources.Add(new(owner, source, "UNKNOWN_SCOPE", "SUBJECT_MAPPING_AND_EXPORT_ADAPTER_NOT_ADOPTED",
                "NOT_QUERIED", null, null, null, "NOT_CAPTURED", []));
        return new("cp6.subject-export-manifest.v1", tenant, subject, "PARTIAL", false,
            "CURRENT_EF_MODEL_AND_NAMED_NON_EF_OWNERS_WITH_EXPLICIT_REMAINDER", "SEPARATE_OWNER_READS", sources.ToArray());
    }
}
