using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.ErpIntegration;
using Microsoft.AspNetCore.WebUtilities;

namespace CP6.Core.Services.Plm;

public sealed record EngineeringCaseNaturalKeyV1(Guid TenantId, string SourceSystem, Guid OpportunityId,
    string TargetAuthority, string LegalEntityKey, string Purpose);

public sealed record EngineeringIntakeBindingV1(string SourceKind, Guid SourceId, string SourceVersion,
    string SourceDigest, string DigestKeyVersion);

public sealed record EngineeringNextQueryV1(EngineeringCaseNaturalKeyV1 CaseNaturalKey, Guid? CaseId,
    string CurrentSubmissionVersion, Guid? CurrentRequestKey, string? CurrentReceiptId, EngineeringIntakeBindingV1? CurrentSource);

public sealed record EngineeringIntakeRefV1(string EngineeringRequestId, string EngineeringRevision);
public sealed record EngineeringIntakeEvidenceV1(string Authority, string Key, string Version, string ProofRef);
public sealed record EngineeringIntakeStreamV1(string Producer, string ContractVersion, string Epoch);

public sealed record EngineeringNextContextV1(EngineeringNextQueryV1 Query, string MainCaseRevision, bool NextAllowed,
    string? RequiredMode, EngineeringIntakeRefV1? Target, EngineeringIntakeEvidenceV1 PriorExecutionFence,
    EngineeringIntakeEvidenceV1 Decision, string RouteVersion, EngineeringIntakeStreamV1 CanonicalIntakeStream,
    EngineeringIntakeStreamV1 CanonicalProgressStream, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string[] BlockingCodes);

public sealed record EngineeringIntakePreviousV1(string SubmissionVersion, Guid RequestKey, string ReceiptId,
    EngineeringIntakeBindingV1 Source, string MainCaseRevision);
public sealed record EngineeringIntakeProductV1(string Owner, string Key, string Version);
public sealed record EngineeringIntakeSourceEvidenceV1(string Owner, string Key, string Version, string UsePurpose);
public sealed record EngineeringIntakeActivityV1(string SourceOwner, string SourceKind, Guid SourceId,
    string SourceVersion, string ReferenceSetVersion, string SourceDigest, string DigestKeyVersion);
public sealed record EngineeringIntakeActivityAttestationV1(EngineeringIntakeActivityV1 Identity, string Issuer,
    string AttestationVersion, string ProofRef);

public sealed record EngineeringIntakeSourceV1(string SchemaVersion, Guid TenantId, string SourceKind, Guid SourceId,
    string SourceVersion, Guid AccountId, JsonElement Requirement, EngineeringIntakeProductV1? CandidateProduct,
    string[] KnownConstraints, string[] Unknowns, EngineeringIntakeActivityV1[] ActivitySources, EngineeringIntakeSourceEvidenceV1[] EvidenceRefs);

public sealed record EngineeringIntakeRequestV1(string SchemaVersion, Guid TenantId, Guid EventId, string Producer, string Epoch,
    EngineeringCaseNaturalKeyV1 CaseNaturalKey, Guid CaseId, string SubmissionVersion, Guid RequestKey,
    EngineeringIntakePreviousV1? Previous, string Mode, EngineeringIntakeRefV1? Target, EngineeringIntakeSourceV1 Source,
    EngineeringIntakeBindingV1 SourceBinding, EngineeringIntakeActivityAttestationV1[] ActivityAttestations,
    EngineeringIntakeEvidenceV1 SourceUseEvidence, EngineeringNextContextV1 NextAuthorization, string RequestReason, DateTimeOffset CreatedAt);

public sealed record EngineeringIntakeReceiveV1(Guid RequestKey, string SourceDigest, string State,
    string Scope, string BusinessOutcome, DateTimeOffset FirstReceivedAt);

public sealed record EngineeringIntakeOriginalKeyV1(string Kind, Guid CaseId, Guid RequestKey, string SubmissionVersion, string SourceDigest);
public sealed record EngineeringIntakeOriginalQueryV1(string SchemaVersion, Guid TenantId, string Authority, string LegalEntityKey,
    EngineeringIntakeOriginalKeyV1 Request, string? PageToken, int MaxResults);
public sealed record EngineeringIntakeTrustV1(string Authority, Guid TenantId, string Producer, string ContractVersion,
    string Epoch, string CredentialSubject, string VerificationRef, DateTimeOffset VerifiedAt);
public sealed record EngineeringIntakeOriginalItemV1(string Contract, string BusinessKey, string Ordinal,
    string OriginalBodyBase64, string BodyHash, EngineeringIntakeTrustV1 ReceivedTrust);
public sealed record EngineeringIntakeMissingRangeV1(string FromOrdinal, string ToOrdinal, string StreamKey);
public sealed record EngineeringIntakeOriginalPageV1(EngineeringIntakeOriginalQueryV1 Query, string Observation,
    EngineeringIntakeOriginalItemV1[] Results, EngineeringIntakeMissingRangeV1[] Missing, string? NextToken,
    EngineeringIntakeEvidenceV1 QueryEvidence, DateTimeOffset AsOf, bool NoEffectProven);

internal sealed record EngineeringIntakeResultSourceV1(EngineeringCaseNaturalKeyV1 CaseNaturalKey, Guid CaseId,
    string SubmissionVersion, Guid RequestKey, EngineeringIntakeBindingV1 Source);
