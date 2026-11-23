using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record WorkflowNotificationPreferenceViewV1(Guid Version, string SupportVersion, string ReadState,
    IReadOnlyList<NotifyMatrixRow> Support, IReadOnlyDictionary<string, NotificationPreferenceCellV1> EffectivePrefs);
public sealed record NotificationPreferenceCellV1(bool InApp, bool Email);
public sealed record WorkflowNotificationPreferenceReceiptV1(string CommandId, string Outcome, DateTimeOffset RecordedAt,
    WorkflowNotificationPreferenceViewV1 Result, string? ClientCorrelationId = null)
{
    public string ResourceVersion => Result.Version.ToString("D");
}

public sealed class WorkflowNotificationPreferencesV1(CP6Context db, ScopeAuthorizationFenceV1 scope, IHttpContextAccessor http)
{
    internal static readonly Guid DefaultVersion = new("00000000-0000-0000-0000-000000000001");

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record PatchInput([property: JsonRequired] Guid ExpectedPreferenceVersion,
        [property: JsonRequired] string PatchMode, [property: JsonRequired] JsonElement Patch);

    internal sealed record StoredPreference(Guid Version, string PrefsJson);
    private sealed record StoredCommand(string Digest, string Result);

    internal static async Task<StoredPreference> SnapshotAsync(CP6Context database, Guid user, CancellationToken ct)
        => await database.Database.SqlQuery<StoredPreference>($"""
            SELECT "NotificationPreferenceVersion" AS "Version", "PrefsJson" AS "PrefsJson"
            FROM public."Wf_InboxPref" WHERE "TenantId"={database.CurrentTenantId} AND "UserId"={user}
            """).SingleOrDefaultAsync(ct) ?? new(DefaultVersion, "{}");

    internal static Task LockAsync(CP6Context database, Guid user, CancellationToken ct)
    {
        var identity = $"oa-notification-prefs:{database.CurrentTenantId:D}:{user:D}";
        return database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))", ct);
    }

    public async Task<WorkflowNotificationPreferenceViewV1> ReadAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct);
        var user = await WorkflowNotificationAuthorityV1.ActualUserAsync(db, http, ct);
        var snapshot = await SnapshotAsync(db, user, ct);
        await scope.ValidateAsync(lease, ct);
        return View(snapshot);
    }

    public async Task<WorkflowNotificationPreferenceReceiptV1?> OriginalAsync(string key, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct);
        var user = await WorkflowNotificationAuthorityV1.ActualUserAsync(db, http, ct);
        await WorkflowNotificationAuthorityV1.RequireActionAsync(db, user, "oa-settings", "edit", ct);
        var stored = await Original(user, key, ct);
        await scope.ValidateAsync(lease, ct);
        return stored is null ? null : Decode(stored);
    }

    public async Task<WorkflowNotificationPreferenceReceiptV1> SaveAsync(byte[] raw, CancellationToken ct)
    {
        var command = PersonalFormOwnerV2.CommandInput<PatchInput>(raw, true, maxBytes: NotifyMatrix.MaxPreferenceBytes);
        if (!Guid.TryParseExact(command.Expected, "D", out var expected) ||
            command.Body.ExpectedPreferenceVersion != expected || command.Body.PatchMode != "TOP_LEVEL_MERGE" ||
            command.Body.Patch.ValueKind != JsonValueKind.Object ||
            !command.Body.Patch.TryGetProperty("notify", out var notify) || command.Body.Patch.EnumerateObject().Count() != 1)
            throw new ScopeAuthorizationExceptionV1("NOTIFICATION_PREFERENCE_PATCH_INVALID", 422);
        NotifyMatrix.ValidateNotify(notify);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct);
        var user = await WorkflowNotificationAuthorityV1.ActualUserAsync(db, http, ct);
        await WorkflowNotificationAuthorityV1.RequireActionAsync(db, user, "oa-settings", "edit", ct);
        await LockAsync(db, user, ct);
        var prior = await Original(user, command.Id, ct);
        if (prior is not null)
        {
            if (prior.Digest != command.Digest)
                throw new ScopeAuthorizationExceptionV1("COMMAND_BODY_CONFLICT", 409);
            await scope.ValidateAsync(lease, ct);
            return Decode(prior);
        }

        var current = await SnapshotAsync(db, user, ct);
        if (current.Version != expected)
            throw new ScopeAuthorizationExceptionV1("NOTIFICATION_PREFERENCE_VERSION_CONFLICT", 412);
        var merged = PrefService.Merge(current.PrefsJson, command.Body.Patch.GetRawText());
        if (System.Text.Encoding.UTF8.GetByteCount(merged) > NotifyMatrix.MaxPreferenceBytes)
            throw new ScopeAuthorizationExceptionV1("NOTIFICATION_PREFERENCE_JSON_INVALID", 422);
        await new PrefService(db).PersistAsync(user, merged, ct);
        var applied = await SnapshotAsync(db, user, ct);
        var receipt = new WorkflowNotificationPreferenceReceiptV1(command.Id, "SUCCEEDED", DateTimeOffset.UtcNow, View(applied), command.Correlation);
        var result = JsonSerializer.Serialize(receipt, PersonalFormOwnerV2.Json);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_notification_v1.preference_commands(tenant_id,environment_id,actor_id,command_id,digest,result_json)
            VALUES({db.CurrentTenantId},{scope.EnvironmentId},{user},{command.Id},{command.Digest},{result})
            """, ct);
        await scope.ValidateAsync(lease, ct);
        await transaction.CommitAsync(ct);
        return receipt;
    }

    private Task<StoredCommand?> Original(Guid user, string key, CancellationToken ct)
        => db.Database.SqlQuery<StoredCommand>($"""
            SELECT digest AS "Digest",result_json AS "Result" FROM cp6_oa_notification_v1.preference_commands
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND actor_id={user} AND command_id={key}
            """).SingleOrDefaultAsync(ct);

    private static WorkflowNotificationPreferenceReceiptV1 Decode(StoredCommand row)
        => JsonSerializer.Deserialize<WorkflowNotificationPreferenceReceiptV1>(row.Result, PersonalFormOwnerV2.Json)!;

    private static WorkflowNotificationPreferenceViewV1 View(StoredPreference preference)
    {
        var rows = NotifyMatrix.Rows();
        var cells = rows.ToDictionary(x => x.TypeKey, x => new NotificationPreferenceCellV1(
            NotifyMatrix.IsEnabled(preference.PrefsJson, x.TypeKey, NotifyMatrix.ChannelInApp),
            NotifyMatrix.IsEnabled(preference.PrefsJson, x.TypeKey, NotifyMatrix.ChannelEmail)));
        var support = WorkflowCommandSupportVersion(rows);
        return new(preference.Version, support, NotifyMatrix.ReadState(preference.PrefsJson), rows, cells);
    }

    private static string WorkflowCommandSupportVersion(IReadOnlyList<NotifyMatrixRow> rows)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)))
            .ToLowerInvariant();
}
