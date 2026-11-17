using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

/// <summary>历史撤销事件按已批准的原 issuer 验证；不重写载荷，也不把任意 issuer 当成当前来源。</summary>
public sealed class IdentitySourceOutboxValidatorV1(IDbContextFactory<CP6Context> factory, CrmIdentityRuntime runtime) : ICp6OutboxEnvelopeValidator
{
    public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope)
    {
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(envelope.Payload); }
        catch (JsonException) { return Cp6MessageValidationResult.Invalid("C02_IDENTITY_CONTRACT_INVALID"); }
        using var json = parsed;
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return Cp6MessageValidationResult.Invalid("C02_IDENTITY_CONTRACT_INVALID");
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || !IdentityEventContracts.IsTokenRevoked(type.GetString()))
            return runtime.Validator.Validate(envelope);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("issuer", out var issuerElement) || issuerElement.ValueKind != JsonValueKind.String)
            return Cp6MessageValidationResult.Invalid("C02_IDENTITY_CONTRACT_INVALID");
        var issuer = issuerElement.GetString()!;
        using var db = factory.CreateDbContext();
        if (!db.Database.IsNpgsql() || !db.Database.SqlQueryRaw<bool>("""
                SELECT to_regprocedure('cp6_identity_source_v1.read_hash(uuid)') IS NOT NULL AS "Value"
                """).Single()) return runtime.Validator.Validate(envelope);
        // 同步接口由 Outbox 调度器定义。每次以已提交的批准事实查询，避免缓存刷新与领取新事件之间的竞态。
        var authorized = db.Database.SqlQuery<bool>($"""
            SELECT (EXISTS(SELECT FROM cp6_identity_source_v1.registrations WHERE tenant_id={envelope.TenantId}
                    AND convert_from(binding_bytes,'UTF8')::jsonb->>'issuer'={issuer})
                OR EXISTS(SELECT FROM cp6_identity_source_v1.publications WHERE tenant_id={envelope.TenantId}
                    AND (convert_from(result_bytes,'UTF8')::jsonb->'previous'->>'issuer'={issuer}
                      OR convert_from(result_bytes,'UTF8')::jsonb->'current'->>'issuer'={issuer}))
                OR ({issuer}={runtime.Options.Issuer} AND NOT EXISTS(
                    SELECT FROM cp6_identity_source_v1.registrations WHERE tenant_id={envelope.TenantId}))) AS "Value"
            """).Single();
        return authorized ? runtime.Validator.ForIssuer(issuer).Validate(envelope)
            : Cp6MessageValidationResult.Invalid("C02_SOURCE_NOT_APPROVED");
    }
}
