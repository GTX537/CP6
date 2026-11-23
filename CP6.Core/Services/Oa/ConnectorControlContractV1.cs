using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;

namespace CP6.Core.Services.Oa;

// 注册来自安全 Owner。普通配置命令只接受 profileId，不能直接写提供方采用、目的地址或秘密。
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnProfileV1(
    string? SourceKind = null, string? Implementation = null, string? ImplementationVersion = null,
    string? DestinationPolicy = null, string? EndpointProfile = null, string? Operation = null,
    string? InputSchema = null, string? OutputSchema = null, string? ParameterMapping = null, string? ResponseMapping = null,
    string? EffectClass = null, int? MaxCallDurationSec = null, string? LeasePolicy = null, string? RetryPolicy = null,
    string? IdempotencyContract = null, string? ResultQueryContract = null, string? DisclosurePolicy = null,
    string? CredentialLocator = null, string? CredentialGeneration = null, string? AdmissionBasis = null,
    string? Registration = null, string? Schema = null, string? VarsSchema = null, string? RateLimitPolicy = null,
    string? SourceModule = null, string? HookName = null, string? TimeZone = null, string? TimeRules = null,
    string? Calendar = null, string? StarterAuthority = null, Guid? StarterId = null, Guid? LegacyCredentialOwnerRef = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnSelectionV1(string Name, string SourceKind, Guid ProfileId, string? FlowKey = null,
    Guid? FlowVersionId = null, Guid? LegacyRootId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnConfigV1(string? Cron = null, JsonElement? Vars = null, Dictionary<string,string>? VarsMap = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnDraftV1(ConnSelectionV1 Selection, string DisplayName, ConnConfigV1? Config = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnPreviewInputV1(Guid VersionId, string Action);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnReviewInputV1(Guid VersionId, string Kind, Guid? ImpactPreviewRef = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnDecideInputV1(Guid SubjectRef, string Decision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConnControlInputV1(Guid VersionId, Guid? ImpactPreviewRef = null, Guid? DecisionRef = null, string? Reason = null);
public sealed record ConnectorPinV1(string SourceKind, Guid RootId, Guid VersionId, string Operation);
public interface ITriggerRegistrationAuthorityV1
{
    bool Supports(Guid tenant,Guid environment,Guid registration,string kind,ConnProfileV1 version,string evidenceRef);
}

public sealed class OaConnectorControlOptionsV1
{
    public bool AllowSimulatedParticipants { get; set; }
}
internal sealed record ConnWireV1(string Id, string Expected, JsonElement Body, string Digest)
{
    internal T Read<T>() where T : class
    {
        try { return Body.Deserialize<T>(ConnectorVersionControlService.Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new ScopeAuthorizationExceptionV1("COMMAND_SHAPE_INVALID",400); }
    }

    internal static ConnWireV1 Parse(byte[] bytes)
    {
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(bytes);
            var envelope = doc.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object ||
                envelope.EnumerateObject().Any(p => p.Name is not ("commandId" or "expectedVersion" or "body" or "clientCorrelationId")))
                throw new JsonException();
            var id = envelope.GetProperty("commandId").GetString();
            if (id is null || !Regex.IsMatch(id, @"\A[A-Za-z0-9_.-]{1,100}\z")) throw new JsonException();
            if (!envelope.TryGetProperty("expectedVersion",out var head) || string.IsNullOrEmpty(head.GetString()))
                throw new ScopeAuthorizationExceptionV1("PRECONDITION_REQUIRED",428);
            var body=envelope.GetProperty("body");
            if(body.ValueKind != JsonValueKind.Object || envelope.TryGetProperty("clientCorrelationId",out var correlation) && correlation.GetString()?.Length>100)
                throw new JsonException();
            return new(id,head.GetString()!,body.Clone(),WorkflowCommandJsonV1.Digest(body,head.GetString()));
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException || e is InvalidOperationException and not ScopeAuthorizationExceptionV1)
        { throw new ScopeAuthorizationExceptionV1("COMMAND_SHAPE_INVALID",400); }
    }
}
