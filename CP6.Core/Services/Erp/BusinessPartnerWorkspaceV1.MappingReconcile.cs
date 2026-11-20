using System.Globalization;
using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed class MappingEventRow
    {
        public Guid EventId { get; init; }
        public string Legal { get; init; } = "";
        public Guid Account { get; init; }
        public long Revision { get; init; }
        public byte[] Body { get; init; } = [];
        public string BodyHash { get; init; } = "";
        public DateTimeOffset SourceAsOf { get; init; }
    }

    private sealed record MappingEventBody(Guid EventId, Guid AccountId, string LegalEntityKey, long Revision, string? BpKey, DateTimeOffset SourceAsOf);

    private sealed class MappingDeliveryRow
    {
        public Guid EventId { get; init; }
        public long Revision { get; init; }
        public Guid? BpId { get; init; }
        public string? BpKey { get; init; }
        public string SemanticHash { get; init; } = "";
        public string BodyHash { get; init; } = "";
        public string State { get; init; } = "";
        public int Attempt { get; init; }
        public DateTimeOffset? NextAttempt { get; init; }
        public string? LastError { get; init; }
    }

    private sealed class MappingJob
    {
        public Guid Id { get; init; }
        public Guid Actor { get; init; }
        public string Legal { get; init; } = "";
        public Guid Account { get; init; }
        public long Revision { get; init; }
        public string ExpectedHash { get; init; } = "";
        public long? Generation { get; init; }
        public Guid? EventId { get; init; }
        public string Mode { get; init; } = "";
        public string State { get; init; } = "";
        public Guid? ClaimId { get; init; }
        public DateTimeOffset? LeaseUntil { get; init; }
        public int Attempt { get; init; }
        public Guid? ObservationId { get; init; }
        public bool Applicable { get; init; }
        public string? LastError { get; init; }
        public DateTimeOffset Updated { get; init; }
    }

    private sealed record ProjectionView(string Generation, string Knowledge, string? MappingRevision, string? State,
        string? BusinessPartnerKey, string? SemanticHash, string? SourceAsOf, string? ReceivedAt, string? ObservedAt, MappingSourceReference? ProofRef);
    private sealed record ProjectionOriginal(long Revision, string State, string? BpKey, string Hash, string SourceAsOf,
        string ReceivedAt, bool Conflict, MappingSourceReference Proof);
    private sealed record ProjectionClaim(MappingJob Job, Guid ClaimId, BusinessPartnerMappingAuthorityBindingV1? Binding);
    private sealed record ReconcileReceipt(Guid JobId, Guid AccountId, string LegalEntityKey, string Mode, string State,
        string? ProjectionGeneration, bool IsBusinessChange);

    public Task<HttpResult> ReadMapping(string? legalEntityKey, string? accountId, string? beforeRevision, int? limit,
        bool historyOnly, CancellationToken ct)
    {
        var legal = LegalKey(legalEntityKey);
        var account = ParseId(accountId);
        var before = beforeRevision is null ? (long?)null : QueryRevision(beforeRevision);
        var pageSize = limit ?? 50;
        if (pageSize is < 1 or > 100 || !historyOnly && (beforeRevision is not null || limit is not null)) throw Invalid();
        return owner.ExecuteAsync(Resource, MappingRead, false, async (access, token) =>
        {
            await MappingAccess(access, legal, false, token);
            var slot = await ReadMappingSlot(legal, account, false, token);
            var revision = await MappingRevisionAt(legal, account, slot?.Revision, token);
            var head = await ViewMappingHead(access, legal, account, revision, token);
            var history = await MappingHistoryRows(legal, account, before, pageSize + 1, token);
            var entries = new List<MappingReference>();
            foreach (var item in history.Take(pageSize))
                if (await CanSeeMappingRevision(access, legal, item.BpId, token)) entries.Add(MappingRef(legal, account, item));
            if (historyOnly)
                return new HttpResult(new { items = entries, nextBeforeRevision = history.Length > pageSize ? Revision(history[pageSize - 1].Revision) : null,
                    asOf = Instant(Now) });

            var deliveries = await db.Database.SqlQuery<MappingDeliveryRow>($"""
                SELECT e.event_id AS "EventId",e.revision AS "Revision",r.bp_id AS "BpId",r.business_partner_key AS "BpKey",
                       r.semantic_hash AS "SemanticHash",e.body_hash AS "BodyHash",d.state AS "State",d.attempt AS "Attempt",
                       d.next_attempt_at AS "NextAttempt",d.last_error AS "LastError"
                  FROM main_bp.mapping_events e JOIN main_bp.mapping_deliveries d USING(tenant_id,event_id)
                  JOIN main_bp.mapping_revisions r USING(tenant_id,legal_entity_key,account_id,revision)
                 WHERE e.tenant_id={Tenant} AND e.legal_entity_key={legal} AND e.account_id={account}
                 ORDER BY e.revision DESC,e.observation_sequence DESC LIMIT 50
                """).ToArrayAsync(token);
            var transport = new List<object>();
            foreach (var row in deliveries)
                if (await CanSeeMappingRevision(access, legal, row.BpId, token))
                    transport.Add(new
                    {
                        eventId = row.EventId, mapping = new MappingReference(account, legal, Revision(row.Revision), row.BpId is null ? "UNLINKED" : "LINKED", row.BpKey, row.SemanticHash),
                        bodyHash = row.BodyHash, state = row.State, attempt = row.Attempt,
                        nextAttemptAt = row.NextAttempt is { } at ? Instant(at) : null, row.LastError
                    });
            var authority = await MappingAuthority(legal, ProjectionPurpose, token);
            var current = authority is null ? null : await GetProjection(slot?.ProjectionId, token, MappingAuthorityHash(authority));
            if (current is not null && current.Generation != OptionalRevision(slot?.Generation)) current = null;
            if (current is not null) current = await VisibleProjection(access, legal, account, current, token);
            return new HttpResult(new { head, history = entries, deliveries = transport,
                projection = current ?? EmptyProjection(slot?.Generation ?? 1, slot?.OwnerConflict == true ? "CONFLICT" : authority is null ? "NOT_CONFIGURED" : "UNKNOWN"),
                asOf = Instant(Now) });
        }, ct);
    }

    private Task<MappingRevisionRow[]> MappingHistoryRows(string legal, Guid account, long? before, int limit, CancellationToken ct)
        => db.Database.SqlQuery<MappingRevisionRow>($"""
            SELECT revision AS "Revision",bp_id AS "BpId",business_partner_key AS "BpKey",semantic_hash AS "SemanticHash",
                   identity_observation_id AS "IdentityId",recorded_at AS "Recorded"
              FROM main_bp.mapping_revisions
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
               AND ({before}::bigint IS NULL OR revision < {before})
             ORDER BY revision DESC LIMIT {limit}
            """).ToArrayAsync(ct);

    private async Task<bool> CanSeeMappingRevision(OperationsAccessV1 access, string legal, Guid? bp, CancellationToken ct)
    {
        if (bp is null) return true;
        try { return (await VisibleAsync(access, bp.Value, false, ct)).Head?.Legal == legal; }
        catch (OrderPriceCorrectionException ex) when (ex.Status == 404) { return false; }
    }

    private static long QueryRevision(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) &&
        version > 0 && Revision(version) == value ? version : throw Invalid();

    public Task<HttpResult> ReconcileMapping(JsonElement input, string? ifMatch, string? commandKey, CancellationToken ct)
    {
        if (ifMatch is not null) throw Invalid("PRECONDITION_SYNTAX");
        var fields = Exact(input, "legalEntityKey", "accountId", "expectedMappingRevision", "mode", "eventId", "reason");
        var legal = LegalKey(Token(fields["legalEntityKey"], 64));
        var account = Uuid(fields["accountId"]);
        var revision = Version(fields["expectedMappingRevision"]);
        var mode = Token(fields["mode"], 40);
        var eventId = OptionalId(fields["eventId"]);
        var reason = Text(fields["reason"], 200);
        if (mode is not ("QUERY_PROJECTION" or "RETRY_ORIGINAL_EVENT") || (mode == "QUERY_PROJECTION") != (eventId is null))
            throw Invalid();

        return MappingCommand("MAPPING_RECONCILE", ParseId(commandKey), input, null, (_, _) => Task.FromResult<string?>(legal),
            async (access, command, fingerprint, token) =>
            {
                await access.ActionAsync(Resource, MappingReconcile, token);
                await MappingAccess(access, legal, true, token);
                var slot = await ReadMappingSlot(legal, account, true, token);
                if (slot?.Revision != revision) throw Stale();
                var current = await MappingRevisionAt(legal, account, revision, token) ?? throw Stale();
                var head = await ViewMappingHead(access, legal, account, current, token);
                long? generation = null;
                var now = PgTime(Now);
                if (eventId is { } requested)
                {
                    RequireMappingEvent(await FindMappingEvent(legal, account, requested, null, false, token));
                    if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE main_bp.mapping_deliveries
                           SET state='READY',attempt=0,claim_id=NULL,lease_until=NULL,next_attempt_at={now},last_error=NULL
                         WHERE tenant_id={Tenant} AND event_id={requested} AND (state<>'LEASED' OR lease_until<={now})
                        """, token) != 1) throw Rejected("MAPPING_DELIVERY_IN_FLIGHT", 409);
                }
                else
                {
                    generation = NextMappingNumber(slot.Generation);
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE main_bp.mapping_slots SET generation={generation},current_projection_id=NULL
                         WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
                        """, token);
                }
                var job = Guid.NewGuid();
                var phase = generation is null ? "WAITING" : "QUEUED";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO main_bp.mapping_jobs(tenant_id,legal_entity_key,account_id,job_id,command_id,actor_id,reason,mode,
                        expected_revision,expected_hash,generation,event_id,state,updated_at)
                    VALUES({Tenant},{legal},{account},{job},{command},{access.ActorId},{reason},{mode},{revision},{current.SemanticHash},
                        {generation},{eventId},{phase},{now})
                    """, token);
                var reconcile = new ReconcileReceipt(job, account, legal, mode, "QUEUED", OptionalRevision(generation), false);
                return new HttpResult(await RecordMappingCommand(access, command, fingerprint, "MAPPING_RECONCILE", legal, account,
                    head.Bp, null, reconcile, "NO_CHANGE", now, token));
            }, ct);
    }

    public Task<HttpResult> ReadMappingJob(Guid id, CancellationToken ct)
        => owner.ExecuteAsync(Resource, MappingRead, false, async (access, token) =>
        {
            var job = await GetMappingJob(id, false, token) ?? throw Missing();
            if (job.Actor != access.ActorId) await access.ActionAsync(Resource, MappingReconcile, token);
            await MappingAccess(access, job.Legal, false, token);
            var slot = await ReadMappingSlot(job.Legal, job.Account, false, token);
            var current = await MappingRevisionAt(job.Legal, job.Account, slot?.Revision, token);
            await ViewMappingHead(access, job.Legal, job.Account, current, token);
            var original = await MappingRevisionAt(job.Legal, job.Account, job.Revision, token);
            if (original is null || !await CanSeeMappingRevision(access, job.Legal, original.BpId, token)) throw Missing();
            var projection = await GetProjection(job.ObservationId, token);
            if (projection is not null) projection = await VisibleProjection(access, job.Legal, job.Account, projection, token);
            var applicable = job.Applicable && slot?.Generation == job.Generation && slot?.ProjectionId == job.ObservationId;
            return new HttpResult(new { jobId = id, accountId = job.Account, legalEntityKey = job.Legal,
                mode = job.Mode, state = job.State, expectedMainRevision = Revision(job.Revision), projectionGeneration = OptionalRevision(job.Generation),
                observation = projection, currentApplicable = applicable, lastError = job.LastError, updatedAt = Instant(job.Updated) });
        }, ct);

    private async Task<MappingJob?> GetMappingJob(Guid id, bool write, CancellationToken ct)
    {
        var sql = write
            ? db.Database.SqlQuery<MappingJob>($"""
                SELECT job_id AS "Id",actor_id AS "Actor",legal_entity_key AS "Legal",account_id AS "Account",mode AS "Mode",state AS "State",
                       expected_revision AS "Revision",expected_hash AS "ExpectedHash",generation AS "Generation",event_id AS "EventId",
                       claim_id AS "ClaimId",lease_until AS "LeaseUntil",attempt AS "Attempt",observation_id AS "ObservationId",current_applicable AS "Applicable",
                       last_error AS "LastError",updated_at AS "Updated"
                FROM main_bp.mapping_jobs WHERE tenant_id={Tenant} AND job_id={id} FOR UPDATE
                """)
            : db.Database.SqlQuery<MappingJob>($"""
                SELECT job_id AS "Id",actor_id AS "Actor",legal_entity_key AS "Legal",account_id AS "Account",mode AS "Mode",state AS "State",
                       expected_revision AS "Revision",expected_hash AS "ExpectedHash",generation AS "Generation",event_id AS "EventId",
                       claim_id AS "ClaimId",lease_until AS "LeaseUntil",attempt AS "Attempt",observation_id AS "ObservationId",current_applicable AS "Applicable",
                       last_error AS "LastError",updated_at AS "Updated"
                FROM main_bp.mapping_jobs WHERE tenant_id={Tenant} AND job_id={id}
                """);
        return (await sql.ToListAsync(ct)).SingleOrDefault();
    }

    private async Task<MappingEventRow?> FindMappingEvent(string legal, Guid account, Guid? eventId, long? revision, bool latest, CancellationToken ct)
        => (await db.Database.SqlQuery<MappingEventRow>($"""
            SELECT event_id AS "EventId",legal_entity_key AS "Legal",account_id AS "Account",revision AS "Revision",
                   body AS "Body",body_hash AS "BodyHash",source_as_of AS "SourceAsOf" FROM main_bp.mapping_events
            WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
              AND ({eventId}::uuid IS NULL OR event_id={eventId}) AND ({revision}::bigint IS NULL OR revision={revision})
              AND ({latest} OR {eventId}::uuid IS NOT NULL OR observation_sequence=0)
            ORDER BY revision DESC,observation_sequence DESC LIMIT 1
            """).ToListAsync(ct)).SingleOrDefault();

    private static MappingEventBody ParseMappingEvent(byte[] original)
    {
        var f = Exact(ParseOriginal(original), "eventId", "accountId", "legalEntityKey", "mappingRevision", "state", "businessPartnerKey", "sourceAsOf");
        var state = Token(f["state"], 8);
        var key = f["businessPartnerKey"].ValueKind == JsonValueKind.Null ? null : Token(f["businessPartnerKey"], 20);
        if (state is not ("LINKED" or "UNLINKED") || (state == "LINKED") != (key is not null)) throw Invalid();
        return new(Uuid(f["eventId"]), Uuid(f["accountId"]), LegalKey(Token(f["legalEntityKey"], 64)),
            Version(f["mappingRevision"]), key, MappingUtc(f["sourceAsOf"]));
    }

    private static MappingEventBody RequireMappingEvent(MappingEventRow? row)
    {
        if (row is null) throw Missing();
        if (RawDigest(row.Body) != row.BodyHash) throw Rejected("MAPPING_ORIGINAL_UNPROVEN", 503);
        var payload = ParseMappingEvent(row.Body);
        if (payload.EventId != row.EventId || payload.AccountId != row.Account || payload.LegalEntityKey != row.Legal ||
            payload.Revision != row.Revision || payload.SourceAsOf != row.SourceAsOf) throw Rejected("MAPPING_ORIGINAL_UNPROVEN", 503);
        return payload;
    }

    private async Task<ProjectionView?> GetProjection(Guid? id, CancellationToken ct, string? authorityHash = null)
    {
        if (id is null) return null;
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT read_result AS "Value" FROM main_bp.mapping_projection_reads WHERE tenant_id={Tenant} AND observation_id={id}
                AND ({authorityHash}::text IS NULL OR authority_hash={authorityHash})
            """).SingleOrDefaultAsync(ct);
        return bytes is null ? null : JsonSerializer.Deserialize<ProjectionView>(bytes, Json);
    }

    private static ProjectionView EmptyProjection(long generation, string knowledge, DateTimeOffset? observedAt = null)
        => new(Revision(generation), knowledge, null, null, null, null, null, null,
            observedAt is null ? null : Instant(observedAt.Value), null);

    private async Task<ProjectionView> VisibleProjection(OperationsAccessV1 access, string legal, Guid account, ProjectionView projection,
        CancellationToken ct)
    {
        if (projection.MappingRevision is not { } version || projection.BusinessPartnerKey is null) return projection;
        var exact = await MappingRevisionAt(legal, account, QueryRevision(version), ct);
        return exact is not null && exact.BpKey == projection.BusinessPartnerKey && exact.SemanticHash == projection.SemanticHash &&
            await CanSeeMappingRevision(access, legal, exact.BpId, ct)
            ? projection : projection with { BusinessPartnerKey = null, SemanticHash = null, ProofRef = null };
    }

    private static ProjectionOriginal? ParseProjection(byte[] body, string legal, Guid account)
    {
        var raw = ParseOriginal(body);
        if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("availability", out var availability) ||
            Token(availability, 20) != "FOUND") return null;
        var f = Exact(raw, "availability", "accountId", "legalEntityKey", "mappingRevision", "state", "businessPartnerKey",
            "semanticHash", "sourceAsOf", "receivedAt", "conflict", "proofRef");
        if (Uuid(f["accountId"]) != account || Token(f["legalEntityKey"], 64) != legal) return null;
        var number = Version(f["mappingRevision"]);
        var state = Token(f["state"], 8);
        var code = f["businessPartnerKey"].ValueKind == JsonValueKind.Null ? null : Token(f["businessPartnerKey"], 20);
        if (state is not ("LINKED" or "UNLINKED") || (state == "LINKED") != (code is not null)) return null;
        var hash = MappingSemanticHash(account, legal, number, code);
        if (hash != Digest(f["semanticHash"])) return null;
        return new(number, state, code, hash, Instant(MappingUtc(f["sourceAsOf"])), Instant(MappingUtc(f["receivedAt"])),
            Flag(f["conflict"]), SourceReference(f["proofRef"]));
    }

    public async Task RunMappingProjectionOnce(CancellationToken ct)
    {
        if (mappingAuthority is null) return;
        var claim = await ClaimMappingProjection(ct);
        if (claim is null) return;
        var job = claim.Job;
        var request = Canonical(new MappingCoordinates(job.Legal, job.Account));
        BusinessPartnerMappingOriginalV1? original = null;
        ProjectionOriginal? read = null;
        if (claim.Binding is { } binding)
        {
            original = await mappingAuthority.ReadAsync(binding, "PROJECTION", request, ct);
            if (original is not null)
                try { read = ParseProjection(original.Body, job.Legal, job.Account); }
                catch (Exception ex) when (ex is JsonException or OrderPriceCorrectionException) { }
        }
        await SaveProjectionResult(claim, original, read, ct);
    }

    private async Task<ProjectionClaim?> ClaimMappingProjection(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = PgTime(Now);
        var id = (await db.Database.SqlQuery<Guid>($"""
            SELECT job_id AS "Value" FROM main_bp.mapping_jobs
             WHERE tenant_id={Tenant} AND mode='QUERY_PROJECTION'
               AND (state='QUEUED' OR (state='RUNNING' AND lease_until<={now}) OR (state='WAITING' AND next_attempt_at<={now}))
             ORDER BY updated_at,job_id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct)).SingleOrDefault();
        if (id == Guid.Empty) return null;
        var job = await GetMappingJob(id, true, ct) ?? throw Stale();
        var claim = Guid.NewGuid();
        var bound = await MappingAuthority(job.Legal, ProjectionPurpose, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.mapping_jobs SET state='RUNNING',claim_id={claim},lease_until={now.AddSeconds(30)},attempt=attempt+1,
                next_attempt_at=NULL,updated_at={now}
             WHERE tenant_id={Tenant} AND job_id={id}
            """, ct);
        await tx.CommitAsync(ct);
        return new(job, claim, bound);
    }

    private async Task SaveProjectionResult(ProjectionClaim claim, BusinessPartnerMappingOriginalV1? original,
        ProjectionOriginal? read, CancellationToken ct)
    {
        var job = claim.Job;
        var generation = job.Generation ?? throw Stale();
        var now = PgTime(Now);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BpUseScopeGate.LockAsync(db, job.Legal, true, ct);
        var slot = await ReadMappingSlot(job.Legal, job.Account, true, ct) ?? throw Stale();
        var current = await MappingRevisionAt(job.Legal, job.Account, slot.Revision, ct);
        var live = await GetMappingJob(job.Id, true, ct) ?? throw Stale();
        var authority = await MappingAuthority(job.Legal, ProjectionPurpose, ct);
        var fresh = claim.Binding is not null && authority is not null && MappingAuthorityHash(claim.Binding) == MappingAuthorityHash(authority);
        var owns = live.ClaimId == claim.ClaimId && live.LeaseUntil > now;
        var applicable = owns && slot.Generation == generation && current?.Revision == job.Revision &&
            current.SemanticHash == job.ExpectedHash;
        if (!fresh) read = null;

        var knowledge = read is null ? (claim.Binding is null ? "NOT_CONFIGURED" : "UNKNOWN") :
            read.Revision > job.Revision ? "AHEAD" :
            read.Conflict || read.Revision == job.Revision && read.Hash != job.ExpectedHash ? "CONFLICT" :
            read.Revision < job.Revision ? "BEHIND" : "MATCHES_CURRENT";
        if (applicable && slot.OwnerConflict && knowledge == "MATCHES_CURRENT") knowledge = "CONFLICT";
        var view = read is null ? EmptyProjection(generation, knowledge, now) :
            new ProjectionView(Revision(generation), knowledge, Revision(read.Revision), read.State,
                read.BpKey, read.Hash, read.SourceAsOf, read.ReceivedAt, Instant(now), read.Proof);
        var id = Guid.NewGuid();
        var bytes = Canonical(view);
        var body = original is null ? null : Protect("projection-original", id).Protect(original.Body);
        var trust = original is null ? null : Protect("projection-trust", id).Protect(original.ReceivedTrust);
        var authorityHash = authority is null ? null : MappingAuthorityHash(authority);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_projection_reads(tenant_id,legal_entity_key,account_id,observation_id,job_id,claim_id,generation,
                knowledge,authority_hash,read_result,protected_original,protected_trust,observed_at,current_applicable)
            VALUES({Tenant},{job.Legal},{job.Account},{id},{job.Id},{claim.ClaimId},{generation},{knowledge},{authorityHash},{bytes},{body},{trust},{now},{applicable})
            ON CONFLICT (tenant_id,job_id,claim_id) DO NOTHING
            """, ct);
        if (inserted == 0) return;
        if (applicable)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_slots SET current_projection_id={id},owner_conflict=owner_conflict OR {knowledge is "AHEAD" or "CONFLICT"}
                 WHERE tenant_id={Tenant} AND legal_entity_key={job.Legal} AND account_id={job.Account}
                """, ct);
        if (owns)
        {
            var waiting = applicable && fresh && original is null && live.Attempt < 5;
            var state = read is not null ? "COMPLETE" : waiting ? "WAITING" : "FAILED";
            DateTimeOffset? retryAt = waiting ? now.AddSeconds(30 * live.Attempt) : null;
            var error = !applicable ? "OBSERVATION_SUPERSEDED" : read is null ? "PROJECTION_UNPROVEN" : null;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_jobs SET state={state},claim_id=NULL,lease_until=NULL,observation_id={id},
                    current_applicable={applicable},last_error={error},updated_at={now},next_attempt_at={retryAt}
                 WHERE tenant_id={Tenant} AND job_id={job.Id}
                """, ct);
        }
        await tx.CommitAsync(ct);
    }
}
