using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;
namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalUserWithdrawV2(Guid CommandId,string Reason);
public sealed record ApprovalWithdrawalActorV2(string Kind,Guid WithdrawCommandId,Guid RequesterId,Guid ActualHandlerId,
    Guid? OnBehalfOfId,Guid? GrantId,string? GrantRevision,ApprovalRefV2 OwnerWithdrawalRef,ApprovalRefV2 WithdrawAuthorizationRef,DateTimeOffset OccurredAt);
public sealed record ApprovalWithdrawalFactV2(string Schema,ApprovalScopeV2 Scope,string Owner,ApprovalRefV2 OwnerWithdrawalRef,
    Guid OwnerCommandId,ApprovalDigestV2 UserRequestDigest,Guid SubmissionId,ApprovalSubjectV2 Subject,ApprovalRefV2 PolicyRef,
    string BusinessBeforeRowVersion,string BusinessAfterRowVersion,DateTimeOffset WithdrawnAt,ApprovalWithdrawalActorV2 Actor,string Reason);
public sealed record ApprovalWithdrawCommandV2(string Schema,ApprovalScopeV2 Scope,string ProducerOwner,Guid CommandId,
    Guid OwnerOutboxId,Guid SubmissionId,ApprovalWithdrawalFactV2 Fact,ApprovalDigestV2 FactDigest);
public sealed record ApprovalWithdrawContextV2(Guid SubmissionId,ApprovalSubjectV2 Subject,ApprovalRefV2 PolicyRef,Guid? InstanceId,
    string WorkflowState,string SubmissionRowVersion,bool CanRelayWithdraw,string[] DisabledReasons);
public sealed record ApprovalWithdrawAckV2(Guid CommandId,Guid OwnerCommandId,Guid SubmissionId,ApprovalRefV2 OwnerWithdrawalRef,
    ApprovalDigestV2 FactDigest,string State,Guid? DecisionId,ApprovalRefV2? DecisionRef,string? ErrorCode,string QueryRef);
public sealed record ApprovalWithdrawResultV2(int Status,ApprovalWithdrawAckV2 Ack);
public static class ApprovalWithdrawalResponseV2
{
    public static byte[] Bytes(object value)=>value is ApprovalWithdrawAckV2 or ApprovalWithdrawContextV2
        ?ApprovalCanonicalV2.Bytes(value):throw new ArgumentException("Unsupported withdrawal response.",nameof(value));
}

