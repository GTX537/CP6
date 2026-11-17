using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentitySourceCommandV1(
    [property: JsonRequired] string OperationRef,
    [property: JsonRequired] string ExpectedRevision,
    [property: JsonRequired] string ExpectedSourceSha256,
    [property: JsonRequired] IdentitySourceBindingV1 Next,
    [property: JsonRequired] string OwnerApprovalRef,
    [property: JsonRequired] DateTimeOffset EffectiveAfterUtc,
    [property: JsonRequired] DateTimeOffset ApprovalValidUntilUtc,
    [property: JsonRequired] int CompatibilityWindowSeconds,
    [property: JsonRequired] string SessionDisposition,
    [property: JsonRequired] string RegionDataDisposition);

public sealed record IdentitySourceStateV1(string Revision, string SourceSha256, IdentitySourceBindingV1 Binding);
public sealed record IdentitySourceItemV1(IdentitySourceBindingV1 Source, IdentityVersion Item, string PayloadBase64);
public sealed record IdentitySourcePublicationV1(string Contract, string OperationRef, string Revision,
    string PreviousRevision, string PreviousSourceSha256, string CurrentSourceSha256,
    IdentitySourceBindingV1 Previous, IdentitySourceBindingV1 Current, string OwnerApprovalRef,
    Guid ApprovedBy, Guid ApprovedByTenant, DateTimeOffset RecordedAtUtc,
    DateTimeOffset EffectiveAfterUtc, DateTimeOffset ApprovalValidUntilUtc, int CompatibilityWindowSeconds,
    string SessionDisposition, string RegionDataDisposition, string Boundary, long ExpectedCount,
    IdentitySourceItemV1[] Items, IdentitySourceOutboxV1[] Outbox, Guid[] RetiredBrowserFamilies);
public sealed record IdentitySourceOutboxV1(string Queue, string MessageId, string AggregateId, long AggregateVersion,
    string PayloadSha256, string PayloadBase64);