internal sealed record EngineeringIntakeSequenceV1(string Ordinal, string? PreviousOrdinal, string? PreviousBusinessDigest);
internal sealed record EngineeringIntakeResultV1(string SchemaVersion, Guid TenantId, string EventId, string Producer,
    string Epoch, EngineeringIntakeResultSourceV1 Source, EngineeringIntakeSequenceV1 Sequence, string ReceiptId,
    string Outcome, EngineeringIntakeRefV1? Engineering, string[] MissingCodes, string? SafeReasonCode,
    EngineeringIntakeEvidenceV1 OwnerEvidence, DateTimeOffset OwnerRecordedAt, string Kind, object? Correction);

public sealed record EngineeringIntakeCallerV1(Guid TenantId, string Issuer, string ClientId, Guid TokenId, DateTimeOffset ExpiresAt);
public sealed record EngineeringIntakeScopeV1(Guid TenantId, string Authority, string LegalEntityKey,
    Guid? OpportunityId, Guid? CaseId = null);
public enum EngineeringIntakeUseV1 { Prepare, Receive, ReadOriginal }

/// <summary>绑定同一业务租户、服务主体、受控路由和已核实写入方目录代次。</summary>
public sealed record EngineeringIntakeRouteV1(EngineeringIntakeScopeV1 Scope, Guid MainActorId, Guid? DepartmentId,
    string RouteVersion, Guid DirectoryGeneration, string DirectoryEvidenceKey, string RequestEpoch,
    EngineeringIntakeStreamV1 IntakeStream, EngineeringIntakeStreamV1 ProgressStream, string ResultCredentialSubject,
    EngineeringIntakeOwnerSignerV1 OwnerSigner, DateTimeOffset ValidUntil);

public sealed record EngineeringIntakeSourceUseV1(EngineeringIntakeProofKeyV1 DigestKey, DateTimeOffset ValidUntil,
    string[] MissingRequiredInputs);
public sealed record EngineeringIntakeSourceLocationV1(EngineeringCaseNaturalKeyV1 NaturalKey, Guid CaseId, Guid RequestKey,
    string SubmissionVersion, EngineeringIntakeBindingV1 Binding, string OriginalBodyHash);

/// <summary>由实际源 Owner 注册的当前权限参加者。缺失时拒绝签发证明或产生业务效果。</summary>
public interface IEngineeringIntakeAuthorityV1
{
    // 在给定的 Main PostgreSQL 事务内持有服务、机会或请求指定的 Case 范围和 SourceUse 共同门，先于 Case 锁。
    // 写入方闭包须含历史执行和运输别名。有效期代表门可保持的承诺，普通 GET 或 TTL 读票不能参加。
    Task<EngineeringIntakeRouteV1?> HoldRouteAsync(CP6Context db, EngineeringIntakeCallerV1 caller, EngineeringIntakeScopeV1 scope,
        EngineeringIntakeUseV1 use, CancellationToken ct);

    // 在已持有的共同门下验证当前 OPP 原版、Account、精确正文和本次用途，及声明的全部 ACT/File/Product 证据。
    Task<EngineeringIntakeSourceUseV1?> RequireSourceUseAsync(CP6Context db, EngineeringIntakeRouteV1 route,
        EngineeringIntakeRequestV1 request, string originalBodyHash, CancellationToken ct);

    // 历史披露保持准确原请求坐标的当前对象、字段和原件用途权限。服务身份与 Main 主体同时校验。
    // service 为空时 actor 来自原生 REQ 用户入口。
    Task<DateTimeOffset?> HoldOriginalReadAsync(CP6Context db, Guid actor, EngineeringIntakeCallerV1? service, EngineeringIntakeSourceLocationV1 original,
        CancellationToken ct);
}

/// <summary>原来源用途 HMAC 密钥。密钥本身不进入返回值、日志或业务记录。</summary>
public sealed class EngineeringIntakeProofKeyV1
{
    private readonly byte[] secret;

    public EngineeringIntakeProofKeyV1(string version, byte[] key)
    {
        if (key.Length < 32) throw new ArgumentException("工程受理证明密钥长度不足。", nameof(key));
        Version = CommercialWireV1.RequireKey(version);
        secret = key.ToArray();
    }

    public string Version { get; }

    internal string Sign<T>(string domain, T value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(secret, EngineeringIntakeOwnerSignerV1.Data(domain, value)));

    internal static bool Equal(string? actual, string expected) =>
        CommercialWireV1.IsDigest(actual) && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual!), Convert.FromHexString(expected));
}

/// <summary>Main 签发者使用已登记的 P-256 密钥，消费方可用准确版本的公钥核验证明。</summary>
public sealed class EngineeringIntakeOwnerSignerV1
{
    private readonly ECDsa signer;
    public string Version { get; }

    public EngineeringIntakeOwnerSignerV1(string version, ECDsa signer)
    {
        if (signer.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            throw new ArgumentException("工程受理签名使用 P-256。", nameof(signer));
        Version = CommercialWireV1.RequireKey(version);
        this.signer = signer;
    }

    internal static byte[] Data<T>(string domain, T value) =>
        [.. Encoding.UTF8.GetBytes(domain + "\n"), .. CommercialCanonicalV1.Data(value)];

    internal string Sign<T>(string domain, T value)
    {
        var data = Data(domain, value);
        lock (signer)
            return WebEncoders.Base64UrlEncode(signer.SignData(data, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    internal bool Verify<T>(string domain, T value, string proof)
    {
        if (proof.Length != 86 || !proof.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        var signature = WebEncoders.Base64UrlDecode(proof);
        if (signature.Length != 64 || WebEncoders.Base64UrlEncode(signature) != proof) return false;
        var data = Data(domain, value);
        lock (signer)
            return signer.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
