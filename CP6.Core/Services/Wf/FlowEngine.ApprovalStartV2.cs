using System.Text.Json;
using System.Security.Cryptography;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    internal const string ApprovalRuntimeV2 = "oa.app/2.0";
    private bool _approvalStartV2;
    private int _approvalTransitions;

    // Stable across retries, but disjoint across Owner/tenant/environment and legacy UUIDs.
    // The durable engineStartId remains the original SubmissionId in its contractual Scope.
    internal static Guid ApprovalInstanceId(ApprovalSubmitCommandV2 command)
    {
        var bytes = SHA256.HashData(ApprovalCanonicalV2.Bytes(new
        { contract = "oa.app.instance-id/2.0", command.Scope, owner = command.Subject.BusinessOwner, command.SubmissionId }));
        bytes[6] = (byte)((bytes[6] & 15) | 128); bytes[8] = (byte)((bytes[8] & 63) | 128);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }

    // Called by TxE only. The caller owns the explicit native transaction and its single commit.
    // Reuse token/stage/task algorithms, with no notifier, dispatcher or service executor side effect.
    internal async Task<Wf_FlowInstance> StageApprovalStartV2(FlowPublicationV2 publication,
        ApprovalSubmitCommandV2 command, string snapshot, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql() || _db.Database.CurrentTransaction == null || _db.ChangeTracker.HasChanges())
            throw new ScopeAuthorizationExceptionV1("APP_START_TRANSACTION_REQUIRED");
        _approvalStartV2 = true;
        var schema = Deserialize(publication.Fields["SchemaJson"]!);
        var first = FirstNode(schema) ?? throw new ScopeAuthorizationExceptionV1("FLOW_VERSION_UNAVAILABLE", 422);
        var instance = new Wf_FlowInstance
        {
            Id = ApprovalInstanceId(command), FlowDefVersionId = publication.VersionId, FlowKey = publication.Key,
            // The authoritative v2 Subject is starts -> immutable submission. Legacy callback
            // keys have narrower lengths, collation and latest-object semantics; do not use them.
            BizType = null, BizId = null, StarterId = command.StarterId,
            VarsJson = snapshot, CurrentNode = first.Id, Status = FlowInstanceStatus.Running, Creator = ApprovalRuntimeV2
        };
        _db.Wf_FlowInstances.Add(instance);
        AddHistory(instance.Id, first.Id, command.StarterId, "submit", "Accepted original Owner submission");
        var root = SpawnToken(instance, first);
        await EnterNodeAsync(instance, schema, first, root);
        if (instance.Status == FlowInstanceStatus.Approved)
            throw new ScopeAuthorizationExceptionV1("APP_EXPLICIT_DECISION_REQUIRED", 409);
        ct.ThrowIfCancellationRequested();
        return instance;
    }

    internal static void RequireLegacyRuntime(Wf_FlowInstance instance)
    {
        if (instance.Creator == ApprovalRuntimeV2)
            throw new ScopeAuthorizationExceptionV1("APP_DECISION_OWNER_REQUIRED", 409);
    }

    internal bool EvaluateCondition(string? expression, string vars)
        => _approvalStartV2 ? ExactCondition(expression, vars) : ExpressionEvaluator.Evaluate(expression, vars);

    internal static bool ExactCondition(string? expression, string vars)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        using var doc = JsonDocument.Parse(vars);
        var fields = new List<string>();
        void Walk(JsonElement value, string prefix)
        {
            foreach (var p in value.EnumerateObject())
            {
                var path = prefix + p.Name;
                if (p.Value.ValueKind == JsonValueKind.Object) Walk(p.Value, path + ".");
                else fields.Add(path);
            }
        }
        Walk(doc.RootElement, "");
        return ApprovalExpressionV2.Evaluate(expression, doc.RootElement, fields.ToArray());
    }
}
