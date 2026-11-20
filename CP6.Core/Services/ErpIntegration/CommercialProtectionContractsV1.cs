using System.Text.Json.Serialization;
using Npgsql;

namespace CP6.Core.Services.ErpIntegration;

public sealed record IcEvidenceRefV1(string Authority, string Key, string Version, string ProofRef);
public sealed record IcRequestLocatorV1(Guid TenantId, string Authority, string LegalEntityKey, Guid HandoffId,
    string RequestVersion, string IntentDigest);
public sealed record IcStreamIdentityV1(string Producer, string ContractVersion, string Epoch);
public sealed record IcExecutionRefV1(Guid TenantId, string ConsumerAuthority, string OperationKind, Guid OperationId,
    string ExecutionFingerprint, string ExecutionGeneration, string ParticipantSetDigest);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IcHandoffSubjectV1), "HANDOFF")]
[JsonDerivedType(typeof(IcOwnerResultSubjectV1), "OWNER_RESULT")]
public abstract record IcProtectedSubjectV1(string OriginalBodyHash);

public sealed record IcHandoffSubjectV1(IcRequestLocatorV1 Locator, CommercialSourceHandoffV1 Source, string OriginalBodyHash)
    : IcProtectedSubjectV1(OriginalBodyHash);

public sealed record IcOwnerResultSubjectV1(string Authority, string LegalEntityKey, CommercialSourceHandoffV1 Source,
    IcStreamIdentityV1 Stream, string BusinessKey, string EventId, string Ordinal, string BusinessDigest, string OriginalBodyHash)
    : IcProtectedSubjectV1(OriginalBodyHash);

public sealed record IcParticipantV1(string ParticipantAuthority, IcProtectedSubjectV1 Subject, string Purpose);
public sealed record IcProtectionPrepareV1(string SchemaVersion, string ProtectionKey, IcExecutionRefV1 Execution,
    string ParticipantAuthority, IcProtectedSubjectV1 Subject, string Purpose, string ExpectedControlWatermark,
    DateTimeOffset AdmissionDeadline, IcEvidenceRefV1 ConsumerRegistration);
public sealed record IcSourceControlRefV1(string Kind, string Authority, string Key, string Watermark);
public sealed record IcProtectionGrantV1(string ProtectionKey, IcExecutionRefV1 Execution, string ParticipantAuthority,
    IcProtectedSubjectV1 Subject, string Purpose, string SourceGeneration, string SourceControlWatermark, string State,
    DateTimeOffset GrantedAt, DateTimeOffset ProgressDeadline, bool AutomaticExpiry, string SharedWriterPolicy,
    IcSourceControlRefV1[] ProtectedControls, string GrantDigest, IcEvidenceRefV1 GrantEvidence);
public sealed record IcExecutionTerminalV1(IcExecutionRefV1 Execution, string Outcome, string EffectScope,
    string? BusinessReceiptRef, string? EffectDigest, string? AbortCode, string TerminalGeneration,
    DateTimeOffset RecordedAt, IcEvidenceRefV1 ConsumerEvidence);
public sealed record IcProtectionReadV1(string ProtectionKey, string Knowledge, IcProtectionGrantV1? Grant, string? DenialCode,
    IcExecutionTerminalV1? Terminal, bool NewGrantsBlocked, DateTimeOffset AsOf);
public sealed record IcExecutionReadV1(IcExecutionRefV1 Execution, string State, string[] AcceptedGrantDigests,
    IcExecutionTerminalV1? Terminal, bool NoEffectProven, DateTimeOffset AsOf);
public sealed record IcProtectionRevocationReadV1(Guid RevocationId, string NewGrants, string ExistingGrants,
    string[] PendingProtectionKeys, string? EffectiveControlWatermark, DateTimeOffset RequestedAt, DateTimeOffset? SettledAt);
