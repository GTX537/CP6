using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>OA-APP Tx B only. Configuration is not a submission, engine start, decision or business application.</summary>
public sealed class ApprovalBindingOwnerV2
{
    public const string Resource = "approval-binding", Menu = "oa-flow-admin", ReadAction = "oa.app.binding.read", ConfigureAction = "oa.app.binding.configure";
    public static readonly string[] Fields = ["Configuration"];
    internal static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
        json.Converters.Add(new InstantConverter()); return json;
    }
    private sealed class InstantConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => DateTimeOffset.Parse(r.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter w, DateTimeOffset v, JsonSerializerOptions o) => w.WriteStringValue(v.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
    private readonly CP6Context db;
    private readonly IHttpContextAccessor http;
    private readonly ScopeAuthorizationFenceV1 fence;
    private readonly Func<CancellationToken, Task<Guid>> currentActor;
    private readonly FlowAuthoringOwnerV2 flows;
    private readonly Func<AuthorizationCatalogSnapshotV1> catalog;
    private DateTime? expiry;
    public ApprovalBindingOwnerV2(CP6Context db, IHttpContextAccessor http, ScopeAuthorizationFenceV1 fence,
        Func<CancellationToken, Task<Guid>> currentActor, FlowAuthoringOwnerV2 flows)
        : this(db, http, fence, currentActor, flows, AuthorizationCatalogV1.Capture) { }
    internal ApprovalBindingOwnerV2(CP6Context db, IHttpContextAccessor http, ScopeAuthorizationFenceV1 fence,
        Func<CancellationToken, Task<Guid>> currentActor, FlowAuthoringOwnerV2 flows, Func<AuthorizationCatalogSnapshotV1> catalog)
    { this.db=db; this.http=http; this.fence=fence; this.currentActor=currentActor; this.flows=flows; this.catalog=catalog; }
    private static ScopeAuthorizationExceptionV1 Error(string code, int status=503) => new(code,status);
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges() || System.Transactions.Transaction.Current != null)
            throw Error("AUTHORITY_UNAVAILABLE");
        _=fence.EnvironmentId;
    }
    private async Task Root(CancellationToken ct)
    { await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'",ct); await AuthenticationCompletionGateV1.AcquireAsync(db,ct); }
    private async Task<UserPermissionContext> Actor(CancellationToken ct)
    {
        var id=await currentActor(ct);
        if(id!=CurrentPermissionContext.RequireCurrentUser(db,http) || http.HttpContext!.User.HasClaim(x=>x.Type=="impersonator_id")) throw Error("AUTHENTICATION_REQUIRED",401);
        var tenant=await db.Sys_Tenants.AsNoTracking().Where(x=>x.Id==db.CurrentTenantId && x.Enable).Select(x=>new{x.ExpireDate}).SingleOrDefaultAsync(ct);
        if(tenant==null || tenant.ExpireDate<=DateTime.Now || !await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==id && x.Enable && (x.LockedUntil==null || x.LockedUntil<=DateTime.Now),ct)) throw Error("AUTHENTICATION_REQUIRED",401);
        expiry=tenant.ExpireDate; return await PostgreSqlPermissionReader.ReadAsync(db,id,ct);
    }
    private void Clock()
    {
        var claim=http.HttpContext?.User.FindFirst("exp")?.Value;
        if(expiry<=DateTime.Now || claim!=null && (!long.TryParse(claim,out var seconds) || seconds<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())) throw Error("AUTHENTICATION_REQUIRED",401);
    }
    private async Task<bool> Has(UserPermissionContext actor,string action,CancellationToken ct) => actor.MenuKeys.Contains(Menu) && actor.ActionKeys.Contains(Menu+":"+action)
        && await (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId where m.Enable && m.MenuKey==Menu && a.ActionCode==action select a).AnyAsync(ct);
    private async Task<FieldPermissionDecisionV1> FieldsPermission(UserPermissionContext actor,bool write,CancellationToken ct)
    {
        if(!await Has(actor,ReadAction,ct) || write && !await Has(actor,ConfigureAction,ct)) throw Error("COMMAND_FORBIDDEN",403);
        var source=await new CurrentFieldPermissionSourceReaderV1(db,http,catalog).ReadAsync(Resource,ct);
        if(!source.Fields.Order().SequenceEqual(Fields.Order())) throw Error("FIELD_AUTHORITY_UNAVAILABLE");
        var yes=FieldPermissionCheckV1.Allowed;var na=FieldPermissionCheckV1.NotApplicable;const string mapping="Workflow.AppBinding.v2";
        var uses=new[]{FieldPermissionUseV1.Read,FieldPermissionUseV1.Write};
        var decision=source.Evaluate(source.Versions(mapping),mapping,
            uses.Select(u=>new FieldPermissionMappingV1("Configuration",u,"binding.configuration",true,[])),
            uses.Select(u=>new FieldPermissionConstraintsV1("Configuration",u,u==FieldPermissionUseV1.Write&&!write?FieldPermissionCheckV1.Denied:yes,yes,yes,na,yes,yes,yes)));
        if(!decision.IsAvailable) throw Error("FIELD_AUTHORITY_UNAVAILABLE");
        if(!decision.Fields["Configuration"].CanRead || write&&!decision.Fields["Configuration"].CanWrite) throw Error("FIELD_FORBIDDEN",403);
        return decision;
    }
    private sealed record Head(Guid Id,string BizType,string SubjectKind,string Owner,Guid OwnerId,Guid? DeptId,long Version,Guid TimelineId,Guid RevisionId,int Number);
    private IQueryable<Head> Heads()=>db.Database.SqlQuery<Head>($"""
        SELECT id AS "Id",biz_type AS "BizType",subject_kind AS "SubjectKind",owner AS "Owner",owner_id AS "OwnerId",owner_dept_id AS "DeptId",
          row_version AS "Version",timeline_id AS "TimelineId",revision_id AS "RevisionId",revision_number AS "Number"
        FROM cp6_approval_binding_v2.heads WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
        """);
    private int Scope(UserPermissionContext actor)
    {
        var c=catalog();if(!c.IsSealed || !c.Scopes.TryGetValue(Resource,out var definition) || definition.Default!=1 || !definition.Supports.Order().SequenceEqual(new[]{1,2,3,4,5})) throw Error("RESOURCE_CATALOG_UNAVAILABLE");
        var scope=actor.DataScopes.GetValueOrDefault(Resource,1);if(scope is <1 or >5) throw Error("RESOURCE_SCOPE_UNAVAILABLE");return scope;
    }
    private async Task<bool> Visible(UserPermissionContext actor,Head h,CancellationToken ct)
    {
        var scope=Scope(actor);if(scope==1)return h.OwnerId==actor.UserId;if(scope==5)return true;if(h.DeptId==null)return false;
        var department=await PostgreSqlOrganizationV1.Healthy(db).SingleOrDefaultAsync(x=>x.Id==h.DeptId,ct);if(department==null)return false;
        return scope switch{2=>actor.DeptId==department.Id,3=>actor.DeptPath.Length>0&&department.Path.StartsWith(actor.DeptPath,StringComparison.Ordinal),4=>actor.CustomDeptIds.GetValueOrDefault(Resource)?.Contains(department.Id)==true,_=>false};
    }
    private async Task<Head> HeadFor(UserPermissionContext actor,Guid id,CancellationToken ct)
    { var h=await Heads().SingleOrDefaultAsync(x=>x.Id==id,ct);if(h==null||!await Visible(actor,h,ct))throw Error("RESOURCE_NOT_FOUND",404);return h; }
    private async Task CompleteRead(UserPermissionContext initial,CancellationToken ct,IEnumerable<Head>? heads=null)
    {
        var final=await Actor(ct);if(final.UserId!=initial.UserId)throw Error("AUTHENTICATION_REQUIRED",401);
        await FieldsPermission(final,false,ct);
        foreach(var h in heads??[])if(!await Visible(final,h,ct))throw Error("RESOURCE_NOT_FOUND",404);
    }
    private sealed record Registration(string BizType,string SubjectKind,string Owner,Guid Revision,string SnapshotSchema,string SnapshotFields,bool Enabled);
    private IQueryable<Registration> Registrations()=>db.Database.SqlQuery<Registration>($"""
        SELECT biz_type AS "BizType",subject_kind AS "SubjectKind",owner AS "Owner",revision AS "Revision",snapshot_schema AS "SnapshotSchema",snapshot_fields AS "SnapshotFields",enabled AS "Enabled"
        FROM cp6_approval_binding_v2.registrations WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
        """);
    private sealed record RevisionRow(Guid Id,int Number,string Body,Guid Actor,DateTimeOffset At);
    private IQueryable<RevisionRow> Revisions(Guid binding)=>db.Database.SqlQuery<RevisionRow>($"""
        SELECT id AS "Id",revision_number AS "Number",body AS "Body",actor_id AS "Actor",recorded_at AS "At" FROM cp6_approval_binding_v2.revisions
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND binding_id={binding}
        """);
    private async Task<ApprovalBindingTimelineV2> Timeline(Head h,Guid id,CancellationToken ct)
    {
        var raw=await db.Database.SqlQuery<string>($"""
            SELECT body AS "Value" FROM cp6_approval_binding_v2.timelines WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND binding_id={h.Id} AND id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RESOURCE_NOT_FOUND",404);
        return JsonSerializer.Deserialize<ApprovalBindingTimelineV2>(raw,Json) ?? throw Error("TIMELINE_UNAVAILABLE");
    }
    private async Task<ApprovalBindingViewV2> View(Head h,DateTimeOffset at,CancellationToken ct,RevisionRow? revision=null)
    {
        var r=revision ?? await Revisions(h.Id).SingleAsync(x=>x.Id==h.RevisionId,ct);var body=JsonSerializer.Deserialize<ApprovalBindingWriteV2>(r.Body,Json)!;
        var timeline=await Timeline(h,h.TimelineId,ct);var effective=timeline.Intervals.SingleOrDefault(x=>x.From<=at&&(x.To==null||at<x.To));
        return new(h.Id,r.Id,r.Number,effective?.State??"UNCONFIGURED",h.Owner,ApprovalBindingTimelineBuilderV2.Version(h.Version),r.Actor,r.At,h.TimelineId,effective,
            timeline.Intervals.Where(x=>x.From>at).ToArray(),body.CommandId,body.BizType,body.SubjectKind,body.SelectionMode,body.FlowKey,body.FlowDefVersionId,
            body.EffectiveFrom,body.EffectiveTo,body.RuleMode,body.Rules,body.DetailRoute,body.Remark);
    }
    private async Task Ready(CancellationToken ct)
    { if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_binding_v2.ready() AS \"Value\"").SingleAsync(ct))throw Error("BINDING_WRITER_COVERAGE_UNAVAILABLE"); }
    public async Task<ApprovalBindingCapabilitiesV2> CapabilitiesAsync(CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);
        var snapshot=await fence.ReadLocked(ct);var read=await Has(actor,ReadAction,ct);var write=read&&await Has(actor,ConfigureAction,ct)&&snapshot is {Enabled:true};
        Clock();return new("oa.app/2.0",snapshot?.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)??"UNAVAILABLE",read,write,false,false,false,["oa.app/2.0"]);
    }
    public async Task<ApprovalBindingOptionsV2> OptionsAsync(CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);await FieldsPermission(actor,false,ct);
        var rows=await Registrations().Where(x=>x.Enabled).OrderBy(x=>x.BizType).ThenBy(x=>x.SubjectKind).Take(1001).ToArrayAsync(ct);
        if(rows.Length>1000)throw Error("BINDING_OPTIONS_LIMIT",422);
        var result=new ApprovalBindingOptionsV2(rows.Select(x=>new ApprovalBindingTypeV2(x.BizType,x.Owner,[x.SubjectKind],x.SnapshotSchema)).ToArray(),await flows.BindingChoices(actor,ct),DateTimeOffset.UtcNow);
        await CompleteRead(actor,ct);Clock();return result;
    }
    private static int Offset(string? cursor)
    { if(cursor==null)return 0;if(!int.TryParse(cursor,out var n)||n<0||n>100000||cursor!=n.ToString(System.Globalization.CultureInfo.InvariantCulture))throw Error("CURSOR_INVALID",422);return n; }
    public async Task<ApprovalBindingListV2<ApprovalBindingViewV2>> ListAsync(string? cursor=null,CancellationToken ct=default)
    {
        var offset=Offset(cursor);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);await FieldsPermission(actor,false,ct);_=Scope(actor);
        var visible=new List<Head>();foreach(var h in await Heads().OrderBy(x=>x.BizType).ThenBy(x=>x.SubjectKind).ToArrayAsync(ct))if(await Visible(actor,h,ct))visible.Add(h);
        var at=DateTimeOffset.UtcNow;var result=new List<ApprovalBindingViewV2>();foreach(var h in visible.Skip(offset).Take(100))result.Add(await View(h,at,ct));
        await CompleteRead(actor,ct,visible.Skip(offset).Take(100));Clock();return new(result.ToArray(),visible.Count>offset+100?(offset+100).ToString(System.Globalization.CultureInfo.InvariantCulture):null,at);
    }
    public async Task<ApprovalBindingListV2<ApprovalBindingViewV2>> HistoryAsync(Guid id,string? cursor=null,CancellationToken ct=default)
    {
        var offset=Offset(cursor);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);await FieldsPermission(actor,false,ct);var h=await HeadFor(actor,id,ct);
        var rows=await Revisions(id).OrderByDescending(x=>x.Number).Skip(offset).Take(101).ToArrayAsync(ct);var at=DateTimeOffset.UtcNow;var result=new List<ApprovalBindingViewV2>();
        foreach(var row in rows.Take(100))
        {
            // The first timeline containing a revision is its publication, not today's current schedule.
            var original=await db.Database.SqlQuery<string>($"""
                SELECT t.body AS "Value" FROM cp6_approval_binding_v2.timelines t
                WHERE t.tenant_id={db.CurrentTenantId} AND t.environment_id={fence.EnvironmentId} AND t.binding_id={id}
                  AND EXISTS(SELECT FROM cp6_approval_binding_v2.intervals i WHERE i.tenant_id=t.tenant_id AND i.environment_id=t.environment_id AND i.binding_id=t.binding_id AND i.timeline_id=t.id AND i.revision_id={row.Id})
                ORDER BY t.timeline_no LIMIT 1
                """).SingleAsync(ct);
            var historical=JsonSerializer.Deserialize<ApprovalBindingTimelineV2>(original,Json)!;
            result.Add(await View(h with{TimelineId=historical.TimelineRevisionId,Version=long.Parse(historical.TimelineNo,System.Globalization.CultureInfo.InvariantCulture),RevisionId=row.Id,Number=row.Number},historical.PublishedAt,ct,row));
        }
        await CompleteRead(actor,ct,[h]);Clock();return new(result.ToArray(),rows.Length>100?(offset+100).ToString(System.Globalization.CultureInfo.InvariantCulture):null,at);
    }
    public async Task<ApprovalBindingTimelineV2> TimelineAsync(Guid id,Guid timeline,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);await FieldsPermission(actor,false,ct);
        var head=await HeadFor(actor,id,ct);var value=await Timeline(head,timeline,ct);await CompleteRead(actor,ct,[head]);Clock();return value;
    }
    private sealed record OriginalRow(Guid BindingId,string Operation,string Digest,string ResultJson,DateTimeOffset At);
    private Task<OriginalRow?> Original(Guid actor,Guid command,CancellationToken ct)=>db.Database.SqlQuery<OriginalRow>($"""
        SELECT binding_id AS "BindingId",operation AS "Operation",digest AS "Digest",result_json AS "ResultJson",recorded_at AS "At" FROM cp6_approval_binding_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    public async Task<ApprovalBindingResultV2> OriginalAsync(Guid command,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);await FieldsPermission(actor,false,ct);
        var original=await Original(actor.UserId,command,ct)??throw Error("RESOURCE_NOT_FOUND",404);var head=await HeadFor(actor,original.BindingId,ct);
        await CompleteRead(actor,ct,[head]);Clock();return JsonSerializer.Deserialize<ApprovalBindingResultV2>(original.ResultJson,Json)!;
    }
    public async Task<object> CommandAsync(Guid command,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);await FieldsPermission(actor,false,ct);
        var row=await Original(actor.UserId,command,ct)??throw Error("RESOURCE_NOT_FOUND",404);var head=await HeadFor(actor,row.BindingId,ct);
        await CompleteRead(actor,ct,[head]);Clock();var original=JsonSerializer.Deserialize<ApprovalBindingResultV2>(row.ResultJson,Json)!;
        return new { commandId=command,state="COMPLETED",targetType="BINDING",targetId=original.Result.BindingId,
            resultRef=new { owner="Main Workflow",id=original.Result.TimelineRevisionId.ToString("D"),version=original.Result.RowVersion },
            errorCode=(string?)null,recordedAt=row.At };
    }

    private static void Shape(JsonElement value,params string[] fields)
    { if(value.ValueKind!=JsonValueKind.Object || !value.EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal).SequenceEqual(fields.Order(StringComparer.Ordinal)))throw new JsonException(); }
    private static void Id(JsonElement value,string field,bool nullable=false)
    {var v=value.GetProperty(field);if(nullable&&v.ValueKind==JsonValueKind.Null)return;var s=v.GetString();if(!Guid.TryParseExact(s,"D",out var id)||id==Guid.Empty||s!=id.ToString("D"))throw new JsonException();}
    private static void Instant(JsonElement value,string field,bool nullable=false)
    {var v=value.GetProperty(field);if(nullable&&v.ValueKind==JsonValueKind.Null)return;var s=v.GetString();if(s==null||!Regex.IsMatch(s,@"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.000Z\z"))throw new JsonException();}
    private static bool Key(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max;
    private static (Guid Id,ApprovalBindingWriteV2? Body,string? Reason,string Digest) Parse(byte[] raw,bool disable)
    {
        if(raw.Length>1024*1024)throw Error("BINDING_SHAPE_INVALID",422);
        StrictJsonDocument document;
        try{document=WorkflowCommandJsonV1.Parse(raw);}catch(JsonException){throw Error("MALFORMED_JSON",400);}
        using var parsed=document;
        try
        {
            var root=document.RootElement;Id(root,"commandId");
            if(disable)
            {
                Shape(root,"commandId","reason");var input=root.Deserialize<ApprovalBindingDisableV2>(Json)!;
                if(string.IsNullOrWhiteSpace(input.Reason)||input.Reason.EnumerateRunes().Count()>500)throw new JsonException();
                return(input.CommandId,null,input.Reason,MainOwnerOperationJournalV1.Digest(Encoding.UTF8.GetBytes(WorkflowCommandJsonV1.Canonical(root))));
            }
            Shape(root,"commandId","bizType","subjectKind","selectionMode","flowKey","flowDefVersionId","effectiveFrom","effectiveTo","ruleMode","rules","detailRoute","remark");Id(root,"flowDefVersionId",true);Instant(root,"effectiveFrom");Instant(root,"effectiveTo",true);
            foreach(var rule in root.GetProperty("rules").EnumerateArray()){Shape(rule,"ruleId","priority","when","flowKey","flowDefVersionId");Id(rule,"ruleId");Id(rule,"flowDefVersionId",true);}
            var body=root.Deserialize<ApprovalBindingWriteV2>(Json)!;
            if(!Key(body.BizType,50)||!Key(body.SubjectKind,50)||!Key(body.FlowKey,100)||body.SelectionMode is not ("EXACT_PINNED" or "RESOLVE_PUBLISHED_ON_SUBMIT")
                ||body.RuleMode is not ("ORDERED_FIRST_MATCH" or "EXCLUSIVE_MATCH")||body.Rules==null||body.Rules.Length>1000||body.Remark==null||body.Remark.Length>200
                ||body.EffectiveTo<=body.EffectiveFrom||body.Rules.Select(x=>x.RuleId).Distinct().Count()!=body.Rules.Length||body.Rules.Select(x=>x.Priority).Distinct().Count()!=body.Rules.Length
                ||body.Rules.Any(x=>x.Priority is <0 or >9999||!Key(x.FlowKey,100)||!Key(x.When,4000))
                ||(body.SelectionMode=="EXACT_PINNED")!=(body.FlowDefVersionId!=null)||body.Rules.Any(x=>(body.SelectionMode=="EXACT_PINNED")!=(x.FlowDefVersionId!=null)))throw new JsonException();
            if(body.DetailRoute is {} route && (route.Length>300||!route.StartsWith('/')||route.StartsWith("//")||route.Contains('\\')||route.Any(char.IsControl)
                ||Uri.UnescapeDataString(route).StartsWith("//")||Uri.UnescapeDataString(route).Contains('\\')||Uri.UnescapeDataString(route).Any(char.IsControl)||route.Replace("{bizId}","").IndexOfAny(['{','}'])>=0||route.Split("{bizId}").Length>2))throw new JsonException();
            return(body.CommandId,body,null,MainOwnerOperationJournalV1.Digest(Encoding.UTF8.GetBytes(WorkflowCommandJsonV1.Canonical(root))));
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {throw Error("BINDING_SHAPE_INVALID",422);}
    }
    private async Task ValidateSources(UserPermissionContext actor,ApprovalBindingWriteV2 body,Registration registration,CancellationToken ct)
    {
        string[] fields;try{fields=JsonSerializer.Deserialize<string[]>(registration.SnapshotFields,Json)??throw new JsonException();}catch(JsonException){throw Error("SNAPSHOT_SCHEMA_INVALID",422);}
        if(body.Rules.Any(x=>!ExpressionEvaluator.ValidateSchemaExpression(x.When,fields)))throw Error("SNAPSHOT_SCHEMA_INVALID",422);
        await flows.BindingSource(actor,body.FlowKey,body.FlowDefVersionId,ct);
        foreach(var rule in body.Rules)await flows.BindingSource(actor,rule.FlowKey,rule.FlowDefVersionId,ct);
    }
    public Task<ApprovalBindingResultV2> SaveAsync(Guid? binding,string? etag,byte[] raw,CancellationToken ct=default)=>Write(binding,etag,raw,false,ct);
    public Task<ApprovalBindingResultV2> DisableAsync(Guid binding,string? etag,byte[] raw,CancellationToken ct=default)=>Write(binding,etag,raw,true,ct);
    private async Task<ApprovalBindingResultV2> Write(Guid? binding,string? etag,byte[] raw,bool disable,CancellationToken ct)
    {
        var command=Parse(raw,disable);var operation=disable?"DISABLE":binding==null?"CREATE":"REVISE";Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            await Root(ct);var actor=await Actor(ct);await FieldsPermission(actor,false,ct);var original=await Original(actor.UserId,command.Id,ct);
            if(original!=null)
            {
                var originalHead=await HeadFor(actor,original.BindingId,ct);
                if(original.Operation!=operation||original.Digest!=command.Digest||binding!=null&&binding!=original.BindingId)throw Error("INTENT_PAYLOAD_CONFLICT",409);
                await CompleteRead(actor,ct,[originalHead]);Clock();return JsonSerializer.Deserialize<ApprovalBindingResultV2>(original.ResultJson,Json)!;
            }
            var before=await fence.AcquireAsync(ct);await Ready(ct);await FieldsPermission(actor,true,ct);_=Scope(actor);
            Head? old=binding==null?null:await HeadFor(actor,binding.Value,ct);
            if(old!=null && etag==null)throw Error("PRECONDITION_REQUIRED",428);
            if(old!=null && !ApprovalBindingTimelineBuilderV2.Matches(etag,old.Version))throw Error("VERSION_CHANGED",412);
            var at=DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Registration? registration=null;var body=command.Body;
            if(!disable)
            {
                if(body!.EffectiveFrom<at)throw Error("EFFECTIVE_TIME_PAST",422);
                registration=await Registrations().SingleOrDefaultAsync(x=>x.BizType==body.BizType&&x.SubjectKind==body.SubjectKind&&x.Enabled,ct)??throw Error("OWNER_UNAVAILABLE",503);
                if(old!=null&&(old.BizType!=body.BizType||old.SubjectKind!=body.SubjectKind||old.Owner!=registration.Owner))throw Error("INTENT_PAYLOAD_CONFLICT",409);
                if(old==null&&await Heads().AnyAsync(x=>x.BizType==body.BizType&&x.SubjectKind==body.SubjectKind,ct))throw Error("BINDING_EXISTS",409);
                await ValidateSources(actor,body,registration,ct);
            }
            var id=old?.Id??Guid.NewGuid();var revision=disable?old!.RevisionId:Guid.NewGuid();var number=disable?old!.Number:(old?.Number??0)+1;
            var timelineId=Guid.NewGuid();var version=(old?.Version??0)+1;
            var previous=old==null?null:await Timeline(old,old.TimelineId,ct);
            var intervals=ApprovalBindingTimelineBuilderV2.Revise(previous?.Intervals??[],disable?at:body!.EffectiveFrom,body?.EffectiveTo,revision,disable);
            var timeline=new ApprovalBindingTimelineV2(timelineId,id,version.ToString(System.Globalization.CultureInfo.InvariantCulture),old?.TimelineId,at,actor.UserId,intervals);
            if(!disable)await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_binding_v2.revisions(tenant_id,environment_id,binding_id,id,revision_number,registration_revision,body,actor_id,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{revision},{number},{registration!.Revision},{Serialize(body!)},{actor.UserId},{at})
                """,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_binding_v2.timelines(tenant_id,environment_id,binding_id,id,timeline_no,previous_id,body)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{timelineId},{version},{(old==null?(Guid?)null:old.TimelineId)},{Serialize(timeline)})
                """,ct);
            foreach(var item in intervals)await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_binding_v2.intervals(tenant_id,environment_id,binding_id,timeline_id,starts_at,ends_at,revision_id,state)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{timelineId},{item.From},{item.To},{item.RevisionId},{item.State})
                """,ct);
            var final=await Actor(ct);if(final.UserId!=actor.UserId)throw Error("AUTHENTICATION_REQUIRED",401);
            var permissions=await FieldsPermission(final,true,ct);
            if(old!=null && (!await Visible(final,old,ct)||await Heads().SingleAsync(x=>x.Id==id,ct)!=old))throw Error("VERSION_CHANGED",412);
            if(!disable)
            {
                if(await Registrations().SingleOrDefaultAsync(x=>x.BizType==body!.BizType&&x.SubjectKind==body.SubjectKind,ct)!=registration)throw Error("OWNER_REGISTRATION_CHANGED",409);
                await ValidateSources(final,body!,registration!,ct);
            }
            await fence.ValidateAsync(before,ct);
            var next=old==null?new Head(id,body!.BizType,body.SubjectKind,registration!.Owner,actor.UserId,actor.DeptId,version,timelineId,revision,number):old with{Version=version,TimelineId=timelineId,RevisionId=revision,Number=number};
            if(old==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_binding_v2.heads(tenant_id,environment_id,id,biz_type,subject_kind,owner,owner_id,owner_dept_id,row_version,timeline_id,revision_id,revision_number)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{next.BizType},{next.SubjectKind},{next.Owner},{next.OwnerId},{next.DeptId},{version},{timelineId},{revision},{number})
                """,ct);
            else if(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_approval_binding_v2.heads SET row_version={version},timeline_id={timelineId},revision_id={revision},revision_number={number}
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={id} AND row_version={old.Version}
                """,ct)!=1)throw Error("VERSION_CHANGED",412);
            var scope=await fence.AcquireAsync(ct);var view=await View(next,at,ct);var result=new ApprovalBindingResultV2(command.Id,"COMPLETED",view,"/api/oa/app/v2/commands/"+command.Id.ToString("D"));
            var evidence=Serialize(new{ActualActor=actor.UserId,EffectiveActor=actor.UserId,final.RoleIds,Action=ConfigureAction,BeforeScope=before.Snapshot,AfterScope=scope.Snapshot,
                ResourceScope=Scope(final),final.DeptId,final.DeptPath,final.CustomDeptIds,Fields=permissions.Input.Versions,FieldRows=permissions.Input.Rows,
                Registration=registration,OldTimeline=previous,NewTimeline=timeline,command.Reason,command.Digest,RawDigest=MainOwnerOperationJournalV1.Digest(raw)});
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_binding_v2.commands(tenant_id,environment_id,actor_id,command_id,binding_id,operation,digest,raw_body,result_json,evidence_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{command.Id},{id},{operation},{command.Digest},{raw},{Serialize(result)},{evidence},{at})
                """,ct);
            await Ready(ct);await fence.ValidateAsync(scope,ct);Clock();ct.ThrowIfCancellationRequested();committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception error)
        {
            try{await tx.RollbackAsync(CancellationToken.None);}catch(Exception rollback){throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN",504,new AggregateException(error,rollback));}
            if(committing)throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN",504,error);
            if(error is Npgsql.PostgresException{SqlState:"23505"})throw Error("VERSION_CHANGED",412);
            throw;
        }
    }
}
