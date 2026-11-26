using System.Text;
using System.Text.Json;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionLineageStoreV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRevisionOwnerV1
{
    private const string LegacyContract = "Main.Plm.EngineeringLegacyReference.commands.v1";

    public Task<EngineeringLegacyReferenceV1> RegisterLegacyAsync(EngineeringLegacyRegistrationV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.SourceRepositorySha?.Length != 40
            || !input.SourceRepositorySha.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') || !ValidDigest(input.SourceHash)
            || !ValidText(input.SourceRecordRef, 512) || Encoding.UTF8.GetByteCount(input.SourceRecordRef) > 1024
            || input.EvidenceGaps == null || input.EvidenceGaps.Length is < 1 or > 32)
            throw Error("ENGINEERING_LEGACY_INPUT_INVALID", 400);
        Reference(input.Product);
        if (input.SourcePath != null) Text(input.SourcePath, 2048);
        foreach (var gap in input.EvidenceGaps) Text(gap, 2048);
        Text(input.Reason, 2048);
        var command = OperationsOwnerV1.Freeze(input);
        var bytes = MainOwnerOperationJournalV1.Encode(command);
        var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "legacy-register", true, async (access, token) =>
        {
            await new EngineeringRevisionStoreV1(db).Fence(true, token);
            if (original != null) return await DiscloseLegacy(access, original, command.OperationId, bytes, token);
            await Product(access, command.Product, false, token);
            var saved = await new EngineeringRevisionLineageStoreV1(db).SaveLegacy(
                new(db.CurrentTenantId, Guid.NewGuid(), access.ActorId, DateTimeOffset.UtcNow, command), token);
            await journal.Store(LegacyOwner, LegacyContract, command.OperationId, access.ActorId, bytes, saved.Reference, [saved.Reference.Id],
                new { action = "engineering-legacy-register", saved.Reference, product = command.Product, command.Reason }, token);
            return saved;
        }, ct, actionAtFence: async token =>
        {
            original = await journal.Read(LegacyOwner, command.OperationId, token);
            return original == null ? "legacy-register" : "query";
        });
    }

    private async Task<EngineeringLegacyReferenceV1> ReadLegacy(OperationsAccessV1 access, Guid id, CancellationToken ct)
    {
        var value = await new EngineeringRevisionLineageStoreV1(db).ReadLegacy(id, ct);
        await Product(access, value.Original.Registration.Product, false, ct);
        return value;
    }

    public Task<EngineeringLegacyReferenceV1> LegacyReadAsync(Guid id, CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, (a, t) => ReadLegacy(a, id, t), ct);

    private async Task<EngineeringLegacyReferenceV1> DiscloseLegacy(OperationsAccessV1 access, MainOwnerOperationJournalV1.Original original,
        Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != LegacyContract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var reference = JsonSerializer.Deserialize<EngineeringSourceRefV1>(original.Result);
        if (reference == null || !original.Objects.SequenceEqual(new[] { reference.Id })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var result = await ReadLegacy(access, reference.Id, ct);
        if (result.Reference != reference || result.Original.Registration.OperationId != id) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }

    public Task<EngineeringLegacyOperationV1> LegacyOperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", close, async (a, t) =>
        {
            var journal = new MainOwnerOperationJournalV1(db);
            var original = await journal.Read(LegacyOwner, id, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != LegacyContract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(id, "CLOSED") : new EngineeringLegacyOperationV1(id, "COMMITTED", await DiscloseLegacy(a, original, id, null, t));
            }
            if (!close) return new EngineeringLegacyOperationV1(id, "UNKNOWN");
            var closed = new EngineeringLegacyOperationV1(id, "CLOSED");
            await journal.Store(LegacyOwner, LegacyContract, id, a.ActorId, null, closed, [], new { action = "close-uncommitted-engineering-legacy" }, t);
            return closed;
        }, ct, readOnlyAuthorityFence: !close);
    }

    private async Task VerifyLegacyOrigin(OperationsAccessV1 access, EngineeringSourceRefV1 reference, EngineeringProductRefV1 product, CancellationToken ct)
    {
        var query = await access.SourceAsync(OperationsOwnerV1.EngineeringRevision, ct);
        var evidence = await ReadLegacy(query, reference.Id, ct);
        if (evidence.Reference != reference) throw Error("ENGINEERING_LEGACY_REFERENCE_CHANGED", 412);
        if (evidence.Original.Registration.Product.Id != product.Id) throw Error("ENGINEERING_LEGACY_PRODUCT_MISMATCH", 422);
    }
}
