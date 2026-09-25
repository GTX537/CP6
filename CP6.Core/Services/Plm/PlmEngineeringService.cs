using System.Data;
using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Plm;
using CP6.Entity.DTOs.Plm;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace CP6.Core.Services.Plm;

public sealed class PlmEngineeringService : IPlmEngineeringService
{
    private readonly string _connectionString;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public PlmEngineeringService(IConfiguration configuration, ITenantContext tenant, TimeProvider clock)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is required for PLM.");
        _tenant = tenant;
        _clock = clock;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
    private void CheckScope(Guid tenantId, string? actor = null)
    {
        if (tenantId == Guid.Empty || tenantId != _tenant.CurrentTenantId || actor is not null && (string.IsNullOrWhiteSpace(actor) || actor.Length > 100))
            throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
    }
    private PlmContext NewPlm(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<PlmContext>()
            .UseSqlServer(_connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Plm", "plm")).Options;
        return new PlmContext(options, new TenantContext { CurrentTenantId = tenantId });
    }
    private CP6Context NewOwner(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<CP6Context>().UseSqlServer(_connectionString).Options;
        return new CP6Context(options, new TenantContext { CurrentTenantId = tenantId });
    }
    private async Task<OwnerScope> OpenOwnerScopeAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var plm = new PlmContext(new DbContextOptionsBuilder<PlmContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Plm", "plm")).Options,
            new TenantContext { CurrentTenantId = tenantId });
        var owner = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection).Options,
            new TenantContext { CurrentTenantId = tenantId });
        var transaction = await plm.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await owner.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        return new OwnerScope(connection, plm, owner, transaction);
    }
    private sealed class OwnerScope(SqlConnection connection, PlmContext plm, CP6Context owner, IDbContextTransaction transaction) : IAsyncDisposable
    {
        public PlmContext Plm { get; } = plm;
        public CP6Context Owner { get; } = owner;
        public IDbContextTransaction Transaction { get; } = transaction;
        public async ValueTask DisposeAsync()
        {
            await Owner.DisposeAsync();
            await Plm.DisposeAsync();
            await Transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
    private static bool Unique(DbUpdateException e) => e.InnerException is SqlException sql && sql.Number is 2601 or 2627;
    private static bool Busy(SqlException e) => e.Number is 1205 or -2;
    private static string Version(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');
    private static string Key(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length > max) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
        var normalized = value.Normalize(System.Text.NormalizationForm.FormC);
        if (normalized.Length > max) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch < 32) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            if (char.IsHighSurrogate(ch))
            {
                if (++i >= normalized.Length || !char.IsLowSurrogate(normalized[i]))
                    throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            }
            else if (char.IsLowSurrogate(ch)) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
        }
        return normalized;
    }
    private static void RequireHash(string value)
    {
        if (value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
    }
    private static void RequireGuid(Guid value)
    {
        if (value == Guid.Empty) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
    }
    private static void RequireVersion(int version)
    {
        if (version <= 0) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
    }
    private static void RequireRowVersion(byte[] value)
    {
        if (value is not { Length: 8 }) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
    }
    private static string CaptureHash(Guid tenant, Guid candidate, CaptureTechnicalManifestCommand cmd) => PlmDigest.HashCommand("plm-capture-command-v1",
        ("tenantId", tenant.ToString("D").ToLowerInvariant(), false),
        ("candidateId", candidate.ToString("D").ToLowerInvariant(), false),
        ("expectedCandidateVersion", Version(cmd.ExpectedCandidateVersion), true),
        ("expectedCandidateRowVersion", Base64(cmd.ExpectedCandidateRowVersion), false),
        ("captureRequestId", cmd.CaptureRequestId.ToString("D").ToLowerInvariant(), false));
    private static string FreezeHash(Guid tenant, Guid candidate, FreezeEngineeringBaselineCommand cmd) => PlmDigest.HashCommand("plm-freeze-command-v1",
        ("tenantId", tenant.ToString("D").ToLowerInvariant(), false),
        ("candidateId", candidate.ToString("D").ToLowerInvariant(), false),
        ("expectedCandidateVersion", Version(cmd.ExpectedCandidateVersion), true),
        ("expectedCandidateRowVersion", Base64(cmd.ExpectedCandidateRowVersion), false),
        ("manifestId", cmd.ManifestId.ToString("D").ToLowerInvariant(), false),
        ("expectedManifestDigest", cmd.ExpectedManifestDigest, false),
        ("freezeRequestId", cmd.FreezeRequestId.ToString("D").ToLowerInvariant(), false));
    private static string SupersedeHash(Guid tenant, Guid baseline, SupersedeEngineeringBaselineCommand cmd) => PlmDigest.HashCommand("plm-supersede-command-v1",
        ("tenantId", tenant.ToString("D").ToLowerInvariant(), false),
        ("baselineId", baseline.ToString("D").ToLowerInvariant(), false),
        ("expectedBaselineRowVersion", Base64(cmd.ExpectedBaselineRowVersion), false),
        ("reason", cmd.Reason, false),
        ("supersedeRequestId", cmd.SupersedeRequestId.ToString("D").ToLowerInvariant(), false));
    private static IterationCandidateResult IterationResult(PlmIterationCandidate x) => new(x.Id, x.IterationKey, x.IterationVersion, x.ReviewPackageDigest, x.Status);
    private static EngineeringCandidateResult CandidateResult(PlmEngineeringCandidate x) => new(x.Id, x.IterationCandidateId, x.ProductCd, x.CandidateVersion, x.Status, x.RowVersion);
    private static TechnicalManifestResult ManifestResult(PlmTechnicalManifest x, byte[] rowVersion) => new(x.Id, x.EngineeringCandidateId, x.CandidateVersion, x.ProductCd, x.SchemaVersion, x.ManifestDigest, x.ItemCount, x.CanonicalBytes, rowVersion);
    private static EngineeringBaselineResult BaselineResult(PlmEngineeringBaseline x) => new(x.Id, x.EngineeringCandidateId, x.CandidateVersion, x.ManifestId, x.ManifestDigest, x.Status, x.FrozenAtUtc, x.SupersededAtUtc, x.RowVersion);

    public async Task<PlmOperation<IterationCandidateResult>> CreateIterationCandidateAsync(Guid tenantId, string actor, CreateIterationCandidateCommand command, CancellationToken ct)
    {
        CheckScope(tenantId, actor); RequireVersion(command.IterationVersion);
        var key = Key(command.IterationKey, 80);
        if (command.ReviewPackageDigest is not null) RequireHash(command.ReviewPackageDigest);
        await using var db = NewPlm(tenantId);
        var existing = await db.IterationCandidates.SingleOrDefaultAsync(x => x.IterationKey == key && x.IterationVersion == command.IterationVersion, ct);
        if (existing is not null) return ExistingIteration(existing, command.ReviewPackageDigest);
        var item = new PlmIterationCandidate { Id = Guid.NewGuid(), TenantId = tenantId, IterationKey = key, IterationVersion = command.IterationVersion,
            ReviewPackageDigest = command.ReviewPackageDigest, Status = "OPEN", CreatedAtUtc = UtcNow, CreatedBy = actor };
        db.IterationCandidates.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (Unique(e))
        {
            await using var fresh = NewPlm(tenantId);
            existing = await fresh.IterationCandidates.SingleAsync(x => x.IterationKey == key && x.IterationVersion == command.IterationVersion, ct);
            return ExistingIteration(existing, command.ReviewPackageDigest);
        }
        return new(IterationResult(item), true);
    }
    private static PlmOperation<IterationCandidateResult> ExistingIteration(PlmIterationCandidate existing, string? digest) =>
        existing.ReviewPackageDigest == digest ? new(IterationResult(existing), false) : throw PlmException.Conflict("PLM_ITERATION_KEY_CONFLICT");

    public async Task<PlmOperation<EngineeringCandidateResult>> CreateEngineeringCandidateAsync(Guid tenantId, string actor, CreateEngineeringCandidateCommand command, CancellationToken ct)
    {
        CheckScope(tenantId, actor); RequireGuid(command.IterationCandidateId);
        var key = Key(command.CandidateKey, 80); var hint = Key(command.ProductCd, 20);
        await using var owner = NewOwner(tenantId);
        var product = await owner.ProductMasters.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && !x.IsDeleted && x.ProductCd == hint, ct)
            ?? throw PlmException.NotFound("PLM_OWNER_NOT_FOUND");
        await using var db = NewPlm(tenantId);
        if (!await db.IterationCandidates.AnyAsync(x => x.Id == command.IterationCandidateId, ct)) throw PlmException.NotFound("PLM_NOT_FOUND");
        var existing = await db.EngineeringCandidates.SingleOrDefaultAsync(x => x.IterationCandidateId == command.IterationCandidateId && x.CandidateKey == key, ct);
        if (existing is not null) return ExistingCandidate(existing, Key(product.ProductCd, 20));
        var now = UtcNow;
        var candidate = new PlmEngineeringCandidate { Id = Guid.NewGuid(), TenantId = tenantId, IterationCandidateId = command.IterationCandidateId,
            CandidateKey = key, ProductCd = Key(product.ProductCd, 20), CandidateVersion = 1, Status = "DRAFT", CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actor, UpdatedBy = actor };
        db.EngineeringCandidates.Add(candidate);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (Unique(e))
        {
            await using var fresh = NewPlm(tenantId);
            existing = await fresh.EngineeringCandidates.SingleAsync(x => x.IterationCandidateId == command.IterationCandidateId && x.CandidateKey == key, ct);
            return ExistingCandidate(existing, Key(product.ProductCd, 20));
        }
        return new(CandidateResult(candidate), true);
    }
    private static PlmOperation<EngineeringCandidateResult> ExistingCandidate(PlmEngineeringCandidate existing, string productCd) =>
        existing.ProductCd == productCd ? new(CandidateResult(existing), false) : throw PlmException.Conflict("PLM_CANDIDATE_KEY_CONFLICT");

    private async Task<PlmEngineeringCandidate> LockCandidateAsync(PlmContext db, Guid tenant, Guid candidate, CancellationToken ct) =>
        await db.EngineeringCandidates.FromSqlInterpolated($"SELECT * FROM [plm].[EngineeringCandidate] WITH (UPDLOCK,HOLDLOCK) WHERE [TenantId]={tenant} AND [Id]={candidate}")
            .SingleOrDefaultAsync(ct) ?? throw PlmException.NotFound("PLM_NOT_FOUND");

    private static void CheckCandidate(PlmEngineeringCandidate candidate, int version, byte[] rowVersion)
    {
        if (candidate.CandidateVersion != version || !Same(candidate.RowVersion, rowVersion)) throw PlmException.Conflict("PLM_CANDIDATE_STALE");
    }

    private static async Task<PlmEncodedManifest> ReadOwnerManifestAsync(OwnerScope scope, Guid tenant, string productCd, CancellationToken ct)
    {
        var product = await scope.Owner.ProductMasters.FromSqlInterpolated($"SELECT * FROM [dbo].[T_ProductMaster] WITH (UPDLOCK,HOLDLOCK) WHERE [TenantId]={tenant} AND [ProductCd]={productCd} AND [IsDeleted]=CAST(0 AS bit)")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw PlmException.NotFound("PLM_OWNER_NOT_FOUND");
        var materials = await scope.Owner.ProductMaterials.FromSqlInterpolated($"SELECT * FROM [dbo].[T_ProductMaterial] WITH (UPDLOCK,HOLDLOCK,INDEX(IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd)) WHERE [TenantId]={tenant} AND [ProductCd]={product.ProductCd} AND [IsDeleted]=CAST(0 AS bit)")
            .AsNoTracking().ToListAsync(ct);
        var processes = await scope.Owner.ProductProcesses.FromSqlInterpolated($"SELECT * FROM [dbo].[T_ProductProcess] WITH (UPDLOCK,HOLDLOCK,INDEX(IX_T_ProductProcess_ProductCd_TaskCd)) WHERE [TenantId]={tenant} AND [ProductCd]={product.ProductCd} AND [IsDeleted]=CAST(0 AS bit)")
            .AsNoTracking().ToListAsync(ct);
        var items = new List<PlmProjectedItem> { PlmSnapshotProjector.Product(product) };
        items.AddRange(materials.Select(PlmSnapshotProjector.Bom));
        items.AddRange(processes.Select(PlmSnapshotProjector.Routing));
        return PlmCanonicalWriter.Encode(tenant, items);
    }

    private static async Task<PlmTechnicalManifest> LoadManifestAsync(PlmContext db, Guid id, CancellationToken ct) =>
        await db.TechnicalManifests.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw PlmException.NotFound("PLM_NOT_FOUND");
    private static async Task<PlmEncodedManifest> VerifyManifestAsync(PlmContext db, PlmTechnicalManifest manifest, CancellationToken ct)
    {
        var items = await db.TechnicalManifestItems.AsNoTracking().Where(x => x.ManifestId == manifest.Id).ToListAsync(ct);
        return PlmDigest.Verify(manifest, items);
    }

    private static bool IsTransient(Exception ex) => ex is SqlException sql && Busy(sql) || ex is DbUpdateException update && update.InnerException is SqlException inner && Busy(inner);
    private static async Task<T> RetryOwnerAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await operation(); }
            catch (Exception ex) when (IsTransient(ex) && attempt < 2)
            {
                await Task.Delay(40 + Random.Shared.Next(80), ct);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                throw new PlmException("PLM_OWNER_BUSY", 503, "Owner lock retry exhausted.");
            }
        }
    }

    public async Task<PlmOperation<TechnicalManifestResult>> CaptureTechnicalManifestAsync(Guid tenantId, string actor, Guid candidateId, CaptureTechnicalManifestCommand command, CancellationToken ct)
    {
        CheckScope(tenantId, actor); RequireGuid(candidateId); RequireGuid(command.CaptureRequestId);
        RequireVersion(command.ExpectedCandidateVersion); RequireRowVersion(command.ExpectedCandidateRowVersion);
        var hash = CaptureHash(tenantId, candidateId, command);
        try
        {
            return await RetryOwnerAsync(async () =>
            {
                var prior = await FindCaptureAsync(tenantId, command.CaptureRequestId, hash, ct);
                if (prior is not null) return new PlmOperation<TechnicalManifestResult>(prior, false);
                await using var scope = await OpenOwnerScopeAsync(tenantId, ct);
                var candidate = await LockCandidateAsync(scope.Plm, tenantId, candidateId, ct);
                var committed = await scope.Plm.TechnicalManifests.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CaptureRequestId == command.CaptureRequestId, ct);
                if (committed is not null)
                {
                    if (committed.CaptureInputHash != hash) throw PlmException.Conflict("PLM_REQUEST_PAYLOAD_CONFLICT");
                    await VerifyManifestAsync(scope.Plm, committed, ct);
                    return new PlmOperation<TechnicalManifestResult>(ManifestResult(committed, candidate.RowVersion), false);
                }
                CheckCandidate(candidate, command.ExpectedCandidateVersion, command.ExpectedCandidateRowVersion);
                var ownerManifest = await ReadOwnerManifestAsync(scope, tenantId, candidate.ProductCd, ct);
                var version = candidate.CurrentManifestId.HasValue ? checked(candidate.CandidateVersion + 1) : candidate.CandidateVersion;
                var now = UtcNow;
                var manifest = new PlmTechnicalManifest
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EngineeringCandidateId = candidate.Id, CandidateVersion = version,
                    ProductCd = candidate.ProductCd, SchemaVersion = PlmCanonicalWriter.ManifestSchema,
                    CanonicalBytes = ownerManifest.CanonicalBytes, ManifestDigest = ownerManifest.ManifestDigest,
                    ItemCount = ownerManifest.Items.Count, CaptureRequestId = command.CaptureRequestId,
                    CaptureInputHash = hash, CapturedAtUtc = now, CapturedBy = actor
                };
                scope.Plm.TechnicalManifests.Add(manifest);
                foreach (var encoded in ownerManifest.Items)
                {
                    var p = encoded.Projection;
                    scope.Plm.TechnicalManifestItems.Add(new PlmTechnicalManifestItem
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, ManifestId = manifest.Id,
                        ItemType = p.ItemType, SourceOwnerType = p.SourceOwnerType,
                        IdentityKey = encoded.IdentityKey, ProductCd = p.ProductCd,
                        ProcessCd = p.ProcessCd, MaterialCd = p.MaterialCd, TaskCd = p.TaskCd,
                        SemanticSortOrder = p.SortOrder, ItemCanonicalBytes = encoded.ItemCanonicalBytes,
                        ItemDigest = encoded.ItemDigest, SourceOwnerId = p.SourceOwnerId,
                        SourceRowVersion = p.SourceRowVersion, CapturedAtUtc = now, CapturedBy = actor
                    });
                }
                candidate.CandidateVersion = version;
                candidate.CurrentManifestId = manifest.Id;
                candidate.Status = "DRAFT";
                candidate.UpdatedAtUtc = now;
                candidate.UpdatedBy = actor;
                await scope.Plm.SaveChangesAsync(ct);
                await scope.Transaction.CommitAsync(ct);
                return new PlmOperation<TechnicalManifestResult>(ManifestResult(manifest, candidate.RowVersion), true);
            }, ct);
        }
        catch (DbUpdateException e) when (Unique(e))
        {
            var prior = await FindCaptureAsync(tenantId, command.CaptureRequestId, hash, ct);
            if (prior is not null) return new(prior, false);
            throw PlmException.Conflict("PLM_CANDIDATE_STALE");
        }
    }

    private async Task<TechnicalManifestResult?> FindCaptureAsync(Guid tenantId, Guid requestId, string hash, CancellationToken ct)
    {
        await using var db = NewPlm(tenantId);
        var manifest = await db.TechnicalManifests.AsNoTracking().SingleOrDefaultAsync(x => x.CaptureRequestId == requestId, ct);
        if (manifest is null) return null;
        if (manifest.CaptureInputHash != hash) throw PlmException.Conflict("PLM_REQUEST_PAYLOAD_CONFLICT");
        await VerifyManifestAsync(db, manifest, ct);
        var candidate = await db.EngineeringCandidates.AsNoTracking().SingleAsync(x => x.Id == manifest.EngineeringCandidateId, ct);
        return ManifestResult(manifest, candidate.RowVersion);
    }

    public async Task<TechnicalManifestResult> ValidateTechnicalManifestAsync(Guid tenantId, string actor, Guid candidateId, Guid manifestId, ValidateTechnicalManifestCommand command, CancellationToken ct)
    {
        CheckScope(tenantId, actor); RequireGuid(candidateId); RequireGuid(manifestId);
        RequireVersion(command.ExpectedCandidateVersion); RequireRowVersion(command.ExpectedCandidateRowVersion); RequireHash(command.ExpectedManifestDigest);
        return await RetryOwnerAsync(async () =>
        {
            await using var scope = await OpenOwnerScopeAsync(tenantId, ct);
            var candidate = await LockCandidateAsync(scope.Plm, tenantId, candidateId, ct);
            if (candidate.CandidateVersion != command.ExpectedCandidateVersion || candidate.CurrentManifestId != manifestId)
                throw PlmException.Conflict("PLM_MANIFEST_STALE");
            if (candidate.Status != "VALIDATED" || !Same(candidate.RowVersion, command.ExpectedCandidateRowVersion))
                CheckCandidate(candidate, command.ExpectedCandidateVersion, command.ExpectedCandidateRowVersion);
            if (candidate.Status is not ("DRAFT" or "VALIDATED")) throw PlmException.Conflict("PLM_CANDIDATE_STALE");
            var manifest = await LoadManifestAsync(scope.Plm, manifestId, ct);
            if (manifest.EngineeringCandidateId != candidateId || manifest.CandidateVersion != candidate.CandidateVersion)
                throw PlmException.Conflict("PLM_MANIFEST_STALE");
            await VerifyManifestAsync(scope.Plm, manifest, ct);
            if (manifest.ManifestDigest != command.ExpectedManifestDigest)
                throw PlmException.Conflict("PLM_MANIFEST_STALE");
            var owner = await ReadOwnerManifestAsync(scope, tenantId, candidate.ProductCd, ct);
            if (owner.ManifestDigest != manifest.ManifestDigest) throw PlmException.Conflict("PLM_OWNER_CHANGED");
            if (candidate.Status != "VALIDATED")
            {
                candidate.Status = "VALIDATED"; candidate.UpdatedAtUtc = UtcNow; candidate.UpdatedBy = actor;
                await scope.Plm.SaveChangesAsync(ct);
            }
            await scope.Transaction.CommitAsync(ct);
            return ManifestResult(manifest, candidate.RowVersion);
        }, ct);
    }

    public async Task<PlmOperation<EngineeringBaselineResult>> FreezeEngineeringBaselineAsync(Guid tenantId, string actor, Guid candidateId, FreezeEngineeringBaselineCommand command, CancellationToken ct)
    {
        CheckScope(tenantId, actor); RequireGuid(candidateId); RequireGuid(command.FreezeRequestId); RequireGuid(command.ManifestId);
        RequireVersion(command.ExpectedCandidateVersion); RequireRowVersion(command.ExpectedCandidateRowVersion); RequireHash(command.ExpectedManifestDigest);
        var hash = FreezeHash(tenantId, candidateId, command);
        try
        {
            return await RetryOwnerAsync(async () =>
            {
                var prior = await FindFreezeAsync(tenantId, command.FreezeRequestId, hash, ct);
                if (prior is not null) return new PlmOperation<EngineeringBaselineResult>(prior, false);
                await using var scope = await OpenOwnerScopeAsync(tenantId, ct);
                var candidate = await LockCandidateAsync(scope.Plm, tenantId, candidateId, ct);
                var committed = await scope.Plm.EngineeringBaselines.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.FreezeRequestId == command.FreezeRequestId, ct);
                if (committed is not null)
                {
                    if (committed.FreezeInputHash != hash) throw PlmException.Conflict("PLM_REQUEST_PAYLOAD_CONFLICT");
                    var committedManifest = await LoadManifestAsync(scope.Plm, committed.ManifestId, ct);
                    await VerifyManifestAsync(scope.Plm, committedManifest, ct);
                    return new PlmOperation<EngineeringBaselineResult>(BaselineResult(committed), false);
                }
                if (await scope.Plm.EngineeringBaselines.AnyAsync(x => x.EngineeringCandidateId == candidateId && x.CandidateVersion == command.ExpectedCandidateVersion, ct))
                    throw PlmException.Conflict("PLM_BASELINE_EXISTS");
                CheckCandidate(candidate, command.ExpectedCandidateVersion, command.ExpectedCandidateRowVersion);
                if (candidate.Status != "VALIDATED" || candidate.CurrentManifestId != command.ManifestId)
                    throw PlmException.Conflict("PLM_MANIFEST_STALE");
                var manifest = await LoadManifestAsync(scope.Plm, command.ManifestId, ct);
                if (manifest.EngineeringCandidateId != candidateId || manifest.CandidateVersion != candidate.CandidateVersion)
                    throw PlmException.Conflict("PLM_MANIFEST_STALE");
                await VerifyManifestAsync(scope.Plm, manifest, ct);
                if (manifest.ManifestDigest != command.ExpectedManifestDigest)
                    throw PlmException.Conflict("PLM_MANIFEST_STALE");
                var owner = await ReadOwnerManifestAsync(scope, tenantId, candidate.ProductCd, ct);
                if (owner.ManifestDigest != manifest.ManifestDigest) throw PlmException.Conflict("PLM_OWNER_CHANGED");
                if (await scope.Plm.EngineeringBaselines.AnyAsync(x => x.EngineeringCandidateId == candidateId && x.CandidateVersion == candidate.CandidateVersion, ct))
                    throw PlmException.Conflict("PLM_BASELINE_EXISTS");
                var baseline = new PlmEngineeringBaseline
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EngineeringCandidateId = candidateId,
                    CandidateVersion = candidate.CandidateVersion, ManifestId = manifest.Id,
                    ManifestDigest = manifest.ManifestDigest, FreezeRequestId = command.FreezeRequestId,
                    FreezeInputHash = hash, Status = "FROZEN", FrozenAtUtc = UtcNow, FrozenBy = actor
                };
                scope.Plm.EngineeringBaselines.Add(baseline);
                candidate.Status = "FROZEN"; candidate.UpdatedAtUtc = UtcNow; candidate.UpdatedBy = actor;
                await scope.Plm.SaveChangesAsync(ct);
                await scope.Transaction.CommitAsync(ct);
                return new PlmOperation<EngineeringBaselineResult>(BaselineResult(baseline), true);
            }, ct);
        }
        catch (DbUpdateException e) when (Unique(e))
        {
            var prior = await FindFreezeAsync(tenantId, command.FreezeRequestId, hash, ct);
            if (prior is not null) return new(prior, false);
            throw PlmException.Conflict("PLM_BASELINE_EXISTS");
        }
    }

    private async Task<EngineeringBaselineResult?> FindFreezeAsync(Guid tenantId, Guid requestId, string hash, CancellationToken ct)
    {
        await using var db = NewPlm(tenantId);
        var baseline = await db.EngineeringBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.FreezeRequestId == requestId, ct);
        if (baseline is null) return null;
        if (baseline.FreezeInputHash != hash) throw PlmException.Conflict("PLM_REQUEST_PAYLOAD_CONFLICT");
        var manifest = await LoadManifestAsync(db, baseline.ManifestId, ct);
        await VerifyManifestAsync(db, manifest, ct);
        return BaselineResult(baseline);
    }

    public async Task<TechnicalManifestDetailResult> GetTechnicalManifestAsync(Guid tenantId, Guid manifestId, CancellationToken ct)
    {
        CheckScope(tenantId); RequireGuid(manifestId);
        await using var db = NewPlm(tenantId);
        var manifest = await LoadManifestAsync(db, manifestId, ct);
        var items = await db.TechnicalManifestItems.AsNoTracking().Where(x => x.ManifestId == manifestId).ToListAsync(ct);
        var canonical = PlmDigest.Verify(manifest, items);
        var candidate = await db.EngineeringCandidates.AsNoTracking().SingleAsync(x => x.Id == manifest.EngineeringCandidateId, ct);
        var results = canonical.Items.Select(encoded =>
            ToDetailItem(items.Single(x => x.ItemType == encoded.Projection.ItemType && x.IdentityKey == encoded.IdentityKey))).ToArray();
        return new(ManifestResult(manifest, candidate.RowVersion), results);
    }

    private static TechnicalManifestItemResult ToDetailItem(PlmTechnicalManifestItem item)
    {
        using var document = JsonDocument.Parse(item.ItemCanonicalBytes);
        var root = document.RootElement;
        return new(item.ItemType, item.SourceOwnerType, item.IdentityKey,
            root.GetProperty("identity").Clone(), root.GetProperty("content").Clone(),
            item.ItemDigest, item.ItemCanonicalBytes, item.SourceOwnerId, item.SourceRowVersion);
    }

    public async Task<EngineeringBaselineResult> GetEngineeringBaselineAsync(Guid tenantId, Guid baselineId, CancellationToken ct)
    {
        CheckScope(tenantId); RequireGuid(baselineId);
        await using var db = NewPlm(tenantId);
        var baseline = await db.EngineeringBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == baselineId, ct)
            ?? throw PlmException.NotFound("PLM_NOT_FOUND");
        var manifest = await LoadManifestAsync(db, baseline.ManifestId, ct);
        await VerifyManifestAsync(db, manifest, ct);
        if (baseline.ManifestDigest != manifest.ManifestDigest) throw PlmException.Conflict("PLM_MANIFEST_INTEGRITY");
        return BaselineResult(baseline);
    }

    public async Task<EngineeringBaselineResult> ResolveBaselineIdentityAsync(Guid tenantId, Guid candidateId, int candidateVersion, CancellationToken ct)
    {
        CheckScope(tenantId); RequireGuid(candidateId); RequireVersion(candidateVersion);
        await using var db = NewPlm(tenantId);
        var baseline = await db.EngineeringBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.EngineeringCandidateId == candidateId && x.CandidateVersion == candidateVersion, ct)
            ?? throw PlmException.NotFound("PLM_NOT_FOUND");
        return await GetEngineeringBaselineAsync(tenantId, baseline.Id, ct);
    }

    public async Task<TechnicalManifestDetailResult> ResolveCaptureRequestAsync(Guid tenantId, Guid captureRequestId, CancellationToken ct)
    {
        CheckScope(tenantId); RequireGuid(captureRequestId);
        await using var db = NewPlm(tenantId);
        var manifest = await db.TechnicalManifests.AsNoTracking().SingleOrDefaultAsync(x => x.CaptureRequestId == captureRequestId, ct)
            ?? throw PlmException.NotFound("PLM_NOT_FOUND");
        return await GetTechnicalManifestAsync(tenantId, manifest.Id, ct);
    }

    public async Task<EngineeringBaselineResult> ResolveFreezeRequestAsync(Guid tenantId, Guid freezeRequestId, CancellationToken ct)
    {
        CheckScope(tenantId); RequireGuid(freezeRequestId);
        await using var db = NewPlm(tenantId);
        var baseline = await db.EngineeringBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.FreezeRequestId == freezeRequestId, ct)
            ?? throw PlmException.NotFound("PLM_NOT_FOUND");
        return await GetEngineeringBaselineAsync(tenantId, baseline.Id, ct);
    }

    public async Task<EngineeringBaselineResult> SupersedeEngineeringBaselineAsync(Guid tenantId, string actor, Guid baselineId, SupersedeEngineeringBaselineCommand command, CancellationToken ct)
    {
        CheckScope(tenantId, actor); RequireGuid(baselineId); RequireGuid(command.SupersedeRequestId); RequireRowVersion(command.ExpectedBaselineRowVersion);
        var reason = Key(command.Reason, 500);
        var hash = SupersedeHash(tenantId, baselineId, command with { Reason = reason });
        await using var db = NewPlm(tenantId);
        var prior = await db.EngineeringBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.SupersedeRequestId == command.SupersedeRequestId, ct);
        if (prior is not null)
        {
            if (prior.SupersedeInputHash != hash) throw PlmException.Conflict("PLM_REQUEST_PAYLOAD_CONFLICT");
            return await GetEngineeringBaselineAsync(tenantId, prior.Id, ct);
        }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var baseline = await db.EngineeringBaselines.FromSqlInterpolated($"SELECT * FROM [plm].[EngineeringBaseline] WITH (UPDLOCK,HOLDLOCK) WHERE [TenantId]={tenantId} AND [Id]={baselineId}").SingleOrDefaultAsync(ct)
            ?? throw PlmException.NotFound("PLM_NOT_FOUND");
        if (baseline.SupersedeRequestId == command.SupersedeRequestId)
        {
            if (baseline.SupersedeInputHash != hash) throw PlmException.Conflict("PLM_REQUEST_PAYLOAD_CONFLICT");
            return BaselineResult(baseline);
        }
        if (baseline.Status != "FROZEN" || !Same(baseline.RowVersion, command.ExpectedBaselineRowVersion))
            throw PlmException.Conflict("PLM_BASELINE_IMMUTABLE");
        baseline.Status = "SUPERSEDED"; baseline.SupersededAtUtc = UtcNow; baseline.SupersededBy = actor;
        baseline.SupersedeReason = reason; baseline.SupersedeRequestId = command.SupersedeRequestId; baseline.SupersedeInputHash = hash;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return BaselineResult(baseline);
    }
}
