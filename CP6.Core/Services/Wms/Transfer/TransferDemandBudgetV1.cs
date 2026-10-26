using System.Data;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Wms.Transfer;

/// <summary>
/// Internal persistence prerequisite, not an authorization or registered TF HTTP adapter.
/// The future Owner must resolve deployment, current permission and original master evidence before use.
/// Every operation participates in the caller's actual Main transaction; this type never commits it.
/// </summary>
internal sealed partial class TransferDemandBudgetV1
{
    internal sealed record Space(Guid EnvironmentId, string Mode, Guid? SandboxScopeId);
    internal sealed record Ref(string Owner, Guid Id, string Version, string Digest);
    internal sealed record Code(string Id, string Version);
    internal sealed record Place(Guid WarehouseId, Guid LocationId);
    internal sealed record Interval(string From, string To);
    internal sealed record Segment(string SegmentKey, Interval RequestRange, Interval DemandRange);
    internal sealed record LocalDemand(Guid Id, string BudgetKey, string Version, Guid DocumentId, Guid LineId,
        Ref ProductRef, Code BaseUomRef, Guid SourceSiteId, Guid DestinationSiteId, Code AllowedPurposeRef,
        Place[] AllowedTargetLocations, string Quantity, Interval[] AuthorizedRanges, string State, Ref AuthorityRef);
    internal sealed record SavedDemand(Ref Ref, string Body);
    internal sealed record BindingInput(Guid Id, Guid RequestId, Guid RequestLineId, Ref DemandRef,
        string BoundQuantity, Segment[] DemandSegments);

    private readonly CP6Context db;
    private readonly IDbContextTransaction transaction;
    private readonly Guid tenant;
    private readonly Space space;
    private readonly HashSet<string> keys;
    private Guid Sandbox => space.SandboxScopeId ?? Guid.Empty;
    private TransferDemandBudgetV1(CP6Context db, Space space, IEnumerable<string> keys)
    { this.db=db; this.space=space; this.keys=keys.ToHashSet(StringComparer.Ordinal); tenant=db.CurrentTenantId; transaction=db.Database.CurrentTransaction!; }

