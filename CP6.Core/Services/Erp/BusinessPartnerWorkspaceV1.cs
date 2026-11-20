using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1(CP6Context db, OperationsOwnerV1 owner, IDataProtectionProvider protection,
    BusinessPartnerFinanceReferenceReader financeReader, TimeProvider clock, BusinessPartnerMappingAuthorityV1? mappingAuthority = null)
{
    public sealed record HttpResult(object Body, string? ETag = null);

    public Task<HttpResult> ReadAsync(string operation, Guid? id, string? legal, Guid? financeContext, CancellationToken ct)
    {
        if (operation == "COMMAND_READ")
            return owner.ExecuteAsync(Resource, CommandRead, false, (access, token) => CommandReadAsync(access, id!.Value, token), ct);
        return owner.ExecuteAsync(Resource, View, false, async (access, token) =>
        {
            if (operation == "SCOPES")
            {
                var bases = await BasesAsync(access, null, token);
                var keys = bases.Select(b => b.LegalEntityKey).Distinct(StringComparer.Ordinal).ToArray();
                var entries = await db.Database.SqlQuery<Scope>($"""
                    SELECT legal_entity_key AS "Key",label AS "Label",revision AS "Revision",norm_version AS "NormVersion",
                        coverage_complete AS "Coverage" FROM main_bp.scopes
                    WHERE tenant_id={Tenant} AND enabled AND legal_entity_key=ANY({keys}) ORDER BY legal_entity_key
                    """).ToArrayAsync(token);
                return new HttpResult(entries.Select(x => new { legalEntityKey = x.Key, name = x.Label }).ToArray());
            }
            if (operation == "OPTIONS")
            {
                var scope = await ScopeAsync(LegalKey(legal), false, token);
                return new HttpResult(await OptionsResult(access, scope, token));
            }
            var (row, head) = await VisibleAsync(access, id!.Value, false, token);
            var revision = head?.Revision ?? await LegacyRevision(row.Id, token);
            var tag = RowTag(row.Id, revision, row.RowVersion ?? throw Rejected("BP_PROFILE_FRONTIER_UNPROVEN", 503));
            var visible = await Readable(access, token);
            var detail = await Detail(access, row, head, revision, visible, financeContext, token);
            if (operation == "DETAIL") return new HttpResult(detail, tag);
            if (operation == "EDIT_CONTEXT")
            {
                var context = Adopted(head);
                var edit = await access.ActionAsync(Resource, "edit", token);
                RequireBase(await BasesAsync(edit, context.Legal, token), Base(row), true);
                var scope = await ScopeAsync(context.Legal, false, token);
                var edits = await WritableFields(edit, token);
                return new HttpResult(new { detail, options = await OptionsResult(access, scope, token), editableFields = edits }, tag);
            }
            if (operation == "STATUS_CONTEXT")
            {
                var status = await StatusActions(access, row, head, token);
                return new HttpResult(new
                {
                    bp = BpReference(row, head, revision, !visible.IsComplete), state = StateOf(row, head, revision), allowedActions = status,
                    eligibilities = StatusEligibility(row, head, revision, !visible.IsComplete),
                    issues = head is null ? new[] { new Issue("legalEntityKey", "BP_LEGAL_SCOPE_UNPROVEN") } : []
                }, tag);
            }
            throw Invalid();
        }, ct);
    }

    private sealed record ReadProjection(bool Sensitive, HashSet<string> Original)
    {
        internal bool IsComplete => Original.Count == BusinessPartnerProfileFieldsV1.Original.Length;
    }

    private static async Task<ReadProjection> Readable(OperationsAccessV1 access, CancellationToken ct)
    {
        var sensitive = await access.TryPartnerActionAsync("sensitive.read", ct) is not null;
        return new(sensitive, BusinessPartnerProfileFieldsV1.Original
            .Where(field => (!field.Sensitive || sensitive) && access.CanReadPartnerField(field.Original))
            .Select(field => field.Name).ToHashSet(StringComparer.Ordinal));
    }

    private static async Task<string[]> WritableFields(OperationsAccessV1 access, CancellationToken ct)
    {
        var sensitive = await access.TryPartnerActionAsync("sensitive.read", ct) is not null &&
                        await access.TryPartnerActionAsync("sensitive.write", ct) is not null;
        var core = CoreEditFields.Where(field => access.CanWritePartnerField(PermissionName(field)));
        return core.Concat(BusinessPartnerProfileFieldsV1.Writable
            .Where(field => access.CanWritePartnerField(field.Original) &&
                (!field.Sensitive || sensitive && access.CanReadPartnerField(field.Original)))
            .Select(field => field.Name)).ToArray();
    }

    private static string PermissionName(string field) => field switch
    {
        "bpName" => "BpName", "bpAbbrev" => "BpAbbrev", "baseCd" => "BaseCd", "commercialCurrency" => "CurrencyCd",
        _ => BusinessPartnerProfileFieldsV1.ByName[field].Original
    };

    private static Reference BpReference(BusinessPartner row, Head? head, long revision, bool hideHash = true)
        => new(row.Id, Code(row), head?.Legal, Revision(revision), hideHash ? null : head?.Hash);
    private static State StateOf(BusinessPartner row, Head? head, long revision)
        => new(head?.Registration ?? RegistrationOfLegacy(row), Lifecycle(row), Hold(row), Revision(revision), head is null ? "UNPROVEN" : "ADOPTED");

    private async Task<object> Detail(OperationsAccessV1 access, BusinessPartner row, Head? head, long revision,
        ReadProjection visible, Guid? financeContext, CancellationToken ct)
    {
        var redacted = BusinessPartnerProfileFieldsV1.Original.Where(field => !visible.Original.Contains(field.Name))
            .Select(field => field.Name).ToList();
        var profile = BusinessPartnerProfileFieldsV1.Writable.ToDictionary(field => field.Name,
            field => visible.Original.Contains(field.Name) ? NormalizeFixedColumn(field.Value(row)) : null, StringComparer.Ordinal);
        var relations = new List<object>();
        var slots = head is null ? [] : await ReadSlotsAsync(row.Id, ct);
        foreach (var slot in Slots)
        {
            var stored = slots.SingleOrDefault(item => item.Slot == slot.Key);
            Reference? target = null;
            var availability = head is null ? "UNPROVEN" : "NO_VALUE";
            if (stored?.TargetId is { } id)
            {
                if (!access.CanReadPartnerField("Relations")) availability = "NOT_AUTHORIZED";
                else if (id == row.Id)
                {
                    availability = "AVAILABLE";
                    target = BpReference(row, head, revision);
                }
                else
                {
                    try
                    {
                        var link = await VisibleAsync(access, id, false, ct);
                        if (link.Head?.Legal == head?.Legal && link.Head is { } source)
                        {
                            availability = "AVAILABLE";
                            target = BpReference(link.Row, source, source.Revision);
                        }
                        else availability = "UNPROVEN";
                    }
                    catch (OrderPriceCorrectionException ex) when (ex.Status == 404) { availability = "NOT_AUTHORIZED"; }
                }
            }
            relations.Add(new { slot = slot.Key, target, availability });
        }

        var finance = await FinanceDisplay(access, row, head, financeContext, ct);
        var legacy = BusinessPartnerProfileFieldsV1.Original.Where(field => !field.Writable && visible.Original.Contains(field.Name))
            .Select(field =>
            {
                var ownedByFinance = field.Original is "BankCd" or "BankBranchCd" or "RemittanceAccount" or "MainAccountRegDate";
                if (ownedByFinance) redacted.Add(field.Name);
                return new
                {
                    field = field.Name, value = ownedByFinance ? null : NormalizeFixedColumn(field.Value(row)), sourceRevision = Revision(revision),
                    state = ownedByFinance || head is null ? "UNPROVEN" : "VERIFIED_HISTORY", reason = ownedByFinance ? "FINANCE_OWNER" : "ORIGINAL_COLUMN"
                };
            }).ToArray();
        if (slots.Any(slot => slot.TargetId is not null) && !access.CanReadPartnerField("Relations")) redacted.Add("relations");
        var issues = head is null
            ? [new Issue("legalEntityKey", "BP_LEGAL_SCOPE_UNPROVEN")]
            : MissingRequirements(row, slots.ToDictionary(s => s.Slot, s => s.TargetId));
        if (row.CurrencyCd is null) issues = [.. issues, new Issue("commercialCurrency", "REFERENCE_UNPROVEN")];
        if (!access.CanReadPartnerField("CurrencyCd")) redacted.Add("commercialCurrency");
        return new
        {
            bp = BpReference(row, head, revision, redacted.Count > 0), name = Name(row), abbreviation = row.BpAbbrev?.TrimEnd(),
            baseCd = Base(row), commercialCurrency = access.CanReadPartnerField("CurrencyCd") ? row.CurrencyCd?.TrimEnd() : null,
            roles = RoleSet(row), profile, relations = relations.ToArray(), finance,
            state = StateOf(row, head, revision), issues, legacyFields = legacy, redactedFields = redacted.ToArray(), asOf = Instant(Now)
        };
    }

    private async Task<FinanceView[]> FinanceDisplay(OperationsAccessV1 access, BusinessPartner row, Head? head,
        Guid? financeContext, CancellationToken ct)
    {
        var originals = head is null ? [] : PriorFinance(head);
        var result = new List<FinanceView>();
        foreach (var kind in BankKinds)
        {
            var key = "finance." + kind;
            if (!access.CanReadPartnerField(key) || await access.TryPartnerActionAsync(BusinessPartnerFinanceReferenceReader.ReadAction, ct) is null)
            {
                result.Add(new(kind, null, "NOT_AUTHORIZED"));
                continue;
            }
            var given = originals.SingleOrDefault(x => x.Kind == kind)?.Reference;
            if (financeContext is null || head is null)
            {
                result.Add(new(kind, null, given is null && head is not null ? "NOT_CONFIGURED" : "UNPROVEN"));
                continue;
            }
            try
            {
                var exact = await financeReader.ReadProfileReferenceAsync(access, db.Database.CurrentTransaction!.GetDbTransaction(),
                    row.Id, financeContext.Value, head.Legal, key, ct);
                result.Add(exact.Availability switch
                {
                    "FOUND" when given is null || exact.Reference == given => new(kind, exact.Reference, "AVAILABLE",
                        MaskedAccount: exact.MaskedAccount, CreditLimit: exact.CreditLimit, Currency: exact.Currency),
                    "NOT_AUTHORIZED" => new(kind, null, "NOT_AUTHORIZED"),
                    _ => new(kind, null, given is null ? "NOT_CONFIGURED" : "UNPROVEN")
                });
            }
            catch (OrderPriceCorrectionException ex) when (ex.Status is 403 or 404 or 409 or 412 or 422)
            {
                result.Add(new(kind, null, ex.Status == 403 ? "NOT_AUTHORIZED" : "UNPROVEN"));
            }
        }
        return result.ToArray();
    }

    private async Task<object> OptionsResult(OperationsAccessV1 access, Scope scope, CancellationToken ct)
    {
        var bases = await BasesAsync(access, scope.Key, ct);
        if (bases.Length == 0) throw Missing();
        var options = await OptionsAsync(scope.Key, ct);
        var read = await Readable(access, ct);
        var add = await access.TryPartnerActionAsync("add", ct);
        var edit = await access.TryPartnerActionAsync("edit", ct);
        var register = await access.TryPartnerActionAsync("register", ct);
        var writable = add ?? edit;
        var canPropose = add is not null && await CanCreateFields(add, ct);
        var actions = new List<string> { "read" };
        if (canPropose) actions.Add("create");
        if (register is not null) actions.Add("register");
        if (edit is not null) actions.Add("profile.edit");
        if (await access.TryPartnerActionAsync("status", ct) is not null) actions.Add("status");
        if (await access.TryPartnerActionAsync(CommandRead, ct) is not null) actions.Add(CommandRead);
        foreach (var action in new[] { MappingAction, MappingRead, MappingCorrect, MappingReconcile })
            if (await access.TryPartnerActionAsync(action, ct) is not null) actions.Add(action);
        var fields = BusinessPartnerProfileFieldsV1.Writable.Select(field =>
        {
            var matching = options.Where(x => x.Field == field.Name && x.Owner == DirectoryOwner).ToArray();
            return new
            {
                field = field.Name, type = field.Type, nullable = field.Nullable, maxLength = field.Length,
                precision = field.Precision, scale = field.Scale, sourceOwner = field.SourceOwner,
                options = matching.Select(x => new { code = x.Code, label = x.Label, enabled = x.Enabled }).ToArray(),
                availability = !field.Dictionary || matching.Any(x => x.Enabled) ? "AVAILABLE" : "UNPROVEN"
            };
        }).ToArray();
        var dictionaryRefs = options.Where(x => x.Enabled)
            .Select(x => new BusinessPartnerFinanceSourceRefV1(x.Owner, x.SourceId, x.Version, x.Digest, x.Field))
            .Distinct().OrderBy(x => x.Owner, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Version, StringComparer.Ordinal);
        return new
        {
            legalEntityKey = scope.Key, scopeRevision = Revision(scope.Revision), permissionRevision = access.ReadScopeVersion,
            dictionaryRefs, bases = bases.Select(x => new { baseCd = x.BaseCd, legalEntityKey = x.LegalEntityKey, name = x.Name,
                canCreate = x.CanManage && canPropose,
                createModes = !x.CanManage || !canPropose ? [] : register is null ? new[] { PreRegistered } : new[] { PreRegistered, Registered } }),
            roles = Roles, fieldRules = fields, capabilities = actions, readFields = read.Original.Order(StringComparer.Ordinal),
            writeFields = writable is null ? [] : await WritableFields(writable, ct), asOf = Instant(Now)
        };
    }

    private async Task<string[]> StatusActions(OperationsAccessV1 access, BusinessPartner row, Head? head, CancellationToken ct)
    {
        if (head is null || !RequireBase(await BasesAsync(access, head.Legal, ct), Base(row), false).CanManage) return [];
        var actions = new List<string>();
        if (Lifecycle(row) == Active && head.Registration == PreRegistered && await access.TryPartnerActionAsync("register", ct) is not null)
            actions.Add("REGISTER");
        if (await access.TryPartnerActionAsync("status", ct) is not null)
        {
            actions.Add(row.IsFrozen ? "UNFREEZE" : "FREEZE");
            actions.Add(Lifecycle(row) == Active ? "RETIRE" : "RESTORE");
        }
        return actions.ToArray();
    }

    private object[] StatusEligibility(BusinessPartner row, Head? head, long revision, bool redact)
    {
        var reason = Lifecycle(row) == Retired ? "RETIRED" : row.IsFrozen ? "FROZEN" :
            head?.Registration == PreRegistered ? "PRE_REGISTERED" : "OWNER_PARTICIPANT_UNPROVEN";
        var deny = reason is "RETIRED" or "FROZEN" or "PRE_REGISTERED";
        return new[] { "SALES_NEW_SOURCE", "SALES_CONFIRM", "PURCHASE_NEW_SOURCE", "PURCHASE_RELEASE" }.Select(purpose => (object)new
        {
            purpose, knowledge = deny ? "KNOWN_DENY" : "UNKNOWN", reasons = new[] { reason },
            evaluatedBp = BpReference(row, head, revision, redact), dependencyRefs = System.Array.Empty<object>(),
            asOf = Instant(Now), isExecutionPermit = false
        }).ToArray();
    }
}