/// <summary>批准映射和封存完整原目录在同一 Main PostgreSQL 事务中提交；不迁移租户业务数据。</summary>
public sealed class IdentitySourceAdministrationV1(CP6Context db, CrmIdentityRuntime runtime, IHttpContextAccessor http,
    IReadOnlyDictionary<Guid, Guid[]>? requiredServicePrincipals = null)
{
    private sealed class Stored
    {
        public long Revision { get; set; }
        public byte[] Bytes { get; set; } = [];
        public byte[] Hash { get; set; } = [];
    }
    private sealed class Original
    {
        public byte[] Operation { get; set; } = [];
        public byte[] Input { get; set; } = [];
        public Guid Actor { get; set; }
        public Guid ActorTenant { get; set; }
        public byte[] Result { get; set; } = [];
    }
    private sealed class OutboxRow
    {
        public string Queue { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string AggregateId { get; set; } = "";
        public long AggregateVersion { get; set; }
        public byte[] Payload { get; set; } = [];
    }
    private static byte[] Bytes<T>(T value) => IdentitySourceAuthorityV1.Bytes(value);
    private static TenantManagementExceptionV1 Error(string code, int status = 409) => new(code, status);

    public Task<byte[]> ReadAsync(Guid tenant, TenantManagementSessionV1 session, CancellationToken ct = default)
        => Transaction(false, session, async (_, token) =>
        {
            var current = await Registered(tenant, token);
            if (current is null)
            {
                if (!runtime.Options.SourceBindings.TryGetValue(tenant, out var candidate)) throw Error("C02_SOURCE_NOT_CONFIGURED", 404);
                return Bytes(new IdentitySourceStateV1("unregistered", IdentityEventContracts.Hash(Bytes(candidate)), candidate));
            }
            return Bytes(new IdentitySourceStateV1(current.Revision.ToString(CultureInfo.InvariantCulture),
                Convert.ToHexString(current.Hash).ToLowerInvariant(), JsonSerializer.Deserialize<IdentitySourceBindingV1>(current.Bytes, IdentityEventContracts.Json)!));
        }, ct);

    public Task<byte[]> OriginalAsync(Guid tenant, string operation, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        TenantAdminService.ManagementOperation(operation);
        return Transaction(false, session, async (actor, token) =>
            OriginalBytes(await FindOriginal(tenant, operation, token) ?? throw Error("C02_SOURCE_COMMAND_NOT_FOUND", 404), actor, operation, null), ct);
    }

    public Task<byte[]> PublishAsync(Guid tenant, IdentitySourceCommandV1 command, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        TenantAdminService.ManagementOperation(command.OperationRef);
        var input = Bytes(command);
        var publishing = false;
        return Transaction(true, session, async (actor, token) =>
        {
            var original = await FindOriginal(tenant, command.OperationRef, token);
            if (original is not null) return OriginalBytes(original, actor, command.OperationRef, input);
            Validate(tenant, command);
            var current = await Registered(tenant, token);
            var enrolling = current is null;
            if (enrolling)
            {
                // 历史 Bootstrap 不自动从启动配置取得新身份。只有本次有权限的明确批准可登记原来源。
                var candidate = Bytes(IdentitySourceAuthorityV1.Binding(runtime, tenant));
                if (command.ExpectedRevision != "unregistered" || command.ExpectedSourceSha256 != IdentityEventContracts.Hash(candidate))
                    throw Error("C02_SOURCE_VERSION_CHANGED", 412);
                current = new Stored { Revision = 0, Bytes = candidate, Hash = SHA256.HashData(candidate) };
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_identity_source_v1.registrations(tenant_id,revision,binding_bytes,binding_hash)
                    VALUES({tenant},0,{candidate},sha256({candidate}))
                    """, token);
            }
            if ((!enrolling && current!.Revision.ToString(CultureInfo.InvariantCulture) != command.ExpectedRevision) ||
                Convert.ToHexString(current!.Hash).ToLowerInvariant() != command.ExpectedSourceSha256)
                throw Error("C02_SOURCE_VERSION_CHANGED", 412);
            if (current.Revision == long.MaxValue) throw Error("C02_SOURCE_REVISION_EXHAUSTED", 422);
            var previous = JsonSerializer.Deserialize<IdentitySourceBindingV1>(current.Bytes, IdentityEventContracts.Json)!;
            if (previous.Slug.Length == 0 || !runtime.Options.SourceBindings.ContainsKey(tenant))
                throw Error("C02_SOURCE_FULL_CONFIGURATION_REQUIRED");
            await IdentitySourceAuthorityV1.RequireCurrentAsync(db, runtime, tenant, token);
            var nextBytes = Bytes(command.Next);
            if (nextBytes.AsSpan().SequenceEqual(current.Bytes)) throw Error("C02_SOURCE_UNCHANGED");
            var otherBindings = await db.Database.SqlQuery<byte[]>($"""
                SELECT binding_bytes AS "Value" FROM cp6_identity_source_v1.registrations WHERE tenant_id<>{tenant}
                """).ToArrayAsync(token);
            if (otherBindings.Select(b => JsonSerializer.Deserialize<IdentitySourceBindingV1>(b, IdentityEventContracts.Json)!)
                .Any(b => b.Issuer == command.Next.Issuer && (b.Slug == command.Next.Slug || b.ServiceClientIds.Intersect(command.Next.ServiceClientIds, StringComparer.Ordinal).Any())))
                throw Error("C02_SOURCE_MAPPING_CONFLICT");
            var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            if (now < command.EffectiveAfterUtc || now >= command.ApprovalValidUntilUtc) throw Error("C02_SOURCE_APPROVAL_NOT_CURRENT");
            if (!await db.CrmIdentityBootstrapStates.AnyAsync(x => x.TenantId == tenant && x.CompletedAtUtc != null &&
                    x.ContractBundleSha256 == runtime.ContractBundleSha256, token)) throw Error("C02_BOOTSTRAP_NOT_READY");
            var priorEvents = (await ReadOutbox(tenant, token)).Select(x => x.Queue + ":" + x.MessageId).ToHashSet(StringComparer.Ordinal);
            var writer = new IdentitySnapshotWriter(db, runtime);
            await writer.RevokeTenantTokensAsync(tenant, token);
            await writer.AdvanceSourceBoundaryAsync(tenant, token);
            await db.SaveChangesAsync(token);
            var known = await KnownBindingsAsync(db, tenant, previous, token);
            var snapshots = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant)
                .OrderBy(x => x.AggregateId).ToArrayAsync(token);
            var items = new List<IdentitySourceItemV1>();
            foreach (var row in snapshots)
            {
                var item = new IdentityVersion(row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted);
                var bytes = new UTF8Encoding(false, true).GetBytes(row.PayloadJson);
                using var json = JsonDocument.Parse(bytes);
                var source = IdentityEventContracts.IsTokenRevoked(row.EventType)
                    ? known.LastOrDefault(x => x.Issuer == json.RootElement.GetProperty("issuer").GetString()) : previous;
                if (source is null || IdentityEventContracts.Hash(bytes) != row.PayloadSha256 ||
                    !runtime.Validator.ForIssuer(source.Issuer).IsValidSnapshotData(tenant, item, source.Region, bytes))
                    throw Error("C02_SOURCE_ORIGINAL_FACT_INVALID");
                items.Add(new(source, item, Convert.ToBase64String(bytes)));
            }
            await RequireCompleteDirectory(tenant, items, token);
            var boundary = await db.CrmIdentityTenantGenerations.Where(x => x.TenantId == tenant).Select(x => x.Generation).SingleAsync(token);
            var events = (await ReadOutbox(tenant, token)).Where(x => !priorEvents.Contains(x.Queue + ":" + x.MessageId))
                .Select(x => new IdentitySourceOutboxV1(x.Queue, x.MessageId, x.AggregateId, x.AggregateVersion,
                    IdentityEventContracts.Hash(x.Payload), Convert.ToBase64String(x.Payload))).ToArray();
            var families = await db.Sys_BrowserSessions.IgnoreQueryFilters().Where(x => x.TenantId == tenant)
                .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_identity_source_v1.retired_browser_sessions(tenant_id,family_id,revision)
                SELECT "TenantId","Id",{current.Revision + 1} FROM public."Sys_BrowserSessions" WHERE "TenantId"={tenant}
                ON CONFLICT(tenant_id,family_id) DO NOTHING
                """, token);
            var publication = new IdentitySourcePublicationV1("identity-source-publication-v1", command.OperationRef,
                (current.Revision + 1).ToString(CultureInfo.InvariantCulture), command.ExpectedRevision, command.ExpectedSourceSha256,
                IdentityEventContracts.Hash(nextBytes), previous, command.Next, command.OwnerApprovalRef, actor.Id, actor.TenantId,
                now, command.EffectiveAfterUtc, command.ApprovalValidUntilUtc, command.CompatibilityWindowSeconds,
                command.SessionDisposition, command.RegionDataDisposition, boundary.ToString(CultureInfo.InvariantCulture),
                items.Count, items.ToArray(), events, families);
            var result = Bytes(publication);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_identity_source_v1.registrations SET revision=revision+1,binding_bytes={nextBytes},binding_hash=sha256({nextBytes})
                WHERE tenant_id={tenant};
                INSERT INTO cp6_identity_source_v1.publications(tenant_id,revision,operation_hash,operation_bytes,input_bytes,actor_id,actor_tenant,result_bytes)
                VALUES({tenant},{current.Revision + 1},{SHA256.HashData(Bytes(command.OperationRef))},{Bytes(command.OperationRef)},
                    {input},{actor.Id},{actor.TenantId},{result});
                """, token);
            publishing = true;
            return result;
        }, ct, async token =>
        {
            if (!publishing) return; // 原件恢复不重新实施已经完成的批准。
            var decisionTime = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            if (decisionTime < command.EffectiveAfterUtc || decisionTime >= command.ApprovalValidUntilUtc)
                throw Error("C02_SOURCE_APPROVAL_NOT_CURRENT");
        });
    }

    private async Task RequireCompleteDirectory(Guid tenant, List<IdentitySourceItemV1> items, CancellationToken ct)
    {
        var expected = new List<string> { "tenant:" + tenant.ToString("D") };
        expected.AddRange((await db.Sys_Users.IgnoreQueryFilters().Where(x => x.TenantId == tenant).Select(x => x.Id).ToArrayAsync(ct)).Select(x => "user:" + x.ToString("D")));
        expected.AddRange((await db.Sys_Depts.IgnoreQueryFilters().Where(x => x.TenantId == tenant).Select(x => x.Id).ToArrayAsync(ct)).Select(x => "department:" + x.ToString("D")));
        expected.AddRange((await db.Sys_Roles.IgnoreQueryFilters().Where(x => x.TenantId == tenant).Select(x => x.RoleId).ToArrayAsync(ct)).Select(x => "permission:" + IdentityEventContracts.RoleSubject(x)));
        if (requiredServicePrincipals?.TryGetValue(tenant, out var services) == true)
            expected.AddRange(services.Select(x => "service-principal:" + x.ToString("D")));
        var present = items.Select(x => x.Item.AggregateId).ToHashSet(StringComparer.Ordinal);
        if (!expected.All(present.Contains)) throw Error("C02_SOURCE_DIRECTORY_INCOMPLETE");
        var tokens = await db.CrmServiceTokenRecords.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync(ct);
        if (tokens.Any(x => x.RevokedAtUtc is null || !present.Contains(IdentityEventContracts.TokenAggregate(x.Issuer, x.Jti))))
            throw Error("C02_SOURCE_OLD_DISPOSITION_INCOMPLETE");
        var previousItems = await db.Database.SqlQuery<string>($"""
            SELECT DISTINCT item->'item'->>'aggregateId' AS "Value"
            FROM cp6_identity_source_v1.publications p,
              LATERAL jsonb_array_elements(convert_from(p.result_bytes,'UTF8')::jsonb->'items') item
            WHERE p.tenant_id={tenant}
            """).ToArrayAsync(ct);
        if (!previousItems.All(present.Contains)) throw Error("C02_SOURCE_ORIGINAL_FACT_INCOMPLETE");
    }

    private Task<Stored?> Registered(Guid tenant, CancellationToken ct) => db.Database.SqlQuery<Stored>($"""
        SELECT revision AS "Revision",binding_bytes AS "Bytes",binding_hash AS "Hash"
        FROM cp6_identity_source_v1.registrations WHERE tenant_id={tenant} FOR UPDATE
        """).SingleOrDefaultAsync(ct);
    private Task<Original?> FindOriginal(Guid tenant, string operation, CancellationToken ct) => db.Database.SqlQuery<Original>($"""
        SELECT operation_bytes AS "Operation",input_bytes AS "Input",actor_id AS "Actor",actor_tenant AS "ActorTenant",result_bytes AS "Result"
        FROM cp6_identity_source_v1.publications WHERE tenant_id={tenant} AND operation_hash={SHA256.HashData(Bytes(operation))}
        """).SingleOrDefaultAsync(ct);
    private static byte[] OriginalBytes(Original original, PlatformAdminAuthorization.Actor actor, string operation, byte[]? input)
    {
        if (original.Actor != actor.Id || original.ActorTenant != actor.TenantId) throw Error("C02_SOURCE_ORIGINAL_FORBIDDEN", 403);
        if (!original.Operation.AsSpan().SequenceEqual(Bytes(operation)) || input is not null && !original.Input.AsSpan().SequenceEqual(input))
            throw Error("C02_SOURCE_OPERATION_CONFLICT");
        return original.Result;
    }
    private Task<OutboxRow[]> ReadOutbox(Guid tenant, CancellationToken ct) => db.Database.SqlQuery<OutboxRow>($"""
        SELECT 'ordinary' AS "Queue","MessageId","AggregateId","AggregateVersion"::bigint AS "AggregateVersion","Payload"
        FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={tenant}
        UNION ALL
        SELECT 'priority' AS "Queue","MessageId","AggregateId","AggregateVersion"::bigint AS "AggregateVersion","Payload"
        FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={tenant}
        """).ToArrayAsync(ct);

    internal static async Task<IdentitySourceBindingV1[]> KnownBindingsAsync(CP6Context db, Guid tenant, IdentitySourceBindingV1 current, CancellationToken ct)
    {
        var history = await db.Database.SqlQuery<byte[]>($"""
            SELECT result_bytes AS "Value" FROM cp6_identity_source_v1.publications WHERE tenant_id={tenant} ORDER BY revision
            """).ToArrayAsync(ct);
        return history.SelectMany(bytes => { var p = JsonSerializer.Deserialize<IdentitySourcePublicationV1>(bytes, IdentityEventContracts.Json)!; return new[] { p.Previous, p.Current }; })
            .Append(current).DistinctBy(x => IdentityEventContracts.Hash(Bytes(x))).ToArray();
    }
    private void Validate(Guid tenant, IdentitySourceCommandV1 c)
    {
        var n = c.Next;
        if (n is null || tenant == Guid.Empty || n.TenantId != tenant || !runtime.Options.Tenants.ContainsKey(tenant) ||
            !Regex.IsMatch(n.Slug ?? "", "\\A[a-z0-9][a-z0-9-]{0,62}\\z", RegexOptions.CultureInvariant) ||
            !Uri.TryCreate(n.Issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || n.Issuer.EndsWith('/') ||
            n.ContractBundleSha256 != runtime.ContractBundleSha256 || string.IsNullOrWhiteSpace(c.OwnerApprovalRef) ||
            c.OwnerApprovalRef.Length > 512 || c.CompatibilityWindowSeconds != 0 || c.SessionDisposition != "REAUTHENTICATE_ALL" ||
            c.RegionDataDisposition != "KEEP_EXISTING_STORAGE" || c.EffectiveAfterUtc >= c.ApprovalValidUntilUtc ||
            n.ServiceClientIds is null || n.ReaderClientIds is null || n.ReaderClientIds.Length == 0 ||
            n.ServiceClientIds.Any(x => !Regex.IsMatch(x ?? "", "\\A[A-Za-z0-9._-]{1,80}\\z", RegexOptions.CultureInvariant) || x == "CP6.Web") ||
            !n.ServiceClientIds.SequenceEqual(n.ServiceClientIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) ||
            !n.ReaderClientIds.SequenceEqual(n.ReaderClientIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) ||
            n.ReaderClientIds.Except(n.ServiceClientIds, StringComparer.Ordinal).Any()) throw Error("C02_SOURCE_COMMAND_INVALID", 400);
        try { CP6.Platform.Messaging.Cp6CloudEventAttributes.Region.Validate(n.Region); }
        catch (ArgumentException) { throw Error("C02_SOURCE_COMMAND_INVALID", 400); }
    }

    private async Task<byte[]> Transaction(bool write, TenantManagementSessionV1 session,
        Func<PlatformAdminAuthorization.Actor, CancellationToken, Task<byte[]>> body, CancellationToken ct,
        Func<CancellationToken, Task>? finalCheck = null)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges() || System.Transactions.Transaction.Current != null)
            throw Error("C02_SOURCE_INDEPENDENT_CONTEXT_REQUIRED", 503);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var committing = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var subject = await session.CurrentActor(ct);
            var actor = await PlatformAdminAuthorization.RequireAsync(db, http.HttpContext?.User, ct, lockActor: true);
            if (subject != actor.Id) throw Error("REAUTH_REQUIRED", 401);
            var authorizedUntil = await db.Sys_Tenants.Where(x => x.Id == actor.TenantId).Select(x => x.ExpireDate).SingleAsync(ct);
            var result = await body(actor, ct);
            await session.Complete(ct);
            if (authorizedUntil <= DateTime.Now) throw Error("REAUTH_REQUIRED", 401);
            if (finalCheck is not null) await finalCheck(ct);
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); return result;
        }
        catch (Exception error)
        {
            if (write && committing) throw new TenantManagementExceptionV1("C02_SOURCE_OUTCOME_UNKNOWN", 503, error);
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new TenantManagementExceptionV1("C02_SOURCE_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            throw;
        }
    }
}