public sealed record IcProtectionRevocationV1(Guid RevocationId, string NextControlWatermark, string ReasonCode, IcEvidenceRefV1 Decision);
public sealed record IcExecutionOriginalV1(IcExecutionReadV1 State, byte[]? OriginalReceipt);
public sealed record IcBusinessEffectV1(string BusinessReceiptRef, byte[] OriginalReceipt);

public sealed record IcSourcePublicationV1(string ParticipantAuthority, string SourceGeneration, string SharedWriterPolicy,
    string SubjectWatermark, string PurposePolicyKey, string PurposeWatermark, string? StreamWatermark);

/// <summary>由消费 Owner 冻结原命令。AsOf、当前权限及外部结果取自各自持守者。</summary>
public sealed record IcExecutionDefinitionV1(IcExecutionRefV1 Execution, IcParticipantV1[] Participants, string ActorSubject,
    string EffectKey, string? IfMatch, byte[] OriginalInput, DateTimeOffset AdmissionDeadline, IcEvidenceRefV1 ConsumerRegistration);

public enum IcAuthorityActionV1
{
    PublishSource, ReadSourceOriginal, ReadProtection, Prepare, Revoke, Settle, StartExecution, ReadExecution, Ready, Commit, Abort
}

public sealed record IcAuthorityRequestV1(IcAuthorityActionV1 Action, Guid TenantId, string Authority,
    IcParticipantV1? Participant = null, IcExecutionRefV1? Execution = null, IcEvidenceRefV1? Evidence = null,
    IcSourceControlRefV1? Control = null, IcExecutionDefinitionV1? Definition = null, CommercialOriginalV1? Original = null,
    IcProtectionPrepareV1? Prepare = null, IcSourcePublicationV1? Publication = null,
    IcProtectionRevocationV1? Revocation = null, string? AbortCode = null);

/// <summary>真实 Owner 在传入的当前 PostgreSQL 事务持守自己的授权、源事实和范围。调用者由注册适配器决定。</summary>
public interface ICommercialAuthorityGateV1
{
    Task HoldCurrentAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcAuthorityRequestV1 request, CancellationToken ct);
}

/// <summary>按已注册 Authority 查询实际持久原键。调用在业务事务锁外。</summary>
public interface ICommercialOwnerOriginalsV1
{
    Task<IcProtectionReadV1?> ReadGrantAsync(IcParticipantV1 participant, string protectionKey, IcExecutionRefV1 execution,
        CancellationToken ct);
    Task<IcExecutionTerminalV1?> ReadTerminalAsync(IcExecutionRefV1 execution, CancellationToken ct);
}

public static class CommercialProtectionV1
{
    public const string Contract = "IC-SOURCE-PROTECTION/1";
    public const string MainIntakeNewEffect = "MAIN_INTAKE_NEW_EFFECT";
    public const string CrmResultProjection = "CRM_RESULT_PROJECTION";

    public static string GrantDigest(IcProtectionGrantV1 grant) => CommercialCanonicalV1.Digest("IC-GRANT-1",
        System.Text.Json.JsonSerializer.SerializeToElement(grant, CommercialCanonicalV1.Json), removeProof: true, omitRoot: ["grantDigest"]);

    public static IcProtectedSubjectV1 Subject(CommercialOriginalV1 original)
    {
        var route = original.Route;
        var data = original.Coordinates;
        return route.Contract == CommercialOriginalV1.Handoff
            ? new IcHandoffSubjectV1(new(route.TenantId, route.Authority, route.LegalEntityKey, data.Source.HandoffId,
                data.Source.RequestVersion, data.Source.IntentDigest), data.Source, original.BodyHash)
            : new IcOwnerResultSubjectV1(route.Authority, route.LegalEntityKey, data.Source,
                new(route.Producer, route.Contract, route.Epoch), data.BusinessKey, data.EventId, data.Ordinal, data.BusinessDigest, original.BodyHash);
    }

