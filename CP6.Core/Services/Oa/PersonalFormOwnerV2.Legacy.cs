using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

// Metadata is supplied by the caller's previous context/preview. No server-selected predecessor.
public sealed record LegacyPersonalFormHeadersV1(string Key,string? FormVersion=null,string? Activation=null,
    string? Binding=null,string? FlowVersion=null,string? FlowActivation=null,string? Preview=null,
    string? PreviewDigest=null,string? IfMatch=null,string? DispositionRequest=null,string? DispositionReceipt=null);

public sealed partial class PersonalFormOwnerV2
{
    internal sealed record LegacyCommandInfo(string Key,string Hash,byte[] Raw,int? TargetVersion=null);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record LegacyCreate([property:JsonRequired] JsonElement Data,string? Title);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record LegacySave([property:JsonRequired] JsonElement Data,string? Title,string? RowVersion);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record LegacySubmit(string? RowVersion);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record LegacyDirect([property:JsonRequired] JsonElement Data,Guid? DraftId);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record LegacyRebase(int TargetVersion,bool ConfirmRemovedValues,string? RowVersion);
    private sealed record LegacyMapping(string Key,string Hash);
    private static readonly JsonSerializerOptions LegacyJson=new(JsonSerializerDefaults.Web);

    public static string LegacyCommandId(string key)
    {
        if(string.IsNullOrWhiteSpace(key)||key.Length>100||key.Any(c=>!(char.IsLetterOrDigit(c)||c is '-' or '_' or '.')))
            throw Error("LEGACY_COMMAND_KEY_INVALID",400);
        return "legacy1."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }
    public static string LegacyDraftVersion(string? value)
    {
        try
        {
            var bytes=Convert.FromBase64String(value??"");
            if(bytes.Length==16 && Convert.ToBase64String(bytes)==value && new Guid(bytes)!=Guid.Empty)return new Guid(bytes).ToString("D");
        }
        catch(FormatException) { }
        throw Error("DRAFT_VERSION_REQUIRED",409);
    }
    private static Guid LegacyPin(string? text)
        =>Guid.TryParseExact(text,"D",out var id)&&id!=Guid.Empty?id:throw Error("FORM_CONTEXT_REQUIRED",409);
    private static string? LegacyNullable(string? value)
        =>value==null?throw Error("FORM_CONTEXT_REQUIRED",409):value=="null"?null:value;
    private static PersonalFormDraftSubmitV2 LegacyContext(LegacyPersonalFormHeadersV1 h)
    {
        var flow=LegacyNullable(h.FlowVersion);
        return new("DRAFT",LegacyPin(h.FormVersion),h.Activation??throw Error("FORM_CONTEXT_REQUIRED",409),
            LegacyNullable(h.Binding),flow==null?null:LegacyPin(flow),LegacyNullable(h.FlowActivation));
    }
    private static T LegacyBody<T>(JsonElement value)
    {
        // The original ASP.NET DTOs accept case-insensitive property names. Preserve
        // that contract, rejecting ambiguous aliases before typed normalization.
        if(value.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=value.EnumerateObject().Count())
            throw Error("LEGACY_BODY_INVALID",400);
        try{return value.Deserialize<T>(LegacyJson)??throw new JsonException();}
        catch(JsonException){throw Error("LEGACY_BODY_INVALID",400);}
    }
    private static Command<T> LegacyCommand<T>(T body,string? expected,LegacyPersonalFormHeadersV1 h,object original,byte[] raw,int? target=null)
    {
        var id=LegacyCommandId(h.Key);var element=JsonSerializer.SerializeToElement(body,Json);
        // This is the exact adapter-generated native intent. The original legacy bytes and
        // its own digest are persisted separately, never substituted for the Owner digest.
        var wire=Encoding.UTF8.GetBytes("{\"commandId\":"+WorkflowCommandJsonV1.Quote(id)+",\"expectedVersion\":"+
            (expected==null?"null":WorkflowCommandJsonV1.Quote(expected))+",\"body\":"+WorkflowCommandJsonV1.Canonical(element)+
            ",\"clientCorrelationId\":"+WorkflowCommandJsonV1.Quote(h.Key)+"}");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("FORM-LEGACY-ADAPTER/1\n"+WorkflowCommandJsonV1.Canonical(JsonSerializer.SerializeToElement(original,Json))))).ToLowerInvariant();
        return new(id,expected,body,WorkflowCommandJsonV1.Digest(element,expected),wire,h.Key,new(h.Key,hash,raw.ToArray(),target));
    }
    public async Task<PersonalFormResultV2> LegacyExecuteAsync(string operation,string resource,byte[] raw,LegacyPersonalFormHeadersV1 headers,CancellationToken ct=default)
    {
        _=LegacyCommandId(headers.Key);
        if(raw.Length>DataRequestMaxBytes)throw Error("JSON_SIZE_LIMIT",400);
        StrictJsonDocument document;
        // The legacy DTO has one containing object around data (whose own limit is depth 8).
        try{document=WorkflowCommandJsonV1.Parse(raw,DataRequestMaxBytes,9);}
        catch(JsonException){throw Error("LEGACY_BODY_INVALID",400);}
        using(document)
        {
            var body=document.RootElement;
            if(body.ValueKind!=JsonValueKind.Object)throw Error("LEGACY_BODY_INVALID",400);
            if(operation==Create)
            {
                var input=LegacyBody<LegacyCreate>(body);
                return await CreateDraft(resource,LegacyCommand(new PersonalFormCreateV2(LegacyPin(headers.FormVersion),input.Data,input.Title),null,headers,input,raw),ct);
            }
            if(operation==Direct)
            {
                var input=LegacyBody<LegacyDirect>(body);
                if(input.DraftId!=null)throw Error("LEGACY_DRAFT_SUBMIT_ROUTE_REQUIRED",400);
                var c=LegacyContext(headers);
                return await SubmitForm(resource,null,LegacyCommand(new PersonalFormDirectV2("DIRECT",c.ExpectedFormVersionId,c.ExpectedActivationRevision,
                    c.ExpectedBindingRevision,c.ExpectedFlowVersionId,c.ExpectedFlowActivationRevision,input.Data),null,headers,input,raw),ct);
            }
            if(!Guid.TryParseExact(resource,"D",out var draft)||draft==Guid.Empty)throw Error("DRAFT_NOT_FOUND",404);
            if(operation==Save)
            {
                var input=LegacyBody<LegacySave>(body);
                return await SaveDraft(draft,LegacyCommand(new PersonalFormSaveV2(input.Data,input.Title),LegacyDraftVersion(input.RowVersion),headers,input,raw),ct);
            }
            if(operation==Submit)
            {
                var input=LegacyBody<LegacySubmit>(body);
                return await SubmitForm(null,draft,LegacyCommand(LegacyContext(headers),LegacyDraftVersion(input.RowVersion),headers,input,raw),ct);
            }
            if(operation==Delete)
            {
                if(body.EnumerateObject().Any())throw Error("LEGACY_BODY_INVALID",400);
                var token=headers.IfMatch;
                if(token?.Length>=2 && token[0]=='"' && token[^1]=='"')token=token[1..^1];
                return await ChangeDeleted(draft,LegacyCommand(new PersonalFormReasonV2("Legacy DELETE request"),LegacyDraftVersion(token),headers,body,raw),false,ct);
            }
            if(operation==Rebase)
            {
                var input=LegacyBody<LegacyRebase>(body);
                if(input.TargetVersion<1||headers.PreviewDigest?.Length!=64)throw Error("REBASE_CONTEXT_REQUIRED",409);
                var disposition=headers.DispositionRequest==null&&headers.DispositionReceipt==null?null:
                    new FormDispositionRefV1(LegacyPin(headers.DispositionRequest),LegacyPin(headers.DispositionReceipt));
                var value=new PersonalFormRebaseInputV2(LegacyPin(headers.Preview),headers.PreviewDigest,LegacyPin(headers.FormVersion),input.ConfirmRemovedValues,disposition);
                return await RebaseDraft(draft,LegacyCommand(value,LegacyDraftVersion(input.RowVersion),headers,input,raw,input.TargetVersion),ct);
            }
            throw Error("COMMAND_SCOPE_INVALID",400);
        }
    }
    private async Task MatchLegacy<T>(StoredCommand old,Command<T> command,CancellationToken ct)
    {
        Match(old,command.Digest);
        if(command.Legacy is not {} input)return;
        var mapping=await db.Database.SqlQuery<LegacyMapping>($"""
            SELECT legacy_key AS "Key",request_hash AS "Hash" FROM cp6_personal_form_v2.legacy_commands WHERE command_id={old.Id}
            """).SingleOrDefaultAsync(ct);
        if(mapping==null||mapping.Key!=input.Key||mapping.Hash!=input.Hash)throw Error("COMMAND_BODY_CONFLICT",409);
    }
    private async Task SaveLegacyCommand(Guid id,Guid actor,string operation,string resource,LegacyCommandInfo input,CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_personal_form_v2.legacy_adapter_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("FORM_LEGACY_WRITER_COVERAGE_UNAVAILABLE");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_personal_form_v2.legacy_commands(command_id,tenant_id,environment_id,actor_id,operation,resource_key,
              legacy_key,mapping_version,request_hash,raw_request)
            VALUES({id},{db.CurrentTenantId},{fence.EnvironmentId},{actor},{operation},{resource},{input.Key},'FORM-LEGACY-ADAPTER/1',{input.Hash},{input.Raw})
            """,ct);
    }
    public async Task<PersonalFormResultV2> LegacyOriginalAsync(string operation,string resource,string key,CancellationToken ct=default)
    {
        if(operation is not(Create or Save or Delete or Direct or Submit or Rebase))throw Error("COMMAND_SCOPE_INVALID",400);
        resource=CommandResource(operation,resource);var id=LegacyCommandId(key);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);var old=await Original(actor.UserId,operation,resource,id,ct);
        PersonalFormResultV2 result;
        if(old==null)
        {
            if(operation is Direct or Submit && await db.Wf_FormDatas.AsNoTracking().AnyAsync(x=>x.SubmissionKey==key,ct))
                throw Error("LEGACY_SUBMISSION_MAPPING_REQUIRED",409);
            result=new(id,"NOT_OBSERVED",null,null,null,null,key);
        }
        else
        {
            if(!await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_personal_form_v2.legacy_commands WHERE command_id={old.Id} AND legacy_key={key}) AS \"Value\"").SingleAsync(ct))
                throw Error("LEGACY_COMMAND_MAPPING_REQUIRED",409);
            result=await Disclose(old,actor,ct);
        }
        await Final(actor,lease,ct,false);return result;
    }
    private sealed record LegacyRebaseEvidence(string Audit,string Before);
    public async Task<DraftRebaseResult> LegacyRebaseResultAsync(Guid draft,string key,CancellationToken ct=default)
    {
        var command=LegacyCommandId(key);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);
        var old=await Original(actor.UserId,Rebase,draft.ToString("D"),command,ct)??throw Error("COMMAND_NOT_OBSERVED",404);
        var evidence=await db.Database.SqlQuery<LegacyRebaseEvidence>($"""
            SELECT c.audit_json AS "Audit",h.before_json AS "Before" FROM cp6_personal_form_v2.commands c
            JOIN cp6_personal_form_v2.history h ON h.command_id=c.id
            JOIN cp6_personal_form_v2.legacy_commands l ON l.command_id=c.id
            WHERE c.id={old.Id} AND l.legacy_key={key}
            """).SingleOrDefaultAsync(ct)??throw Error("LEGACY_COMMAND_MAPPING_REQUIRED",409);
        var disclosed=await Disclose(old,actor,ct);var view=disclosed.Result!.Value.Deserialize<PersonalFormDraftV2>(Json)!;
        using var before=JsonDocument.Parse(evidence.Before);using var audit=JsonDocument.Parse(evidence.Audit);
        var row=await Owned(draft,actor.UserId,ct);
        var sourceMask=await Mask(actor,row.Head,await Publication(row.Head,before.RootElement.GetProperty("form_version").GetGuid(),ct),ct);
        var removed=audit.RootElement.GetProperty("changes").Deserialize<PersonalFormRebaseChangeV2[]>(Json)!
            .Where(x=>x.Kind is "REMOVED" or "TYPE_CHANGED" && sourceMask.GetValueOrDefault(x.Path.Split('[','.')[0],"hidden")!="hidden")
            .Select(x=>x.Path).Distinct().ToArray();
        // The old result has no field-mask/diagnostic contract. Preserve failure presence
        // without leaking historical hidden paths through its free-text validation strings.
        string[] errors=audit.RootElement.GetProperty("errors").GetArrayLength()==0?[]:["FORM_VALIDATION_FAILED"];
        var result=new DraftRebaseResult(draft,view.FormVersion,view.DataJson,removed,errors,Guid.Parse(view.RowVersion).ToByteArray());
        await Final(actor,lease,ct,false);return result;
    }
}