internal static class ApprovalWithdrawalWireV2
{
    internal sealed record Parsed(ApprovalWithdrawCommandV2 Value,byte[] Canonical,string Hash);
    internal static void RowVersion(string value)
    {if(Convert.FromBase64String(value).Length!=8 || Convert.ToBase64String(Convert.FromBase64String(value))!=value)throw new JsonException();}
    private static void Instant(JsonElement e)
    {if(!DateTimeOffset.TryParseExact(e.GetString(),"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _))throw new JsonException();}
    internal static Parsed Parse(byte[] raw)
    {
        if(raw.Length>1048576)throw Error("WITHDRAW_SHAPE_INVALID",422);
        try
        {
            _=new UTF8Encoding(false,true).GetString(raw);using var doc=WorkflowCommandJsonV1.Parse(raw);var e=doc.RootElement;
            Shape(e,"schema","scope","producerOwner","commandId","ownerOutboxId","submissionId","fact","factDigest");
            var f=e.GetProperty("fact");Shape(f,"schema","scope","owner","ownerWithdrawalRef","ownerCommandId","userRequestDigest","submissionId","subject","policyRef","businessBeforeRowVersion","businessAfterRowVersion","withdrawnAt","actor","reason");
            foreach(var s in new[]{e.GetProperty("scope"),f.GetProperty("scope")}){Shape(s,"environmentId","tenantId");Key(s.GetProperty("environmentId"));Id(s.GetProperty("tenantId"));}
            foreach(var k in new[]{"commandId","ownerOutboxId","submissionId"})Id(e.GetProperty(k));
            foreach(var k in new[]{"ownerCommandId","submissionId"})Id(f.GetProperty(k));
            Ref(f.GetProperty("ownerWithdrawalRef"));Ref(f.GetProperty("policyRef"));Digest(f.GetProperty("userRequestDigest"));Digest(e.GetProperty("factDigest"));
            Key(e.GetProperty("producerOwner"));Key(f.GetProperty("owner"));
            var s1=f.GetProperty("subject");Shape(s1,"businessOwner","bizType","subjectKind","subjectId","businessRevision","contentDigest");
            foreach(var k in s1.EnumerateObject())if(k.Name=="contentDigest")Digest(k.Value);else Key(k.Value,k.Name=="businessRevision"?128:100);
            RowVersion(f.GetProperty("businessBeforeRowVersion").GetString()!);RowVersion(f.GetProperty("businessAfterRowVersion").GetString()!);Instant(f.GetProperty("withdrawnAt"));
            var a=f.GetProperty("actor");Shape(a,"kind","withdrawCommandId","requesterId","actualHandlerId","onBehalfOfId","grantId","grantRevision","ownerWithdrawalRef","withdrawAuthorizationRef","occurredAt");
            foreach(var k in new[]{"withdrawCommandId","requesterId","actualHandlerId"})Id(a.GetProperty(k));Id(a.GetProperty("onBehalfOfId"),true);Id(a.GetProperty("grantId"),true);
            Ref(a.GetProperty("ownerWithdrawalRef"));Ref(a.GetProperty("withdrawAuthorizationRef"));Instant(a.GetProperty("occurredAt"));
            var v=e.Deserialize<ApprovalWithdrawCommandV2>(ApprovalBindingOwnerV2.Json)!;var fact=v.Fact;var actor=fact.Actor;
            if(v.Schema!="oa.app.withdraw/2.0" || fact.Schema!="oa.app.withdrawal-fact/1.0" || v.Scope!=fact.Scope || v.ProducerOwner!=fact.Owner
                || fact.Subject.BusinessOwner!=fact.Owner || v.SubmissionId!=fact.SubmissionId || fact.OwnerWithdrawalRef.Version!="1"
                || actor.Kind!="SUBMITTER_WITHDRAWAL" || actor.WithdrawCommandId!=fact.OwnerCommandId || actor.OwnerWithdrawalRef!=fact.OwnerWithdrawalRef
                || actor.OccurredAt!=fact.WithdrawnAt || fact.BusinessBeforeRowVersion==fact.BusinessAfterRowVersion
                || string.IsNullOrWhiteSpace(fact.Reason) || fact.Reason.EnumerateRunes().Count()>500)throw Error("WITHDRAW_FACT_MISMATCH",422);
            if(actor.RequesterId==actor.ActualHandlerId)
            {if(actor.OnBehalfOfId!=null || actor.GrantId!=null || actor.GrantRevision!=null)throw new JsonException();}
            else if(actor.OnBehalfOfId!=actor.RequesterId || actor.GrantId==null || string.IsNullOrEmpty(actor.GrantRevision))throw new JsonException();
            else Key(a.GetProperty("grantRevision"));
            if(v.FactDigest!=ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(fact)))throw Error("WITHDRAW_FACT_DIGEST_MISMATCH",422);
            if(fact.UserRequestDigest!=ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(new ApprovalUserWithdrawV2(fact.OwnerCommandId,fact.Reason))))throw Error("WITHDRAW_FACT_MISMATCH",422);
            var bytes=ApprovalCanonicalV2.Bytes(e);return new(v,bytes,ApprovalCanonicalV2.Hash(bytes));
        }
        catch(Exception e)when(e is JsonException or DecoderFallbackException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        {if(e is ScopeAuthorizationExceptionV1)throw;throw Error("WITHDRAW_SHAPE_INVALID",422);}
    }
}