    public static string ProtectionKey(IcExecutionRefV1 execution, IcParticipantV1 participant)
    {
        RequireExecution(execution);
        RequireParticipant(execution.TenantId, execution.OperationKind, participant);
        return CommercialCanonicalV1.Digest("IC-PROTECTION-KEY-1", new
        {
            execution, participant.ParticipantAuthority, participant.Subject, participant.Purpose
        });
    }

    public static IcExecutionDefinitionV1 Freeze(Guid tenant, string consumerAuthority, string operationKind, Guid operationId,
        string generation, string actorSubject, string effectKey, string? ifMatch, ReadOnlyMemory<byte> originalInput,
        IEnumerable<IcParticipantV1> participating, DateTimeOffset deadline, IcEvidenceRefV1 registration)
    {
        if (tenant == Guid.Empty || operationId == Guid.Empty || originalInput.Length is 0 or > CommercialOriginalV1.MaximumBodyBytes ||
            ifMatch is { Length: > 1024 } || operationKind is not ("intake.decide" or "opportunity.result.apply"))
            throw new CommercialProtocolException("EXECUTION_INVALID");
        _ = CommercialWireV1.RequireKey(consumerAuthority);
        _ = CommercialWireV1.Revision(generation);
        _ = CommercialWireV1.RequireKey(actorSubject);
        _ = CommercialWireV1.RequireKey(effectKey);
        CheckEvidence(registration);
        var participants = participating.ToArray();
        if (participants.Length is 0 or > 201) throw new CommercialProtocolException("PARTICIPANTS_INCOMPLETE");
        foreach (var participant in participants) RequireParticipant(tenant, operationKind, participant);
        participants = participants.OrderBy(p => p.ParticipantAuthority, StringComparer.Ordinal)
            .ThenBy(p => System.Text.Encoding.UTF8.GetString(CommercialCanonicalV1.Data(p.Subject)), StringComparer.Ordinal).ToArray();
        if (participants.GroupBy(p => (p.ParticipantAuthority, Subject: NaturalSubjectKey(p), p.Purpose)).Any(g => g.Count() != 1))
            throw new CommercialProtocolException("PARTICIPANTS_INCOMPLETE");
        var participantSetDigest = CommercialCanonicalV1.Digest("IC-PARTICIPANT-SET-1", participants);
        var bytes = originalInput.ToArray();
        var fingerprint = CommercialCanonicalV1.Digest("IC-EXECUTION-INPUT-1", new
        {
            bodyHash = CommercialOriginalV1.Hash(bytes), tenant, consumerAuthority, operationKind, operationId, generation,
            actorSubject, effectKey, ifMatch, participantSetDigest, admissionDeadline = Milliseconds(deadline), registration
        });
        return new(new(tenant, consumerAuthority, operationKind, operationId, fingerprint, generation, participantSetDigest),
            participants, actorSubject, effectKey, ifMatch, bytes, Milliseconds(deadline), registration);
    }

