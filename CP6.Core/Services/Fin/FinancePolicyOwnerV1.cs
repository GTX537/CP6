using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.FinancePolicyValuesV1;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

public sealed class FinancePolicyOwnerV1(CP6Context db, OperationsOwnerV1 authority, ManufacturingFinanceScopeV1 scope,
    ControlledDocumentOwnerV1 documents) : ICostPolicyExternalSourceV1
{
    public string Owner => FinancePolicyValuesV1.Owner;

    public Task<JsonElement> SubmitAsync(Guid context, FinancePolicySubmitV1 input, CancellationToken ct)
        => Write(context, Action(input.Kind, "submit"), "submit", input.CommandId, input, async (ledger, access, token) =>
        {
            var interval = Validate(input); Reason(input.Reason);
            var id = Guid.NewGuid(); var root = id; long version = 1;
            if (input.PreviousVersionId is Guid previous)
            {
                var old = await Read(ledger.Id, previous, token);
                if (old.Kind != input.Kind || old.Original == null
                    || await db.Database.SqlQuery<bool>($"""
                        SELECT EXISTS(SELECT FROM cp6_fin_policy_v1.versions
                        WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger.Id} AND previous_id={previous}) AS "Value"
                        """).SingleAsync(token)) throw Error("FINANCE_POLICY_SUCCESSOR_CONFLICT", 409);
                root = old.Root; version = checked(old.Version + 1);
            }
            var row = new Row { Id=id,Root=root,Version=version,Kind=input.Kind,Maker=access.ActorId,
                Body=input.Definition.GetRawText(),Evidence=Encode(input.ApprovalBasisRef),From=interval.From,Until=interval.ToExclusive,
                State="SUBMITTED",Revision=1 };
            await Evidence(ledger, row, access, false, token);
            Persisted(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_policy_v1.versions(tenant_id,ledger_id,id,root_id,version,previous_id,kind,maker,body,evidence,reason,valid_from,to_exclusive)
                VALUES ({db.CurrentTenantId},{ledger.Id},{id},{root},{version},{input.PreviousVersionId},{input.Kind},{access.ActorId},
                    CAST({row.Body} AS jsonb),CAST({row.Evidence} AS jsonb),{input.Reason},{row.From},{row.Until});
                INSERT INTO cp6_fin_policy_v1.controls(tenant_id,ledger_id,id,state,revision)
                VALUES ({db.CurrentTenantId},{ledger.Id},{id},'SUBMITTED',1);
                """, token), 2);
            return Snapshot(row);
        }, ct);

    public Task<JsonElement> ApproveAsync(Guid context, Guid id, FinancePolicyDecisionV1 input, CancellationToken ct)
        => Change(context, id, "approve", input.CommandId, input, input.ExpectedRevision, input.Reason,
            row => ReviewAction(row.Kind, input.Role), async (ledger, row, access, token) =>
        {
            if (row.State != "SUBMITTED") throw Error("FINANCE_POLICY_STATE_CONFLICT", 409);
            var approvals = await Approvals(ledger.Id, id, token);
            if (row.Maker == access.ActorId || approvals.Any(a => a.Actor == access.ActorId))
                throw Error("FINANCE_REVIEW_INDEPENDENCE_REQUIRED", 403);
            if (approvals.Any(a => a.Role == input.Role)) throw Error("FINANCE_POLICY_APPROVAL_CONFLICT", 409);
            var action = ReviewAction(row.Kind, input.Role);
            var qualification = await scope.QualificationAsync(ledger.Id, access.ActorId, action, token);
            var original = await QualificationOriginal(ledger.Id, qualification, token);
            var evidence = await Evidence(ledger, row, access, true, token);
            if (row.Kind == "ACCOUNTING_POLICY") await ContractForPolicy(ledger, row, access, token);
            access.AtCommit(async t => { await scope.QualificationAsync(ledger.Id, access.ActorId, action, t); });
            Persisted(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_policy_v1.approvals(tenant_id,ledger_id,id,role,actor,qualification_id,qualification_original,evidence_original,reason)
                VALUES ({db.CurrentTenantId},{ledger.Id},{id},{input.Role},{access.ActorId},{qualification},{original},{evidence},{input.Reason})
                """, token));
            row.State = row.Kind == "ACCOUNTING_POLICY" || approvals.Length == 1 ? "APPROVED" : "SUBMITTED";
        }, ct);

    public Task<JsonElement> PublishAsync(Guid context, Guid id, FinancePolicyControlV1 input, CancellationToken ct)
        => Change(context, id, "publish", input.CommandId, input, input.ExpectedRevision, input.Reason,
            row => Action(row.Kind, "release"), async (ledger, row, access, token) =>
        {
            if (row.State != "APPROVED") throw Error("FINANCE_POLICY_STATE_CONFLICT", 409);
            if (row.Maker == access.ActorId) throw Error("FINANCE_REVIEW_INDEPENDENCE_REQUIRED", 403);
            var release = Action(row.Kind, "release");
            var qualification = await scope.QualificationAsync(ledger.Id, access.ActorId, release, token);
            var approvals = await RequireApprovals(ledger, row, access, token);
            await Evidence(ledger, row, access, true, token);
            if (row.Kind == "ACCOUNTING_POLICY") await ContractForPolicy(ledger, row, access, token);
            access.AtCommit(async t => { await scope.QualificationAsync(ledger.Id, access.ActorId, release, t); });
            var body = JsonNode.Parse(row.Body)!.AsObject();
            body["scope"] = JsonSerializer.SerializeToNode(ledger.Scope(db.CurrentTenantId), Wire);
            body["state"] = "APPROVED"; body["controlRevision"] = "1";
            if (row.Kind == "ACCOUNTING_POLICY")
            {
                body["mode"] = "REAL";
                body["qualificationRef"] = JsonSerializer.SerializeToNode(QualificationRef(approvals.Single()), Wire);
            }
            else
            {
                body["schema"] = "FINANCE-SOURCE-CONTRACT-1";
                body["approvals"] = JsonSerializer.SerializeToNode(approvals.Select(a => new { a.Role, a.Actor, qualificationRef=QualificationRef(a) }), Wire);
            }
            // 摘要覆盖原 Unicode、Scope、身份、版本和已批准正文；排除唯一自引用 ref 字段。
            var digest = ApprovalCanonicalV2.Hash([.."FINANCE-POLICY-CONTENT-1\n"u8,
                ..ApprovalCanonicalV2.Bytes(new { id=row.Root,version=Seq(row.Version),content=body })]);
            var reference = new CostPolicyRefV1(Owner, row.Root.ToString("D"), Seq(row.Version), digest, row.Kind);
            body["ref"] = JsonSerializer.SerializeToNode(reference, Wire);
            row.Original = body.ToJsonString(Wire); row.Reference = Encode(reference); row.State = "PUBLISHED";
            Persisted(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_policy_v1.originals(tenant_id,ledger_id,id,reference,original,publisher,qualification_id)
                VALUES ({db.CurrentTenantId},{ledger.Id},{row.Id},CAST({row.Reference} AS jsonb),{row.Original},{access.ActorId},{qualification})
                """, token));
        }, ct);

    public Task<JsonElement> RevokeAsync(Guid context, Guid id, FinancePolicyControlV1 input, CancellationToken ct)
        => Change(context, id, "revoke", input.CommandId, input, input.ExpectedRevision, input.Reason,
            row => Action(row.Kind, "revoke"), async (ledger, row, access, token) =>
        {
            if (row.State != "PUBLISHED") throw Error("FINANCE_POLICY_STATE_CONFLICT", 409);
            var purpose = Action(row.Kind, "release");
            await scope.QualificationAsync(ledger.Id, access.ActorId, purpose, token);
            access.AtCommit(async t => { await scope.QualificationAsync(ledger.Id, access.ActorId, purpose, t); });
            row.State = "REVOKED";
        }, ct);

    public Task<JsonElement> ReadAsync(Guid context, Guid id, CancellationToken ct)
        => scope.WithContextAsync(authority, context, View, false, async (ledger, access, token) =>
        {
            await Gate(false, token); var row = await Read(ledger.Id, id, token);
            await Evidence(ledger, row, access, false, token); return Snapshot(row);
        }, ct);

    public Task<JsonElement> CommandAsync(Guid context, Guid command, CancellationToken ct)
        => scope.WithContextAsync(authority, context, View, false, async (ledger, access, token) =>
        {
            await Gate(false, token);
            var record = await Command(ledger.Id, command, token);
            if (record != null) await RequireResultVisibility(ledger, access, record, token);
            return record == null ? JsonSerializer.SerializeToElement(new { kind="NOT_OBSERVED",commandId=command,
                provesPermanentNoEffect=false }, Wire) : Element(record.Result);
        }, ct);

    public async Task<CostPolicySourceOriginalV1> ReadInUowAsync(CostPolicySourceContextV1 context,
        CostPolicyRefV1 reference, string role, CancellationToken ct)
    {
        if (!ReferenceEquals(context.Database, db) || db.Database.CurrentTransaction == null)
            throw Error("COST_POLICY_UOW_REQUIRED", 503);
        if (role is not ("GL_ACCOUNTING_POLICY" or "SOURCE_CONTRACT")) throw Error("COST_POLICY_SOURCE_ROLE_MISMATCH", 422);
        var access = context.Access;
        await access.ActionAsync(OperationsOwnerV1.FinCost, View, ct);
        var ledger = (await scope.ListAsync(context.ActorId, ct)).SingleOrDefault(l => l.Scope(db.CurrentTenantId) == context.Scope)
            ?? throw Error("FINANCE_CONTEXT_NOT_FOUND", 404);
        await Gate(false, ct);
        var row = await Exact(ledger.Id, reference, ct);
        if (row.Kind != (role == "GL_ACCOUNTING_POLICY" ? "ACCOUNTING_POLICY" : "SOURCE_CONTRACT"))
            throw Error("COST_POLICY_SOURCE_ROLE_MISMATCH", 422);
        await Evidence(ledger, row, access, context.ForAdoption, ct);
        if (context.ForAdoption)
        {
            await RequireCurrent(ledger, row, access, context.BusinessDate, ct);
            if (row.Kind == "ACCOUNTING_POLICY")
            {
                var p = JsonSerializer.Deserialize<FinanceAccountingPolicyInputV1>(row.Body, Wire)!;
                if (p.SourceContractRef != context.ExpectedSourceContract || !p.EventTypes.Contains("COST_SETTLEMENT", StringComparer.Ordinal))
                    throw Error("FINANCE_POLICY_SOURCE_CONTRACT_MISMATCH", 422);
            }
            else
            {
                var c = JsonSerializer.Deserialize<FinanceSourceContractInputV1>(row.Body, Wire)!;
                if (c.SourceOwner != "Finance Costing" || c.SourceSchema != "COST-GL-SOURCE-1"
                    || !c.EventTypes.Contains("COST_SETTLEMENT", StringComparer.Ordinal))
                    throw Error("FINANCE_POLICY_SOURCE_CONTRACT_MISMATCH", 422);
            }
        }
        return new(reference, context.Scope, role, row.Original!);
    }

    private Task<JsonElement> Change<T>(Guid context, Guid id, string operation, Guid command, T input,
        string expected, string reason, Func<Row,string> action,
        Func<LedgerRow,Row,OperationsAccessV1,CancellationToken,Task> change, CancellationToken ct)
        => Write(context, View, $"{id:D}/{operation}", command, input, async (ledger, access, token) =>
        {
            Reason(reason); var row = await Read(ledger.Id, id, token);
            await access.ActionAsync(OperationsOwnerV1.FinCost, action(row), token);
            if (Seq(row.Revision) != expected) throw Error("FINANCE_POLICY_STALE", 412);
            await change(ledger, row, access, token); row.Revision = checked(row.Revision + 1);
            Persisted(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_fin_policy_v1.controls SET state={row.State},revision={row.Revision}
                WHERE (tenant_id,ledger_id,id)=({db.CurrentTenantId},{ledger.Id},{id})
                """, token));
            return Snapshot(row);
        }, ct);

    private Task<JsonElement> Write<T>(Guid context, string action, string route, Guid command, T input,
        Func<LedgerRow,OperationsAccessV1,CancellationToken,Task<JsonElement>> write, CancellationToken ct)
    {
        Input(command != Guid.Empty);
        var request = ApprovalCanonicalV2.Bytes(input);
        Replay? prior = null;
        return scope.WithContextAsync(authority, context, action, true, async (ledger, access, token) =>
        {
            if (prior != null)
            {
                if (prior.Actor != access.ActorId || prior.Route != route || !prior.Request.AsSpan().SequenceEqual(request))
                    throw Error("FINANCE_COMMAND_CONFLICT", 409);
                await RequireResultVisibility(ledger, access, prior, token);
                return Element(prior.Result);
            }
            var result = await write(ledger, access, token);
            Persisted(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_policy_v1.commands(tenant_id,ledger_id,command_id,actor,route,request,result)
                VALUES ({db.CurrentTenantId},{ledger.Id},{command},{access.ActorId},{route},{request},{result.GetRawText()})
                """, token));
            return result;
        }, ct, async (ledger, token) =>
        {
            await Gate(true, token); prior = await Command(ledger.Id, command, token);
            return prior == null ? action : View;
        });
    }

    private async Task ContractForPolicy(LedgerRow ledger, Row row, OperationsAccessV1 access, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<FinanceAccountingPolicyInputV1>(row.Body, Wire)!;
        var contract = await Exact(ledger.Id, p.SourceContractRef, ct);
        if (contract.Kind != "SOURCE_CONTRACT") throw Error("FINANCE_POLICY_SOURCE_CONTRACT_MISMATCH", 422);
        await RequireCurrent(ledger, contract, access, row.From, ct);
        var c = JsonSerializer.Deserialize<FinanceSourceContractInputV1>(contract.Body, Wire)!;
        if (p.EventTypes.Except(c.EventTypes, StringComparer.Ordinal).Any()
            || contract.Until.HasValue && (!row.Until.HasValue || row.Until > contract.Until))
            throw Error("FINANCE_POLICY_SOURCE_CONTRACT_MISMATCH", 422);
        await Evidence(ledger, contract, access, true, ct);
    }
    private async Task RequireCurrent(LedgerRow ledger, Row row, OperationsAccessV1 access, DateOnly date, CancellationToken ct)
    {
        if (row.State != "PUBLISHED" || date < row.From || row.Until.HasValue && date >= row.Until)
            throw Error("FINANCE_POLICY_SOURCE_UNAVAILABLE", 422);
        await RequireApprovals(ledger, row, access, ct);
    }
    private async Task<Approval[]> RequireApprovals(LedgerRow ledger, Row row, OperationsAccessV1 access, CancellationToken ct)
    {
        var approvals = await Approvals(ledger.Id, row.Id, ct);
        await Current(ct); access.AtCommit(Current); return approvals;
        async Task Current(CancellationToken token)
        {
            foreach (var a in approvals)
            {
                var action = ReviewAction(row.Kind, a.Role);
                await authority.RequireCurrentFinanceReviewerAsync(action, a.Actor, token);
                if (await scope.QualificationAsync(ledger.Id, a.Actor, action, token) != a.Qualification)
                    throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);
            }
        }
    }
    private async Task<string> Evidence(LedgerRow ledger, Row row, OperationsAccessV1 access, bool adopt, CancellationToken ct)
    {
        var reference = JsonSerializer.Deserialize<CostPolicyRefV1>(row.Evidence, Wire)!;
        var doc = await access.SourceAsync(OperationsOwnerV1.ControlledDocument, ct); doc.RequireDocumentFields(false);
        var original = await documents.ReviewSource(doc, new(reference.Owner, Guid.Parse(reference.Id), reference.Version, reference.Digest), adopt, ct, ledger.EnvironmentId);
        if (adopt && (original.State != "APPROVED" || original.Missing!.Length != 0))
            throw Error("FINANCE_POLICY_BASIS_UNAVAILABLE", 422);
        return original.OriginalJson;
    }
    private async Task<Row> Exact(Guid ledger, CostPolicyRefV1 reference, CancellationToken ct)
    {
        if (reference.Owner != Owner || !Guid.TryParseExact(reference.Id,"D",out var root)
            || !long.TryParse(reference.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || Seq(version) != reference.Version) throw Error("FINANCE_POLICY_REFERENCE_CHANGED", 422);
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_fin_policy_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger} AND root_id={root} AND version={version}
            """).ToArrayAsync(ct);
        if (ids.Length == 0) throw Error("NOT_FOUND_OR_NOT_VISIBLE", 404);
        var row = await Read(ledger, ids[0], ct);
        if (row.Reference == null || JsonSerializer.Deserialize<CostPolicyRefV1>(row.Reference, Wire) != reference)
            throw Error("FINANCE_POLICY_REFERENCE_CHANGED", 422);
        return row;
    }
    private async Task RequireResultVisibility(LedgerRow ledger, OperationsAccessV1 access, Replay replay, CancellationToken ct)
    {
        using var result = JsonDocument.Parse(replay.Result);
        var row = await Read(ledger.Id, result.RootElement.GetProperty("id").GetGuid(), ct);
        await Evidence(ledger, row, access, false, ct);
    }
    private async Task<Row> Read(Guid ledger, Guid id, CancellationToken ct)
        => await db.Database.SqlQuery<Row>($"""
            SELECT v.id AS "Id",v.root_id AS "Root",v.version AS "Version",v.kind AS "Kind",v.maker AS "Maker",
                v.body::text AS "Body",v.evidence::text AS "Evidence",v.valid_from AS "From",v.to_exclusive AS "Until",
                c.state AS "State",c.revision AS "Revision",o.original AS "Original",o.reference::text AS "Reference"
            FROM cp6_fin_policy_v1.versions v JOIN cp6_fin_policy_v1.controls c USING(tenant_id,ledger_id,id)
            LEFT JOIN cp6_fin_policy_v1.originals o USING(tenant_id,ledger_id,id)
            WHERE (v.tenant_id,v.ledger_id,v.id)=({db.CurrentTenantId},{ledger},{id})
            """).SingleOrDefaultAsync(ct) ?? throw Error("NOT_FOUND_OR_NOT_VISIBLE",404);
    private Task<Approval[]> Approvals(Guid ledger, Guid id, CancellationToken ct)
        => db.Database.SqlQuery<Approval>($"""
            SELECT role AS "Role",actor AS "Actor",qualification_id AS "Qualification",qualification_original AS "Original"
            FROM cp6_fin_policy_v1.approvals WHERE (tenant_id,ledger_id,id)=({db.CurrentTenantId},{ledger},{id}) ORDER BY role
            """).ToArrayAsync(ct);
    private Task<string> QualificationOriginal(Guid ledger, Guid id, CancellationToken ct)
        => db.Database.SqlQuery<string>($"""
            SELECT to_jsonb(d)::text AS "Value" FROM cp6_fin_mfg_v1.qualification_decisions d
            WHERE (tenant_id,ledger_id,id)=({db.CurrentTenantId},{ledger},{id})
            """).SingleAsync(ct);
    private Task<Replay?> Command(Guid ledger, Guid id, CancellationToken ct)
        => db.Database.SqlQuery<Replay>($"""
            SELECT actor AS "Actor",route AS "Route",request AS "Request",result AS "Result"
            FROM cp6_fin_policy_v1.commands WHERE (tenant_id,ledger_id,command_id)=({db.CurrentTenantId},{ledger},{id})
            """).SingleOrDefaultAsync(ct);
    private Task Gate(bool write, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(write
        ? "LOCK TABLE cp6_fin_policy_v1.controls IN SHARE ROW EXCLUSIVE MODE"
        : "LOCK TABLE cp6_fin_policy_v1.controls IN SHARE MODE", ct);
    private static string Seq(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonElement Element(string json)
    { using var parsed = JsonDocument.Parse(json); return parsed.RootElement.Clone(); }
    private static void Persisted(int actual, int expected = 1)
    { if (actual != expected) throw Error("FINANCE_POLICY_WRITE_INCOMPLETE", 503); }
    private static void Reason(string value) => Input(!string.IsNullOrEmpty(value) && value.EnumerateRunes().Count() <= 500 && !value.Any(char.IsControl));
    private static string ReviewAction(string kind, string role)
    { Input(role == "GL" || kind == "SOURCE_CONTRACT" && role == "SOURCE"); return Action(kind,kind == "ACCOUNTING_POLICY" ? "review" : role == "SOURCE" ? "source-review" : "gl-review"); }
    private static CostPolicyRefV1 QualificationRef(Approval approval)
        => new("Finance Costing",approval.Qualification.ToString("D"),"1",
            FinanceCostCommandCanonicalV1.Hex(FinanceCostCommandCanonicalV1.Digest(Element(approval.Original))),"POLICY_QUALIFICATION");
    private static JsonElement Snapshot(Row row) => JsonSerializer.SerializeToElement(new { row.Id,rootId=row.Root,version=Seq(row.Version),
        row.Kind,row.State,revision=Seq(row.Revision),definition=Element(row.Body),
        reference=row.Reference==null?null:JsonSerializer.Deserialize<CostPolicyRefV1>(row.Reference,Wire),originalJson=row.Original },Wire);
    private sealed class Row
    {
        public Guid Id {get;set;} public Guid Root {get;set;} public long Version {get;set;} public string Kind {get;set;}=null!;
        public Guid Maker {get;set;} public string Body {get;set;}=null!; public string Evidence {get;set;}=null!;
        public DateOnly From {get;set;} public DateOnly? Until {get;set;} public string State {get;set;}=null!; public long Revision {get;set;}
        public string? Original {get;set;} public string? Reference {get;set;}
    }
    private sealed class Approval
    { public string Role {get;set;}=null!; public Guid Actor {get;set;} public Guid Qualification {get;set;} public string Original {get;set;}=null!; }
    private sealed class Replay
    { public Guid Actor {get;set;} public string Route {get;set;}=null!; public byte[] Request {get;set;}=null!; public string Result {get;set;}=null!; }
}
