using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    public Task<HttpResult> PostAsync(string operation, JsonElement body, string? ifMatch, string? commandKey,
        Guid? financeContext, CancellationToken ct)
    {
        if (operation is "QUERY" or "HISTORY")
        {
            if (ifMatch is not null || commandKey is not null) throw Invalid("PRECONDITION_SYNTAX");
            return owner.ExecuteAsync(Resource, operation == "HISTORY" ? "history" : View, false,
                async (access, token) => new HttpResult(operation == "HISTORY"
                    ? await History(access, body, token) : await List(access, body, token)), ct);
        }
        if (operation is "CREATE_PREVIEW" or "PROFILE_PREVIEW")
        {
            if (ifMatch is not null || commandKey is not null) throw Invalid("PRECONDITION_SYNTAX");
            return owner.ExecuteAsync(Resource, View, true,
                (access, token) => Preview(access, operation, body, financeContext, token), ct);
        }
        var command = ParseId(commandKey);
        return owner.ExecuteAsync(Resource, View, true, async (access, token) =>
        {
            var fingerprint = Sha(Canonical(new
            {
                actor = access.ActorId, operation, ifMatch, body,
                path = "/api/main/v1/bp/" + operation.ToLowerInvariant().Replace('_', '-')
            }));
            var prior = await FindCommand(command, token);
            if (prior is not null)
                return new HttpResult(await Replay(access, prior, fingerprint, token));
            return operation switch
            {
                "CREATE" or "PROFILE_APPLY" => new HttpResult(await ApplyPreview(access, operation, body, ifMatch, command, fingerprint, financeContext, token)),
                "REGISTER" or "HOLD" or "RETIRE" or "RESTORE" => new HttpResult(await ChangeState(access, operation, body, ifMatch, command, fingerprint, financeContext, token)),
                _ => throw Invalid()
            };
        }, ct, scopedActionAtFence: async (subject, token) =>
        {
            // 同一真实授权事务内查看原命令。已有结果不要求当前的新写权限。
            var prior = await FindCommand(command, token);
            return prior?.Actor == subject ? CommandRead : View;
        });
    }

    private sealed record Proposal(Scope Scope, BusinessPartner Row, Head? Head, CandidateSet Candidates, string Sources,
        Issue[] Issues, Dictionary<string, Guid?> Slots, Dictionary<string, string> RelationCodes, FinanceLink[] Finance, string? Reason);

    private async Task<Proposal> ProposeCreate(OperationsAccessV1 access, CreateIntent intent, Scope scope,
        Guid? financeContext, CancellationToken ct)
    {
        var bases = await BasesAsync(access, scope.Key, ct);
        await AuthorizeCreate(access, intent, bases, ct);
        var options = await OptionsAsync(scope.Key, ct);
        var (candidate, issue) = await Candidates(access, scope, intent.Row, ct);
        var (relations, codes, relationIssues) = await ResolveRelations(access, scope.Key, intent.Row,
            new Dictionary<string, Guid?>(), intent.Relations, requireAll: true, ct);
        var findings = new List<Issue>(Consistency(intent.Row));
        findings.AddRange(CodeIssues(intent.Row, options));
        findings.AddRange(relationIssues);
        findings.AddRange(CandidateDecision(intent, candidate, issue));
        if (intent.Decision.Kind == "NEW_DISTINCT" && await CodeExists(intent.Row, ct))
            findings.Add(new("bpCd", "BP_CODE_EXISTS"));
        if (intent.Registration == Registered) findings.AddRange(MissingRequirements(intent.Row, relations));
        findings.AddRange(await FinanceIssues(access, null, scope.Key, intent.Finance, financeContext, ct));
        return new(scope, intent.Row, null, candidate, SourceFingerprint(bases, options), findings.Distinct().ToArray(),
            relations, codes, intent.Finance, intent.Decision.Reason);
    }

    private async Task<Proposal> ProposeEdit(OperationsAccessV1 access, EditIntent intent, Scope scope,
        BusinessPartner row, Head head, Guid? financeContext, CancellationToken ct)
    {
        if (head.Revision != intent.Revision) throw Rejected("BP_VERSION_STALE", 412);
        var edit = await access.ActionAsync(Resource, "edit", ct);
        var bases = await BasesAsync(edit, head.Legal, ct);
        RequireBase(bases, Base(row), true);
        var writable = await WritableFields(edit, ct);
        if (intent.Changes.Any(x => !writable.Contains(x.Field, StringComparer.Ordinal)) ||
            intent.Relations.Length > 0 && !edit.CanWritePartnerField("Relations") ||
            intent.Finance.Length > 0 && !edit.CanWritePartnerField("FinanceLinks"))
            throw Denied("FIELD_WRITE_FORBIDDEN");

        var options = await OptionsAsync(head.Legal, ct);
        var beforeIssues = Consistency(row);
        ApplyFields(row, intent.Changes);
        RequireBase(bases, Base(row), true);
        var touched = intent.Changes.Select(x => x.Field).ToHashSet(StringComparer.Ordinal);
        var issues = Consistency(row).Except(beforeIssues.Where(x => !touched.Contains(x.Field))).ToList();
        issues.AddRange(CodeIssues(row, options, touched));
        var priorSlots = (await ReadSlotsAsync(row.Id, ct)).ToDictionary(x => x.Slot, x => x.TargetId, StringComparer.Ordinal);
        var (slots, codes, relationIssues) = await ResolveRelations(access, head.Legal, row, priorSlots, intent.Relations, false, ct);
        issues.AddRange(relationIssues);
        issues.AddRange(await FinanceIssues(access, head, scope.Key, intent.Finance, financeContext, ct));
        var original = PriorFinance(head).ToDictionary(x => x.Kind, StringComparer.Ordinal);
        foreach (var item in intent.Finance) original[item.Kind] = item;
        var empty = new CandidateSet("COMPLETE", [], Hash(new { scope.Key, head.Id, head.Revision }), Revision(scope.Revision));
        return new(scope, row, head, empty, SourceFingerprint(bases, options, touched), issues.ToArray(), slots,
            codes, original.Values.OrderBy(x => x.Kind, StringComparer.Ordinal).ToArray(), intent.Reason);
    }

    private static string SourceFingerprint(BaseChoice[] bases, List<Option> options, HashSet<string>? touched = null)
        => Hash(new
        {
            bases, options = options.Where(x => touched is null || touched.Contains(x.Field)).Select(x =>
                new { x.Field, x.Code, x.Label, x.Owner, x.SourceId, x.Version, x.Digest, x.Enabled }).ToArray()
        });

    private async Task<HttpResult> Preview(OperationsAccessV1 access, string mode, JsonElement body, Guid? financeContext, CancellationToken ct)
    {
        Proposal proposal;
        var operation = mode == "CREATE_PREVIEW" ? "CREATE" : "PROFILE_APPLY";
        if (operation == "CREATE")
        {
            var create = ParseCreate(body);
            var scope = await ScopeAsync(create.Legal, false, ct);
            proposal = await ProposeCreate(access, create, scope, financeContext, ct);
        }
        else
        {
            var edit = ParseEdit(body);
            var visible = await VisibleAsync(access, edit.Id, false, ct);
            var head = Adopted(visible.Head);
            var scope = await ScopeAsync(head.Legal, false, ct);
            proposal = await ProposeEdit(access, edit, scope, visible.Row, head, financeContext, ct);
        }
        var scopeRevision = proposal.Scope.Revision;
        var newId = Guid.NewGuid();
        var stamp = Now.AddMinutes(10);
        var expires = stamp.AddTicks(-(stamp.UtcTicks % 10));
        var valid = proposal.Issues.Length == 0;
        var fingerprint = Hash(new { operation, body, legal = proposal.Scope.Key, scopeRevision, proposal.Sources, proposal.Candidates.SetHash });
        var canonical = Canonical(body);
        byte[] cipher;
        try { cipher = Protect("preview", newId).Protect(canonical); }
        finally { CryptographicOperations.ZeroMemory(canonical); }
        var target = proposal.Head?.Id;
        var rowVersion = proposal.Head?.RowVersion;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.previews(tenant_id,preview_id,actor_id,legal_entity_key,scope_revision,operation,permission_version,
                intent_hash,source_hash,candidate_hash,protected_body,bp_id,row_version,expires_at,can_apply)
            VALUES({Tenant},{newId},{access.ActorId},{proposal.Scope.Key},{scopeRevision},{operation},{access.ReadScopeVersion},
                {fingerprint},{proposal.Sources},{proposal.Candidates.SetHash},{cipher},{target},{rowVersion},{expires},{valid})
            """, ct);
        return new HttpResult(new
        {
            reference = new PreviewReference(newId, fingerprint, Instant(expires)), canApply = valid,
            issues = proposal.Issues, candidates = proposal.Candidates,
            expectedBpRevision = proposal.Head is null ? null : Revision(proposal.Head.Revision), expectedMappingRevision = (string?)null,
            canonicalIntentHash = fingerprint
        }, PreviewTag(newId, fingerprint));
    }

    private async Task<CommandResponse> ApplyPreview(OperationsAccessV1 access, string operation, JsonElement body, string? ifMatch,
        Guid command, byte[] fingerprint, Guid? financeContext, CancellationToken ct)
    {
        var input = Exact(body, "preview");
        var preview = Exact(input["preview"], "previewId", "intentHash", "expiresAt");
        var id = Uuid(preview["previewId"]);
        var hash = Digest(preview["intentHash"]);
        var saved = await FindPreview(id, ct);
        if (saved is null || saved.Actor != access.ActorId) throw Missing();
        var scope = await ScopeAsync(saved.Legal, true, ct);
        if (saved.UsedCommand is not null) throw Rejected("PREVIEW_ALREADY_USED", 409);
        RequireStrongTag(ifMatch, PreviewTag(id, saved.IntentHash), stale: true);
        if (saved.IntentHash != hash || saved.Operation != operation || saved.Expires <= Now ||
            Instant(saved.Expires) != Token(preview["expiresAt"], 40) || saved.Permission != access.ReadScopeVersion) throw Stale();

        var plain = Protect("preview", id).Unprotect(saved.Body);
        JsonElement intent;
        try
        {
            using var json = JsonDocument.Parse(plain);
            intent = json.RootElement.Clone();
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        CommandResponse response;
        if (operation == "CREATE")
        {
            var create = ParseCreate(intent);
            if (create.Legal != scope.Key) throw Stale();
            var authorized = await BasesAsync(access, scope.Key, ct);
            await AuthorizeCreate(access, create, authorized, ct);
            if (create.Decision.Kind == "NEW_DISTINCT" && await CodeExists(create.Row, ct)) throw Rejected("BP_CODE_EXISTS", 409);
            if (saved.ScopeRevision != scope.Revision) throw Stale();
            var proposal = await ProposeCreate(access, create, scope, financeContext, ct);
            RequirePreviewMatches(saved, proposal);
            if (create.Decision.Kind == "USE_EXISTING")
            {
                var result = await VisibleAsync(access, create.Decision.Existing!.BpId, false, ct);
                var target = Adopted(result.Head);
                response = await Record(access, command, fingerprint, operation, result.Row, target, false, null, [], false, ct);
            }
            else
            {
                var row = create.Row;
                row.Id = Guid.NewGuid();
                var relations = proposal.Slots.ToDictionary(x => x.Key, x => x.Value == Guid.Empty ? row.Id : x.Value, StringComparer.Ordinal);
                ProjectRelations(row, proposal.RelationCodes, Slots.Select(x => x.Key));
                await InsertRoot(row, access, ct);
                var head = await SaveRevision(row, null, scope, create.Registration, relations, proposal.Finance, true, Now, ct);
                response = await Record(access, command, fingerprint, operation, row, head, true, proposal.Reason, [], true, ct);
            }
        }
        else
        {
            var edit = ParseEdit(intent);
            var visible = await VisibleAsync(access, edit.Id, true, ct);
            var head = Adopted(visible.Head);
            if (head.Legal != scope.Key || saved.Target != edit.Id || saved.ScopeRevision != scope.Revision ||
                !(saved.RowVersion ?? []).SequenceEqual(head.RowVersion)) throw Stale();
            var before = ProfileFingerprint(visible.Row);
            var beforeRelations = (await ReadSlotsAsync(edit.Id, ct)).ToDictionary(x => x.Slot, x => x.TargetId, StringComparer.Ordinal);
            var beforeFinance = PriorFinance(head);
            var proposal = await ProposeEdit(access, edit, scope, visible.Row, head, financeContext, ct);
            RequirePreviewMatches(saved, proposal);
            var changed = before != ProfileFingerprint(visible.Row) ||
                beforeRelations.Any(x => proposal.Slots[x.Key] != x.Value) || !beforeFinance.SequenceEqual(proposal.Finance);
            if (changed)
            {
                ProjectRelations(visible.Row, proposal.RelationCodes, edit.Relations.Select(x => x.Slot));
                await TouchRoot(visible.Row, access, ct);
                head = await SaveRevision(visible.Row, head, scope, head.Registration, proposal.Slots,
                    proposal.Finance, edit.Relations.Length != 0, Now, ct);
            }
            response = await Record(access, command, fingerprint, operation, visible.Row, head, changed, edit.Reason,
                edit.Changes.Select(x => x.Field).Concat(edit.Relations.Select(x => x.Slot)).Concat(edit.Finance.Select(x => "finance." + x.Kind)).ToArray(),
                false, ct);
        }
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.previews SET used_command={command} WHERE tenant_id={Tenant} AND preview_id={id} AND used_command IS NULL
            """, ct) != 1) throw Rejected("PREVIEW_ALREADY_USED", 409);
        return response;
    }

    private static string ProfileFingerprint(BusinessPartner row)
        => Hash(new { name = Name(row), abbreviation = row.BpAbbrev?.TrimEnd(), @base = Base(row), currency = row.CurrencyCd?.TrimEnd(), profile = Profile(row) });

    private static void RequirePreviewMatches(SavedPreview saved, Proposal proposal)
    {
        if (saved.SourceHash != proposal.Sources || saved.CandidateHash != proposal.Candidates.SetHash) throw Stale();
        RejectIssues(proposal.Issues);
        if (!saved.CanApply) throw Stale();
    }

    private static StateRequest ParseState(string operation, JsonElement body)
    {
        var properties = new[] { "bpId", "expectedRevision", "reason" };
        var fields = operation switch
        {
            "HOLD" => Exact(body, [.. properties, "hold"]),
            "RESTORE" => Exact(body, [.. properties, "confirmRegistration"]),
            _ => Exact(body, properties)
        };
        var hold = operation == "HOLD" ? Key(fields["hold"], 6) : null;
        if (operation == "HOLD" && hold is not (Clear or Frozen)) throw Invalid();
        return new(Uuid(fields["bpId"]), Version(fields["expectedRevision"]), Text(fields["reason"], 200), hold,
            operation == "RESTORE" && Flag(fields["confirmRegistration"]));
    }

    private async Task<CommandResponse> ChangeState(OperationsAccessV1 access, string operation, JsonElement body, string? ifMatch,
        Guid command, byte[] fingerprint, Guid? financeContext, CancellationToken ct)
    {
        var choice = ParseState(operation, body);
        var visible = await VisibleAsync(access, choice.Id, true, ct);
        var head = Adopted(visible.Head);
        var scope = await ScopeAsync(head.Legal, true, ct);
        var action = await access.ActionAsync(Resource, operation == "REGISTER" ? "register" : "status", ct);
        RequireBase(await BasesAsync(action, scope.Key, ct), Base(visible.Row), true);
        RequireStrongTag(ifMatch, RowTag(choice.Id, head.Revision, head.RowVersion));
        if (choice.Revision != head.Revision) throw Rejected("BP_VERSION_STALE", 412);
        var slots = (await ReadSlotsAsync(choice.Id, ct)).ToDictionary(x => x.Slot, x => x.TargetId, StringComparer.Ordinal);
        var finance = PriorFinance(head);
        var row = visible.Row;
        var before = StateOf(row, head, head.Revision);
        var registration = head.Registration;
        if (operation == "REGISTER")
        {
            if (before.Lifecycle != Active || registration == UnknownLegacy) throw Rejected("REGISTRATION_NOT_ALLOWED");
            if (registration != Registered)
            {
                await RequireRegistration(access, scope, row, head, slots, finance, financeContext, ct);
                registration = Registered;
                row.Status = 1;
            }
        }
        else if (operation == "HOLD") row.IsFrozen = choice.RequestedHold == Frozen;
        else if (operation == "RETIRE")
        {
            row.IsDeleted = true;
            row.Status = 9;
        }
        else if (before.Lifecycle == Retired)
        {
            if (registration == UnknownLegacy && !choice.ConfirmRegistration)
                throw Rejected("REGISTRATION_CONFIRMATION_REQUIRED");
            if (choice.ConfirmRegistration)
            {
                await access.ActionAsync(Resource, "register", ct);
                registration = Registered;
            }
            if (registration == Registered)
                await RequireRegistration(access, scope, row, head, slots, finance, financeContext, ct);
            row.IsDeleted = false;
            row.Status = registration == Registered ? 1 : 0;
        }
        else if (choice.ConfirmRegistration) throw Rejected("ACTION_NOT_APPLICABLE");
        var changed = before.Registration != registration || before.Lifecycle != Lifecycle(row) || before.Hold != Hold(row);
        if (changed)
        {
            await TouchRoot(row, access, ct);
            head = await SaveRevision(row, head, scope, registration, slots, finance, false, Now, ct);
        }
        var recordedOperation = operation == "HOLD" ? (choice.RequestedHold == Frozen ? "FREEZE" : "UNFREEZE") : operation;
        return await Record(access, command, fingerprint, recordedOperation, row, head, changed, choice.Reason, [], true, ct);
    }

    private async Task RequireRegistration(OperationsAccessV1 access, Scope scope, BusinessPartner row, Head head,
        Dictionary<string, Guid?> slots, FinanceLink[] finance, Guid? context, CancellationToken ct)
    {
        var options = await OptionsAsync(scope.Key, ct);
        var (_, _, relationIssues) = await ResolveRelations(access, scope.Key, row, slots, [], true, ct);
        var issues = MissingRequirements(row, slots).Concat(Consistency(row)).Concat(CodeIssues(row, options)).Concat(relationIssues).ToList();
        if (row.CurrencyCd is null) issues.Add(new("commercialCurrency", "REFERENCE_UNPROVEN"));
        issues.AddRange(await FinanceIssues(access, head, scope.Key, finance, context, ct));
        RejectIssues(issues.ToArray());
    }

    private async Task<CommandResponse> Replay(OperationsAccessV1 access, SavedCommand prior, byte[] fingerprint, CancellationToken ct)
    {
        if (prior.Actor != access.ActorId) throw Missing();
        var visible = await VisibleAsync(access, prior.Target, false, ct);
        if (visible.Head?.Legal != prior.Legal) throw Missing();
        if (!CryptographicOperations.FixedTimeEquals(prior.Fingerprint, fingerprint))
            throw Rejected("COMMAND_IDEMPOTENCY_CONFLICT", 409);
        return new(ReadReceipt(prior), true);
    }

    private async Task<HttpResult> CommandReadAsync(OperationsAccessV1 access, Guid command, CancellationToken ct)
    {
        var original = await FindCommand(command, ct);
        Receipt? receipt = null;
        if (original?.Actor == access.ActorId)
        {
            var current = await VisibleAsync(access, original.Target, false, ct);
            if (current.Head?.Legal == original.Legal) receipt = ReadReceipt(original);
        }
        return new HttpResult(new { knowledge = receipt is null ? "NOT_OBSERVED" : "FOUND",
            receipt, replayed = receipt is not null, noEffectProven = false });
    }

    private static Receipt ReadReceipt(SavedCommand original)
    {
        var receipt = JsonSerializer.Deserialize<Receipt>(original.Original, Json);
        if (receipt is null || receipt.CommandId != original.Id || receipt.Operation != original.Operation ||
            receipt.Bp.BpId != original.Target || receipt.Bp.LegalEntityKey != original.Legal || receipt.ReceiptHash != ReceiptHash(receipt))
            throw Rejected("BP_COMMAND_ORIGINAL_UNPROVEN", 503);
        return receipt;
    }

    private static string ReceiptHash(Receipt receipt) => Hash(new
    {
        receipt.CommandId, receipt.Operation, receipt.Outcome, receipt.Bp, receipt.Mapping, receipt.Reconcile,
        receipt.ErrorCode, receipt.CompletedAt
    });

    private Task<bool> WorkspaceInstalled(CancellationToken ct)
        => db.Database.SqlQueryRaw<bool>("SELECT to_regclass('main_bp.heads') IS NOT NULL AS \"Value\"").SingleAsync(ct);

    internal async Task RequireLegacyCreateAsync(string baseCd, CancellationToken ct)
    {
        if (!await WorkspaceInstalled(ct)) return;
        if (await UsesFormalBase(baseCd.TrimEnd(), baseCd.TrimEnd(), ct))
            throw Rejected("BP_CREATE_PREVIEW_REQUIRED", 409);
    }

    private Task<bool> UsesFormalBase(string existing, string proposed, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM main_bp.bases b JOIN main_bp.scopes s USING(tenant_id,legal_entity_key)
                WHERE b.tenant_id={Tenant} AND b.base_cd IN ({existing},{proposed}) AND b.enabled AND s.enabled) AS "Value"
            """).SingleAsync(ct);

    internal async Task RequireLegacyEditAsync(BusinessPartner row, string? proposedBase, CancellationToken ct)
    {
        if (!await WorkspaceInstalled(ct)) return;
        if (await HeadAsync(row.Id, ct) is not null)
            throw Rejected("BP_PROFILE_PREVIEW_REQUIRED", 409);
        if (await UsesFormalBase(Base(row), proposedBase?.TrimEnd() ?? Base(row), ct))
            throw Rejected("BP_LEGAL_SCOPE_UNPROVEN", 409);
    }

    internal async Task<bool> RetireCompatibilityAsync(OperationsAccessV1 access, BusinessPartner row, byte[]? version, CancellationToken ct)
    {
        if (!await WorkspaceInstalled(ct)) return false;
        var head = await HeadAsync(row.Id, ct);
        if (head is null) return false;
        if (version is null || !version.SequenceEqual(row.RowVersion ?? []) || !version.SequenceEqual(head.RowVersion))
            throw new DbUpdateConcurrencyException("该业务伙伴已被其他操作修改。");
        var scope = await ScopeAsync(head.Legal, true, ct);
        RequireBase(await BasesAsync(access, scope.Key, ct), Base(row), true);
        var slots = (await ReadSlotsAsync(row.Id, ct)).ToDictionary(x => x.Slot, x => x.TargetId, StringComparer.Ordinal);
        var links = PriorFinance(head);
        row.IsDeleted = true;
        row.Status = 9;
        await TouchRoot(row, access, ct);
        var revision = await SaveRevision(row, head, scope, head.Registration, slots, links, false, Now, ct);
        var id = Guid.NewGuid();
        await Record(access, id, Sha(Canonical(new { bpId = row.Id, rowVersion = version, access.ActorId, commandId = id })), "RETIRE", row,
            revision, true, "旧入口退休", [], true, ct);
        return true;
    }
}