    internal static void RequireParticipant(Guid tenant, string? operation, IcParticipantV1 participant)
    {
        _ = CommercialWireV1.RequireKey(participant.ParticipantAuthority);
        if (!CommercialWireV1.IsDigest(participant.Subject.OriginalBodyHash)) throw new CommercialProtocolException("SUBJECT_BINDING_CONFLICT");
        switch (participant)
        {
            case { Purpose: MainIntakeNewEffect, Subject: IcHandoffSubjectV1 request }:
                var source = request.Source;
                var locator = request.Locator;
                if (tenant != locator.TenantId || source.HandoffId != locator.HandoffId ||
                    source.RequestVersion != locator.RequestVersion || source.IntentDigest != locator.IntentDigest ||
                    operation is not (null or "intake.decide"))
                    throw new CommercialProtocolException("SUBJECT_BINDING_CONFLICT");
                _ = CommercialWireV1.RequireKey(locator.Authority);
                _ = CommercialWireV1.RequireKey(locator.LegalEntityKey);
                RequireSource(source);
                break;
            case { Purpose: CrmResultProjection, Subject: IcOwnerResultSubjectV1 result }:
                if (operation is not (null or "opportunity.result.apply") ||
                    result.Stream.ContractVersion is not (CommercialOriginalV1.Intake or CommercialOriginalV1.Quote or CommercialOriginalV1.Commercial) ||
                    !CommercialWireV1.IsDigest(result.BusinessDigest)) throw new CommercialProtocolException("SUBJECT_BINDING_CONFLICT");
                foreach (var coordinate in new[] { result.Authority, result.LegalEntityKey, result.BusinessKey, result.EventId,
                    result.Stream.Producer, result.Stream.Epoch }) _ = CommercialWireV1.RequireKey(coordinate);
                _ = CommercialWireV1.Revision(result.Ordinal);
                RequireSource(result.Source);
                break;
            default: throw new CommercialProtocolException("PROTECTION_PURPOSE_UNSUPPORTED");
        }
    }

    internal static void RequireExecution(IcExecutionRefV1 execution)
    {
        if (execution.TenantId == Guid.Empty || execution.OperationId == Guid.Empty ||
            execution.OperationKind is not ("intake.decide" or "opportunity.result.apply") ||
            !CommercialWireV1.IsDigest(execution.ExecutionFingerprint) || !CommercialWireV1.IsDigest(execution.ParticipantSetDigest))
            throw new CommercialProtocolException("EXECUTION_INVALID");
        _ = CommercialWireV1.RequireKey(execution.ConsumerAuthority);
        _ = CommercialWireV1.Revision(execution.ExecutionGeneration);
    }

    internal static void CheckEvidence(IcEvidenceRefV1 proof)
    {
        foreach (var coordinate in new[] { proof.Authority, proof.Key, proof.Version, proof.ProofRef })
            _ = CommercialWireV1.RequireKey(coordinate);
    }

    private static void RequireSource(CommercialSourceHandoffV1 source)
    {
        if (source.HandoffId == Guid.Empty || source.AccountId == Guid.Empty || source.OpportunityId == Guid.Empty ||
            source.IntentId == Guid.Empty || !CommercialWireV1.IsDigest(source.IntentDigest))
            throw new CommercialProtocolException("SUBJECT_BINDING_CONFLICT");
        _ = CommercialWireV1.Revision(source.RequestVersion);
        _ = CommercialWireV1.Revision(source.RequirementVersion);
        _ = CommercialWireV1.Revision(source.ForecastVersion);
    }

    internal static string NaturalSubjectKey(IcParticipantV1 participant) => participant.Subject switch
    {
        IcHandoffSubjectV1 h => CommercialCanonicalV1.Digest("IC-SUBJECT-1", new { h.Source.HandoffId, h.Source.RequestVersion, participant.Purpose }),
        IcOwnerResultSubjectV1 r => CommercialCanonicalV1.Digest("IC-SUBJECT-1", new
        { r.Authority, r.LegalEntityKey, r.Stream, r.BusinessKey, r.Ordinal, participant.Purpose }),
        _ => throw new CommercialProtocolException("SUBJECT_BINDING_CONFLICT")
    };

    public static string ProducerStreamControlKey(Guid tenant, IcOwnerResultSubjectV1 result) =>
        CommercialCanonicalV1.Digest("IC-PRODUCER-STREAM-1", new
        { tenant, result.Authority, result.LegalEntityKey, result.Stream });

    internal static string EffectScope(IcExecutionRefV1 execution) =>
        execution.OperationKind == "intake.decide" ? "MAIN_INTAKE" : "CRM_PROJECTION";
    internal static DateTimeOffset Milliseconds(DateTimeOffset instant) =>
        DateTimeOffset.FromUnixTimeMilliseconds(instant.ToUnixTimeMilliseconds());
}