    private static readonly ConditionalWeakTable<IDbContextTransaction, object> Sessions = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
    private static readonly Regex QtyPattern = new(@"\A(?:0|[1-9][0-9]{0,12})\.[0-9]{8}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex RevisionPattern = new(@"\A[1-9][0-9]{0,18}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static InvalidOperationException Error(string code) => new(code);
    private static long Rev(string value) => value!=null && RevisionPattern.IsMatch(value)
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw Error("TRANSFER_REVISION_INVALID");
    private static decimal Qty(string value, bool positive=false)
    {
        if(value==null || !QtyPattern.IsMatch(value)) throw Error("TRANSFER_QUANTITY_INVALID");
        var q=decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return positive && q==0 ? throw Error("TRANSFER_QUANTITY_INVALID") : q;
    }
    private static void Identifier(Guid id) { if(id==Guid.Empty) throw Error("TRANSFER_ID_REQUIRED"); }
    private static void Evidence(Ref value)
    {
        if(value==null || string.IsNullOrWhiteSpace(value.Owner)) throw Error("TRANSFER_REFERENCE_INVALID");
        Identifier(value.Id); Rev(value.Version);
        if(value.Digest==null || value.Digest.Length!=64 || value.Digest.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Error("TRANSFER_REFERENCE_INVALID");
    }
    private static void Unit(Code value)
    { if(value==null || string.IsNullOrWhiteSpace(value.Id)) throw Error("TRANSFER_UNIT_INVALID"); Rev(value.Version); }
    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value,Json);
    private static T Freeze<T>(T value) => JsonSerializer.Deserialize<T>(Bytes(value),Json)!;
    private static byte[] Canonical(object value)
    {
        using var doc=JsonDocument.Parse(Bytes(value)); using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)) Write(doc.RootElement,writer);
        return stream.ToArray();
        static void Write(JsonElement e,Utf8JsonWriter w)
        {
            if(e.ValueKind==JsonValueKind.Object)
            { w.WriteStartObject(); foreach(var p in e.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)) { w.WritePropertyName(p.Name); Write(p.Value,w); } w.WriteEndObject(); }
            else if(e.ValueKind==JsonValueKind.Array) { w.WriteStartArray(); foreach(var x in e.EnumerateArray()) Write(x,w); w.WriteEndArray(); }
            else e.WriteTo(w);
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static NpgsqlRange<decimal>[] Ranges(Interval[] input)
    {
        if(input==null || input.Length==0) throw Error("TRANSFER_RANGE_INVALID");
        var rows=input.Select(x=>x==null?throw Error("TRANSFER_RANGE_INVALID"):new NpgsqlRange<decimal>(Qty(x.From),true,Qty(x.To),false)).ToArray();
        var sorted=rows.OrderBy(x=>x.LowerBound).ToArray();
        for(var i=0;i<sorted.Length;i++)
            if(sorted[i].LowerBound>=sorted[i].UpperBound || i>0 && sorted[i-1].UpperBound>sorted[i].LowerBound)
                throw Error("TRANSFER_RANGE_INVALID");
        return rows;
    }
    private static bool Covered(NpgsqlRange<decimal>[] required,NpgsqlRange<decimal>[] allowed)
    {
        var sorted=allowed.OrderBy(x=>x.LowerBound).ToArray();
        foreach(var r in required)
        {
            var at=r.LowerBound;
            foreach(var a in sorted) { if(a.UpperBound<=at) continue; if(a.LowerBound>at) break; at=Math.Max(at,a.UpperBound); if(at>=r.UpperBound) break; }
            if(at<r.UpperBound) return false;
        }
        return true;
    }
    private static bool Overlaps(NpgsqlRange<decimal>[] a,NpgsqlRange<decimal>[] b)
        => a.Any(x=>b.Any(y=>x.LowerBound<y.UpperBound && y.LowerBound<x.UpperBound));
    private void Require(string? key=null)
    {
        if(!db.Database.IsNpgsql() || tenant==Guid.Empty || db.CurrentTenantId!=tenant
            || !ReferenceEquals(transaction,db.Database.CurrentTransaction)
            || transaction?.GetDbTransaction() is not NpgsqlTransaction t
            || !ReferenceEquals(t.Connection,db.Database.GetDbConnection())
            || t.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable)
            || System.Transactions.Transaction.Current!=null) throw Error("TRANSFER_OWNER_TRANSACTION_REQUIRED");
        if(key!=null && !keys.Contains(key)) throw Error("TRANSFER_BUDGET_LOCK_NOT_DECLARED");
    }
    private NpgsqlCommand Command(string sql, params (string Name,object Value)[] values)
    {
        Require(); var cmd=new NpgsqlCommand(sql,(NpgsqlConnection)db.Database.GetDbConnection(),(NpgsqlTransaction)transaction.GetDbTransaction());
        cmd.CommandTimeout=db.Database.GetCommandTimeout()??30;
        cmd.Parameters.AddWithValue("e",space.EnvironmentId);cmd.Parameters.AddWithValue("s",Sandbox);cmd.Parameters.AddWithValue("t",tenant);
        foreach(var (name,value) in values)
            if(value is NpgsqlRange<decimal>[] ranges)cmd.Parameters.AddWithValue(name,NpgsqlDbType.Array|NpgsqlDbType.Range|NpgsqlDbType.Numeric,ranges);
            else cmd.Parameters.AddWithValue(name,value);
        return cmd;
    }
    internal static async Task<TransferDemandBudgetV1> Open(CP6Context db, Space space, IEnumerable<string> budgetKeys, CancellationToken ct=default)
    {
        if(space==null || space.EnvironmentId==Guid.Empty || space.Mode is not ("PRODUCTION" or "SIMULATION")
            || (space.Mode=="PRODUCTION" ? space.SandboxScopeId!=null : space.SandboxScopeId is null || space.SandboxScopeId==Guid.Empty))
            throw Error("TRANSFER_ENVIRONMENT_INVALID");
        var keys=budgetKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(keys.Length==0 || keys.Any(string.IsNullOrWhiteSpace))throw Error("TRANSFER_BUDGET_REQUIRED");
        var store=new TransferDemandBudgetV1(db,space,keys);store.Require();
        // One complete lock declaration per actual transaction. Reopening cannot add a lower-order lock.
        lock(Sessions) { if(Sessions.TryGetValue(store.transaction,out _)) throw Error("TRANSFER_LOCK_PLAN_ALREADY_OPEN"); Sessions.Add(store.transaction,new()); }
        foreach(var key in keys)
        {
            var hash=SHA256.HashData(Canonical(new { space.EnvironmentId, sandbox=store.Sandbox,store.tenant,key }));
            await using var cmd=store.Command("SELECT pg_advisory_xact_lock(@lock)",("lock",BinaryPrimitives.ReadInt64BigEndian(hash)));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return store;
    }
    private sealed record Budget(Guid Id,long Version,long Revision,byte[] Identity);
    private async Task<Budget?> ReadBudget(string key,CancellationToken ct)
    {
        Require(key);
        await using var cmd=Command("""
            SELECT demand_id,current_version,revision,identity_bytes FROM cp6_transfer_local_v1.budgets
            WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND budget_key=@key AND mode=@mode FOR UPDATE
            """,("key",key),("mode",space.Mode));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetGuid(0),r.GetInt64(1),r.GetInt64(2),r.GetFieldValue<byte[]>(3)):null;
    }
    private async Task<(byte[] Body,string State,NpgsqlRange<decimal>[] Ranges,string Digest)?> Version(string key,long version,CancellationToken ct)
    {
        await using var cmd=Command("""
            SELECT body_bytes,state,ranges,digest FROM cp6_transfer_local_v1.demand_versions
            WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND budget_key=@key AND version=@version
            """,("key",key),("version",version));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?(r.GetFieldValue<byte[]>(0),r.GetString(1),r.GetFieldValue<NpgsqlRange<decimal>[]>(2),r.GetString(3)):null;
    }
    private async Task<List<NpgsqlRange<decimal>[]>> Active(string key,CancellationToken ct)
    {
        await using var cmd=Command("""
            SELECT demand_ranges FROM cp6_transfer_local_v1.bindings
            WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND budget_key=@key AND state='ACTIVE'
            """,("key",key));
        await using var r=await cmd.ExecuteReaderAsync(ct);var result=new List<NpgsqlRange<decimal>[]>();
        while(await r.ReadAsync(ct)) result.Add(r.GetFieldValue<NpgsqlRange<decimal>[]>(0));return result;
    }
    internal async Task<SavedDemand> RegisterLocal(LocalDemand demand, string expectedVersion, CancellationToken ct=default)
    {
        var d=Freeze(demand);Require(d.BudgetKey);Identifier(d.Id);Identifier(d.DocumentId);Identifier(d.LineId);
        Identifier(d.SourceSiteId);Identifier(d.DestinationSiteId);Evidence(d.ProductRef);Evidence(d.AuthorityRef);Unit(d.BaseUomRef);Unit(d.AllowedPurposeRef);
        if(d.AuthorityRef.Owner!="WMS_TRANSFER" || d.State is not ("OPEN" or "STOPPED" or "CLOSED")
            || d.AllowedTargetLocations==null || d.AllowedTargetLocations.Length==0
            || d.AllowedTargetLocations.Distinct().Count()!=d.AllowedTargetLocations.Length) throw Error("TRANSFER_LOCAL_SOURCE_INVALID");
        foreach(var p in d.AllowedTargetLocations) { Identifier(p.WarehouseId);Identifier(p.LocationId); }
        var version=Rev(d.Version);var expected=expectedVersion=="0"?0:Rev(expectedVersion);var quantity=Qty(d.Quantity,true);var ranges=Ranges(d.AuthorizedRanges);
        decimal measured=0;foreach(var r in ranges) { measured+=r.UpperBound-r.LowerBound; if(measured>quantity)throw Error("TRANSFER_QUANTITY_INVALID"); }
        if(measured!=quantity)throw Error("TRANSFER_QUANTITY_INVALID");
        var body=Canonical(new { owner="WMS_TRANSFER",d.DocumentId,d.LineId,d.Version,d.ProductRef,d.Quantity,d.AuthorizedRanges,
            d.BaseUomRef,d.SourceSiteId,d.DestinationSiteId,d.AllowedPurposeRef,
            allowedTargetLocations=d.AllowedTargetLocations.Select(p=>new {kind="LOCATION",p.WarehouseId,p.LocationId}),
            d.State,d.BudgetKey,delegation="LOCAL_TRANSACTION",d.AuthorityRef });
        var identity=Canonical(new {d.Id,d.DocumentId,d.LineId,d.ProductRef,d.BaseUomRef,d.SourceSiteId,d.DestinationSiteId,d.AllowedPurposeRef,d.AllowedTargetLocations});
        var result=new SavedDemand(new("WMS_TRANSFER",d.Id,d.Version,Hash(body)),Encoding.UTF8.GetString(body));
        var budget=await ReadBudget(d.BudgetKey,ct);
        if(budget!=null)
        {
            if(budget.Id!=d.Id || !budget.Identity.AsSpan().SequenceEqual(identity))throw Error("TRANSFER_SOURCE_IDENTITY_CONFLICT");
            var original=await Version(d.BudgetKey,version,ct);
            if(original!=null)
            { if(!original.Value.Body.AsSpan().SequenceEqual(body))throw Error("TRANSFER_SOURCE_VERSION_CONFLICT");return result; }
            if(expected!=budget.Version || version!=checked(budget.Version+1))throw Error("TRANSFER_SOURCE_STALE");
            var current=(await Version(d.BudgetKey,budget.Version,ct))!.Value;
            if(current.State=="CLOSED")throw Error("TRANSFER_SOURCE_CLOSED");
            if((await Active(d.BudgetKey,ct)).Any(a=>!Covered(a,ranges)))throw Error("SOURCE_BUDGET_CONFLICT");
        }
        else
        {
            if(expected!=0 || version!=1 || d.State!="OPEN")throw Error("TRANSFER_SOURCE_STALE");
            await using var add=Command("""
                INSERT INTO cp6_transfer_local_v1.budgets(environment_id,sandbox_id,tenant_id,mode,budget_key,demand_id,current_version,revision,identity_bytes)
                VALUES(@e,@s,@t,@mode,@key,@id,1,1,@identity)
                """,("mode",space.Mode),("key",d.BudgetKey),("id",d.Id),("identity",identity));
            await add.ExecuteNonQueryAsync(ct);
        }
        await using(var add=Command("""
            INSERT INTO cp6_transfer_local_v1.demand_versions(environment_id,sandbox_id,tenant_id,budget_key,version,body_bytes,digest,state,quantity,ranges)
            VALUES(@e,@s,@t,@key,@version,@body,@digest,@state,@quantity,@ranges)
            """,("key",d.BudgetKey),("version",version),("body",body),("digest",result.Ref.Digest),("state",d.State),("quantity",quantity),("ranges",ranges)))
            await add.ExecuteNonQueryAsync(ct);
        if(budget!=null)
        {
            await using var update=Command("""
                UPDATE cp6_transfer_local_v1.budgets SET current_version=@version,revision=revision+1
                WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND budget_key=@key
                """,("key",d.BudgetKey),("version",version));await update.ExecuteNonQueryAsync(ct);
        }
        return result;
    }
    private sealed record Original(string Key,Guid Request,Guid Line,byte[] Input,byte[] Result,string State);
    private async Task<Original?> ReadBinding(Guid id,CancellationToken ct)
    {
        await using var cmd=Command("""
            SELECT budget_key,request_id,line_id,input_bytes,result_bytes,state FROM cp6_transfer_local_v1.bindings
            WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND id=@id
            """,("id",id));await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetString(0),r.GetGuid(1),r.GetGuid(2),r.GetFieldValue<byte[]>(3),r.GetFieldValue<byte[]>(4),r.GetString(5)):null;
    }
    private async Task<long> Advance(string key,CancellationToken ct)
    {
        await using var cmd=Command("""
            UPDATE cp6_transfer_local_v1.budgets SET revision=revision+1
            WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND budget_key=@key RETURNING revision
            """,("key",key));return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
    internal async Task<string> Bind(string key, BindingInput input, CancellationToken ct=default)
    {
        var i=Freeze(input);Require(key);Identifier(i.Id);Identifier(i.RequestId);Identifier(i.RequestLineId);Evidence(i.DemandRef);
        if(i.DemandRef.Owner!="WMS_TRANSFER")throw Error("TRANSFER_LOCAL_SOURCE_REQUIRED");
        var quantity=Qty(i.BoundQuantity,true);
        if(i.DemandSegments==null || i.DemandSegments.Length==0 || i.DemandSegments.Any(s=>s==null || string.IsNullOrWhiteSpace(s.SegmentKey))
            || i.DemandSegments.Select(s=>s.SegmentKey).Distinct(StringComparer.Ordinal).Count()!=i.DemandSegments.Length)throw Error("TRANSFER_RANGE_INVALID");
        var sorted=i.DemandSegments.OrderBy(s=>Qty(s.RequestRange.From)).ToArray();
        var requests=Ranges(sorted.Select(s=>s.RequestRange).ToArray());var demands=Ranges(sorted.Select(s=>s.DemandRange).ToArray());
        if(requests[0].LowerBound!=0 || requests[^1].UpperBound!=quantity)throw Error("TRANSFER_RANGE_INVALID");
        for(var n=0;n<requests.Length;n++)
            if(requests[n].UpperBound-requests[n].LowerBound!=demands[n].UpperBound-demands[n].LowerBound
                || n>0 && requests[n-1].UpperBound!=requests[n].LowerBound)throw Error("TRANSFER_RANGE_INVALID");
        var bytes=Canonical(i);var budget=await ReadBudget(key,ct)??throw Error("TRANSFER_SOURCE_NOT_FOUND");
        var original=await ReadBinding(i.Id,ct);
        if(original!=null)
        {
            if(original.Key!=key || !original.Input.AsSpan().SequenceEqual(bytes))throw Error("SOURCE_BINDING_INPUT_CONFLICT");
            return Encoding.UTF8.GetString(original.Result);
        }
        var current=(await Version(key,budget.Version,ct))!.Value;
        if(i.DemandRef.Id!=budget.Id || Rev(i.DemandRef.Version)!=budget.Version || i.DemandRef.Digest!=current.Digest)
            throw Error("TRANSFER_SOURCE_STALE");
        if(current.State!="OPEN")throw Error("TRANSFER_SOURCE_NOT_OPEN");
        if(!Covered(demands,current.Ranges) || (await Active(key,ct)).Any(a=>Overlaps(a,demands)))throw Error("SOURCE_BUDGET_CONFLICT");
        var revision=await Advance(key,ct);
        var body=new { demandRef=i.DemandRef,i.RequestId,i.RequestLineId,i.BoundQuantity,i.DemandSegments,
            sourceBudgetRevision=revision.ToString(CultureInfo.InvariantCulture),state="ACTIVE",delegationReceipt=(object?)null };
        var reference=new Ref("WMS_TRANSFER",i.Id,"1",Hash(Canonical(body)));
        var result=Canonical(new { @ref=reference,body.demandRef,body.RequestId,body.RequestLineId,body.BoundQuantity,body.DemandSegments,
            body.sourceBudgetRevision,body.state,body.delegationReceipt });
        await using var add=Command("""
            INSERT INTO cp6_transfer_local_v1.bindings(environment_id,sandbox_id,tenant_id,budget_key,id,request_id,line_id,demand_version,
              input_bytes,result_bytes,state,quantity,request_ranges,demand_ranges,created_revision)
            VALUES(@e,@s,@t,@key,@id,@request,@line,@version,@input,@result,'ACTIVE',@quantity,@requests,@demands,@revision)
            """,("key",key),("id",i.Id),("request",i.RequestId),("line",i.RequestLineId),("version",budget.Version),
            ("input",bytes),("result",result),("quantity",quantity),("requests",requests),("demands",demands),("revision",revision));
        await add.ExecuteNonQueryAsync(ct);return Encoding.UTF8.GetString(result);
    }
    internal async Task<string> Release(string key, Guid binding, Guid request, Guid line, CancellationToken ct=default)
    {
        Require(key);await ReadBudget(key,ct);
        var original=await ReadBinding(binding,ct)??throw Error("TRANSFER_BINDING_NOT_FOUND");
        if(original.Key!=key || original.Request!=request || original.Line!=line)throw Error("TRANSFER_BINDING_OWNER_MISMATCH");
        if(original.State=="CLOSED")return "CLOSED";
        var revision=await Advance(key,ct);
        await using var cmd=Command("""
            UPDATE cp6_transfer_local_v1.bindings SET state='CLOSED',closed_revision=@revision
            WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND id=@id
            """,("id",binding),("revision",revision));await cmd.ExecuteNonQueryAsync(ct);return "CLOSED";
    }
    internal async Task<string?> ReadState(string key, Guid binding, CancellationToken ct=default)
    {
        Require(key);var original=await ReadBinding(binding,ct);return original?.Key==key?original.State:null;
    }
}
