using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

internal sealed class ToolActualUsageStoreV1(CP6Context db)
{
    internal const string Owner = "Main.Wms.ToolActualUsage.v1", EvidenceOwner = "Main.Wms.ManualToolUseEvidence.v1",
        OccurrenceOwner = "Main.Wms.ToolUseOccurrence.v1", ObservationOwner = "Main.Wms.ToolActualObservation.v1",
        PolicyOwner = "Main.Wms.ToolActualRecognitionPolicy.v1", DecisionOwner = "Main.Wms.ToolActualRecognition.v1",
        UseOwner = "Main.Wms.ToolUse.v1", ApplicationOwner = "Main.Wms.ToolObservationApplication.v1";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Hash(string text) => ToolMaintenanceStoreV1.Hash(text);
    internal static string OwnerOf(string kind) => kind switch { "EVIDENCE" => EvidenceOwner, "OCCURRENCE" => OccurrenceOwner,
        "M_OCCURRENCE" => "Main.Wms.ToolMaintenanceOccurrence.v1", "M_OBSERVATION" => ObservationOwner,
        "M_POLICY" => "Main.Wms.ToolMaintenanceRecognitionPolicy.v1", "M_RECOGNITION" => DecisionOwner, "M_APPLICATION" => ApplicationOwner,
        "OBSERVATION" => ObservationOwner, "POLICY" => PolicyOwner, "RECOGNITION" => DecisionOwner, "USE" => UseOwner, "APPLICATION" => ApplicationOwner, _ => throw Error("TOOL_ACTUAL_UNAVAILABLE",503) };
    internal static ToolActualRefV1 Ref(ToolActualFactV1 fact) => new(OwnerOf(fact.Kind), fact.RecordId, fact.Revision, Hash(JsonSerializer.Serialize(fact, Json)));
    internal static T Body<T>(ToolActualFactViewV1 fact) => fact.Fact.Body.Deserialize<T>(Json) ?? throw Error("TOOL_ACTUAL_UNAVAILABLE",503);
    internal static ToolActualFactViewV1 Exact(ToolActualFactViewV1[] facts, ToolActualRefV1 reference, string kind)
        => reference != null && facts.SingleOrDefault(f => f.Reference == reference && f.Fact.Kind == kind) is { } result ? result : throw Error("TOOL_ACTUAL_REFERENCE_CHANGED",412);
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null) throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_actual_v1.environment,cp6_tool_actual_v1.documents,cp6_tool_actual_v1.facts " + (write ? "IN SHARE ROW EXCLUSIVE MODE" : "IN SHARE MODE"),ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=8 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid IN ('cp6_tool_actual_v1.environment'::regclass,
              'cp6_tool_actual_v1.documents'::regclass,'cp6_tool_actual_v1.facts'::regclass) AND tgname LIKE 'tool_actual_%'
              AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("TOOL_ACTUAL_UNAVAILABLE",503);
    }
    private sealed class EnvironmentRow { public Guid Id { get; set; } public string Mode { get; set; } = ""; }
    internal async Task Runtime(ToolActualRuntimeV1 runtime, bool bind, CancellationToken ct)
    {
        if (runtime.EnvironmentId == Guid.Empty || runtime.Mode is not ("PRODUCTION" or "SIMULATION")) throw Error("TOOL_ACTUAL_RUNTIME_UNCONFIGURED",503);
        var row = await db.Database.SqlQueryRaw<EnvironmentRow>("SELECT environment_id AS \"Id\",mode AS \"Mode\" FROM cp6_tool_actual_v1.environment").SingleOrDefaultAsync(ct);
        if (row != null && (row.Id != runtime.EnvironmentId || row.Mode != runtime.Mode)) throw Error("TOOL_ACTUAL_RUNTIME_MISMATCH",503);
        if (row == null && bind) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_tool_actual_v1.environment(singleton,environment_id,mode) VALUES(true,{runtime.EnvironmentId},{runtime.Mode})",ct);
    }
    private sealed class Row { public string Json { get; set; } = ""; public string Digest { get; set; } = ""; }
    internal async Task<ToolActualFactViewV1[]> Facts(Guid tool, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<Row>($"SELECT body_json AS \"Json\",body_digest AS \"Digest\" FROM cp6_tool_actual_v1.facts WHERE tenant_id={db.CurrentTenantId} AND tool_id={tool} ORDER BY sequence").ToArrayAsync(ct);
        return rows.Select(row => { var fact=JsonSerializer.Deserialize<ToolActualFactV1>(row.Json,Json);
            if (fact == null || fact.Scope.TenantId != db.CurrentTenantId || fact.ToolId != tool || Hash(row.Json) != row.Digest || Ref(fact).Digest != row.Digest) throw Error("TOOL_ACTUAL_UNAVAILABLE",503);
            return new ToolActualFactViewV1(Ref(fact),fact); }).ToArray();
    }
    internal async Task<ToolActualFactViewV1> Append(ToolActualFactV1 fact,CancellationToken ct)
    {
        var json=JsonSerializer.Serialize(fact,Json);var reference=Ref(fact);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_actual_v1.facts(tenant_id,tool_id,record_id,site_id,kind,subject_id,identity_key,revision,body_json,body_digest)
            VALUES({fact.Scope.TenantId},{fact.ToolId},{fact.RecordId},{fact.Scope.SiteId},{fact.Kind},{fact.SubjectId},{fact.IdentityKey},{Revision(fact.Revision)},{json},{reference.Digest})
            """,ct);return new(reference,fact);
    }
    internal async Task<Guid> Document(Guid site,string key,CancellationToken ct)
    {
        var id=await db.Database.SqlQuery<Guid>($"SELECT document_id AS \"Value\" FROM cp6_tool_actual_v1.documents WHERE tenant_id={db.CurrentTenantId} AND site_id={site} AND document_key={key}").SingleOrDefaultAsync(ct);
        if(id!=Guid.Empty)return id;id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_tool_actual_v1.documents(tenant_id,site_id,document_key,document_id) VALUES({db.CurrentTenantId},{site},{key},{id})",ct);return id;
    }
    internal async Task<bool> ForeignIdentity(string key,Guid tool,CancellationToken ct)
        => await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_tool_actual_v1.facts WHERE tenant_id={db.CurrentTenantId} AND identity_key={key} AND tool_id<>{tool}) AS \"Value\"").SingleAsync(ct);
    internal static bool Unreconciled(ToolActualFactViewV1[] facts) => facts.Any(f=>f.Fact.Kind is "OBSERVATION" or "M_OBSERVATION");
    internal static ToolActualObservationViewV1 Observation(ToolActualFactViewV1 original,ToolActualFactViewV1[] facts)
    {
        var decisions=facts.Where(f=>f.Fact.Kind=="RECOGNITION"&&f.Fact.SubjectId==original.Reference.Id).ToArray();
        var application=facts.SingleOrDefault(f=>f.Fact.Kind=="APPLICATION"&&f.Fact.SubjectId==original.Reference.Id);
        return new(original,application!=null?"APPLIED":decisions.Length==0?"UNAPPLIED":"WAITING_EVIDENCE",application!=null?"2":"1",decisions,application);
    }
    internal static ToolMaintenanceCycleV1 ProjectCycle(ToolRegistrationOriginalV1 original,ToolMaintenanceCycleV1 current,ToolActualFactViewV1[] facts)
    {
        var uses=facts.Where(f=>f.Fact.Kind=="USE").Select(Body<ToolActualUseBodyV1>).ToArray();
        var shots=checked(current.Shots+uses.Where(u=>u.TargetCycle.Id==current.Id).Sum(u=>u.Shots));
        var lifetime=original.LifetimeShots==null?null:checked(long.Parse(original.LifetimeShots,CultureInfo.InvariantCulture)+uses.Sum(u=>(long)u.Shots)).ToString(CultureInfo.InvariantCulture);
        return current with { Shots=shots,LifetimeKnowledge=original.LifetimeKnowledge,LifetimeShots=lifetime,
            ProjectionRevision=uses.Length==0?null:(uses.Length+1L).ToString(CultureInfo.InvariantCulture) };
    }
    internal static async Task<ToolActualCycleV1[]> Cycles(CP6Context db,ToolRegistrationOriginalV1 original,ToolMaintenanceFactViewV1[] maintenance,ToolActualFactViewV1[] facts,CancellationToken ct)
    {
        var initial=await db.Database.SqlQuery<DateTimeOffset>($"SELECT observed_at AS \"Value\" FROM cp6_tool_instance_v1.versions WHERE tenant_id={db.CurrentTenantId} AND object_id={original.ObjectId} AND revision={Revision(original.NativeAtRegistration.Version)}").SingleAsync(ct);
        var bases=new List<(ToolMaintenanceCycleV1 Cycle,DateTimeOffset? Start)> { (new(original.CycleAnchorId,original.CycleNo,int.Parse(original.InitialCycleShots,CultureInfo.InvariantCulture),original.LifetimeKnowledge,original.LifetimeShots),original.CycleNo==null?null:initial) };
        bases.AddRange(maintenance.Where(f=>f.Fact.Kind=="RESET").Select(f=>(f.Fact.CycleAfter!, (DateTimeOffset?)f.Fact.RecordedAt)));
        return bases.Select((b,i)=> { var cycle=ProjectCycle(original,b.Cycle,facts);return new ToolActualCycleV1(ToolMaintenanceStoreV1.CycleRef(cycle),cycle.Number,cycle.Shots.ToString(CultureInfo.InvariantCulture),i==bases.Count-1,b.Start,i<bases.Count-1?bases[i+1].Start:null); }).ToArray();
    }
}
