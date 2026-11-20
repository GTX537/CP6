using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed record CreateIntent(string Legal, string Registration, BusinessPartner Row,
        Related[] Relations, FinanceLink[] Finance, DuplicateDecision Decision);
    private sealed record EditIntent(Guid Id, long Revision, Patch[] Changes, Related[] Relations,
        FinanceLink[] Finance, string Reason);

    private static CreateIntent ParseCreate(JsonElement json)
    {
        var input = Exact(json, "legalEntityKey", "registration", "identity", "duplicateDecision");
        var legal = LegalKey(Key(input["legalEntityKey"], 64));
        var registration = Key(input["registration"], 20);
        if (registration is not (PreRegistered or Registered)) throw Invalid();
        var data = Exact(input["identity"], "bpCd", "bpName", "bpAbbrev", "baseCd", "commercialCurrency", "roles", "profile", "relations", "financeLinks");
        var row = new BusinessPartner
        {
            Id = Guid.Empty, BpCd = Key(data["bpCd"], 20), BpName = Text(data["bpName"], 100),
            BpAbbrev = OptionalText(data["bpAbbrev"], 10), BaseCd = Key(data["baseCd"], 10),
            CurrencyCd = Key(data["commercialCurrency"], 3), Status = registration == Registered ? 1 : 0
        };
        if (!BusinessCode.IsMatch(row.BpCd) || !CurrencyCode.IsMatch(row.CurrencyCd)) throw Invalid();
        var roles = Exact(data["roles"], Roles).ToDictionary(x => x.Key, x => Flag(x.Value), StringComparer.Ordinal);
        if (!Roles.Take(9).Any(role => roles[role]) || roles["PaidSupply"] && !roles["Supplier"] ||
            roles["RebuyObligation"] && !roles["PaidSupply"]) throw Invalid("ROLE_SET_INVALID");
        SetRoles(row, roles);
        var profile = Exact(data["profile"], BusinessPartnerProfileFieldsV1.Writable.Select(f => f.Name).ToArray());
        foreach (var (name, value) in profile)
            BusinessPartnerProfileFieldsV1.ByName[name].Assign(row, value);
        return new(legal, registration, row, ParseRelations(data["relations"], complete: true), ParseFinance(data["financeLinks"], complete: true),
            ParseDecision(input["duplicateDecision"]));
    }

    private static EditIntent ParseEdit(JsonElement json)
    {
        var input = Exact(json, "bpId", "expectedRevision", "changes", "relationChanges", "financeLinkChanges", "reason");
        var changes = Array(input["changes"], 190).Select(item =>
        {
            var operation = Exact(item, "field", "value");
            var field = Key(operation["field"], 80);
            if (field == "roles" || Roles.Contains(field, StringComparer.Ordinal) ||
                Roles.Any(role => (char.ToLowerInvariant(role[0]) + role[1..] + "Flg") == field))
                throw Invalid("ROLE_IMMUTABLE");
            if (CoreIdentityFields.Contains(field, StringComparer.Ordinal)) throw Invalid("BP_SCOPE_IMMUTABLE");
            if (ReadOnlyFields.Contains(field, StringComparer.Ordinal) ||
                BusinessPartnerProfileFieldsV1.ByName.TryGetValue(field, out var info) && !info.Writable)
                throw Invalid("OWNER_FIELD_READ_ONLY");
            if (!CoreEditFields.Contains(field, StringComparer.Ordinal) && !BusinessPartnerProfileFieldsV1.ByName.ContainsKey(field))
                throw Invalid("UNKNOWN_FIELD");
            return new Patch(field, operation["value"]);
        }).ToArray();
        var relations = ParseRelations(input["relationChanges"], complete: false);
        var finance = ParseFinance(input["financeLinkChanges"], complete: false);
        if (changes.Select(x => x.Field).Distinct(StringComparer.Ordinal).Count() != changes.Length ||
            changes.Length + relations.Length + finance.Length == 0) throw Invalid();
        return new(Uuid(input["bpId"]), Version(input["expectedRevision"]), changes, relations, finance, Text(input["reason"], 200));
    }

    private static CandidateReference ParseCandidate(JsonElement json)
    {
        var item = Exact(json, "bpId", "revision", "candidateHash");
        return new(Uuid(item["bpId"]), Revision(Version(item["revision"])), Digest(item["candidateHash"]));
    }

    private static DuplicateDecision ParseDecision(JsonElement json)
    {
        var data = Object(json, ["kind", "acknowledged", "reason", "reference"], "kind");
        var kind = Key(data["kind"], 20);
        if (kind == "USE_EXISTING")
        {
            var exact = Exact(json, "kind", "reference");
            return new(kind, [], null, ParseCandidate(exact["reference"]));
        }
        if (kind != "NEW_DISTINCT") throw Invalid();
        var create = Exact(json, "kind", "acknowledged", "reason");
        var acknowledged = Array(create["acknowledged"], 50).Select(ParseCandidate).OrderBy(x => x.BpId.ToString("D"), StringComparer.Ordinal).ToArray();
        if (acknowledged.Select(x => x.BpId).Distinct().Count() != acknowledged.Length) throw Invalid();
        return new(kind, acknowledged, OptionalText(create["reason"], 200), null);
    }

    private static Related[] ParseRelations(JsonElement json, bool complete)
    {
        var items = Array(json, 9).Select(value =>
        {
            var item = Exact(value, "slot", "target");
            var slot = Key(item["slot"], 25);
            if (!Slots.Any(x => x.Key == slot)) throw Invalid("UNKNOWN_FIELD");
            if (item["target"].ValueKind == JsonValueKind.Null) return new Related(slot, null);
            var data = Object(item["target"], ["kind", "bpId"], "kind");
            var kind = Key(data["kind"], 5);
            if (kind == "SELF" && data.Count == 1) return new Related(slot, new RelationTarget(kind, null));
            if (kind == "BP" && data.Count == 2) return new Related(slot, new RelationTarget(kind, Uuid(data["bpId"])));
            throw Invalid("REFERENCE_INVALID");
        }).OrderBy(x => x.Slot, StringComparer.Ordinal).ToArray();
        if (items.Select(x => x.Slot).Distinct(StringComparer.Ordinal).Count() != items.Length || complete && items.Length != 9)
            throw Invalid("RELATION_SET_INVALID");
        return items;
    }

    private static FinanceLink[] ParseFinance(JsonElement json, bool complete)
    {
        var items = Array(json, 3).Select(value =>
        {
            var item = Exact(value, "kind", "reference");
            var kind = Key(item["kind"], 24);
            if (!BankKinds.Contains(kind, StringComparer.Ordinal)) throw Invalid("UNKNOWN_FIELD");
            if (item["reference"].ValueKind == JsonValueKind.Null) return new FinanceLink(kind, null);
            var reference = Exact(item["reference"], "owner", "id", "version", "digest", "purpose");
            return new FinanceLink(kind, new BusinessPartnerFinanceSourceRefV1(Key(reference["owner"], 128), Key(reference["id"], 128),
                Key(reference["version"], 128), Digest(reference["digest"]), Key(reference["purpose"], 128)));
        }).OrderBy(x => x.Kind, StringComparer.Ordinal).ToArray();
        if (items.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count() != items.Length || complete && items.Length != 3) throw Invalid();
        return items;
    }

    private static async Task<bool> CanCreateFields(OperationsAccessV1 actor, CancellationToken ct)
        => BusinessPartnerProfileFieldsV1.Core.All(actor.CanWritePartnerField) && actor.CanWritePartnerField("Relations") &&
           actor.CanWritePartnerField("FinanceLinks") && (await WritableFields(actor, ct)).Length ==
           CoreEditFields.Length + BusinessPartnerProfileFieldsV1.Writable.Length;

    private async Task AuthorizeCreate(OperationsAccessV1 access, CreateIntent intent, BaseChoice[] bases, CancellationToken ct)
    {
        var add = await access.ActionAsync(Resource, "add", ct);
        if (!await CanCreateFields(add, ct)) throw Denied("FIELD_WRITE_FORBIDDEN");
        RequireBase(bases, Base(intent.Row), true);
        if (intent.Registration == Registered) await access.ActionAsync(Resource, "register", ct);
    }

    private static void ApplyFields(BusinessPartner row, IEnumerable<Patch> patches)
    {
        foreach (var patch in patches)
        {
            switch (patch.Field)
            {
                case "bpName": row.BpName = Text(patch.Value, 100); break;
                case "bpAbbrev": row.BpAbbrev = OptionalText(patch.Value, 10); break;
                case "baseCd": row.BaseCd = Key(patch.Value, 10); break;
                case "commercialCurrency":
                    var currency = Key(patch.Value, 3);
                    if (!CurrencyCode.IsMatch(currency)) throw Invalid();
                    row.CurrencyCd = currency;
                    break;
                default: BusinessPartnerProfileFieldsV1.ByName[patch.Field].Assign(row, patch.Value); break;
            }
        }
    }

    private static Issue[] Consistency(BusinessPartner row)
    {
        var issues = new List<Issue>();
        var pattern = row.SupplierPattern?.TrimEnd();
        if (pattern is not (null or "1" or "2" or "3" or "4" or "5" or "9"))
            issues.Add(new("supplierPattern", "FIELD_INVALID"));
        if (pattern != "5" && (row.SubcontractTargetFlg || row.SubcontractPriceDiv != null || row.SupplyPostingDiv != null))
            issues.Add(new("supplierPattern", "FIELD_INVALID"));
        if (row.SupplierFlg && !row.DeliveryConfirmFlg)
            issues.Add(new("deliveryConfirmFlg", "FIELD_INVALID", "Supplier"));
        if ((row.DeliveryTimeFrom is null) != (row.DeliveryTimeTo is null) ||
            row.DeliveryTimeFrom is { } from && row.DeliveryTimeTo is { } to && string.CompareOrdinal(from, to) > 0)
            issues.Add(new("deliveryTimeFrom", "TIME_WINDOW_INVALID"));
        if ((row.Ein is null) != (row.EinType is null)) issues.Add(new("einType", "FIELD_INVALID"));
        return issues.ToArray();
    }

    private static Issue[] MissingRequirements(BusinessPartner row, IReadOnlyDictionary<string, Guid?> slots)
    {
        var issues = new List<Issue>();

        void Required(string role, params (string Field, object? Value)[] fields)
        {
            if (!HasRole(row, role)) return;
            foreach (var (name, value) in fields)
                if (value is null || value is string str && string.IsNullOrWhiteSpace(str))
                    issues.Add(new(name, "PROFILE_INCOMPLETE", role));
        }

        foreach (var slot in Slots)
            if (slot.Required) Required(slot.Source, (slot.Key, slots.GetValueOrDefault(slot.Key)));
        Required("Customer", ("salesStaffCd", row.SalesStaffCd), ("businessStaffCd", row.BusinessStaffCd));
        Required("Delivery", ("logisticsGroupCd", row.LogisticsGroupCd));
        Required("Supplier", ("supplierPattern", row.SupplierPattern), ("purchaseFractionDiv", row.PurchaseFractionDiv),
            ("purchaseTaxFractionDiv", row.PurchaseTaxFractionDiv), ("supplyConsignDiv", row.SupplyConsignDiv), ("purchaseLotSplitDiv", row.PurchaseLotSplitDiv));
        Required("PaymentSchedule", ("paymentScheduleDeptCd", row.PaymentScheduleDeptCd));
        if (!row.PaidEachTimeFlg) Required("PaymentSchedule", ("paymentClosingDay1", row.PaymentClosingDay1));
        if (row.PaymentTaxCalcFlg) Required("PaymentSchedule", ("paymentTaxFractionDiv", row.PaymentTaxFractionDiv));
        if (row.BatchPaymentScheduleFlg) Required("PaymentSchedule", ("paymentScheduleDelayDays", row.PaymentScheduleDelayDays));
        Required("PaidSupply", ("paidSupplyAmountFractionDiv", row.PaidSupplyAmountFractionDiv),
            ("paidSupplyTaxFractionDiv", row.PaidSupplyTaxFractionDiv), ("paidSupplyTaxCalcDiv", row.PaidSupplyTaxCalcDiv));
        Required("RebuyObligation", ("paidSupplyTaxCd", row.PaidSupplyTaxCd));
        return issues.ToArray();
    }

    private static Issue[] CodeIssues(BusinessPartner row, List<Option> options, HashSet<string>? changed = null)
    {
        var codes = BusinessPartnerProfileFieldsV1.Writable.Where(x => x.Dictionary)
            .Select(x => (x.Name, Value: NormalizeFixedColumn(x.Value(row)) as string))
            .Prepend((Name: "commercialCurrency", Value: row.CurrencyCd?.TrimEnd()))
            .Where(x => x.Item2 is not null && (changed is null || changed.Contains(x.Name)));
        return codes.Where(x => !options.Any(opt => opt.Field == x.Name && opt.Code == x.Item2 && opt.Enabled &&
                opt.Owner == DirectoryOwner && opt.Version.Length > 0 && opt.SourceId.Length > 0))
            .Select(x => new Issue(x.Name, "REFERENCE_UNPROVEN")).ToArray();
    }

    private async Task<(Dictionary<string, Guid?> Slots, Dictionary<string, string> Codes, Issue[] Issues)> ResolveRelations(
        OperationsAccessV1 access, string legal, BusinessPartner row, IReadOnlyDictionary<string, Guid?> persisted,
        Related[] changed, bool requireAll, CancellationToken ct)
    {
        var result = Slots.ToDictionary(slot => slot.Key, slot => persisted.GetValueOrDefault(slot.Key), StringComparer.Ordinal);
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        var issues = new List<Issue>();
        foreach (var intent in changed) result[intent.Slot] = intent.Target switch
        {
            null => null,
            { Kind: "SELF" } => row.Id,
            _ => intent.Target.BpId
        };
        foreach (var slot in Slots)
        {
            var touched = changed.SingleOrDefault(x => x.Slot == slot.Key);
            if (!requireAll && touched is null) continue;
            if (result[slot.Key] is not { } target) continue;
            var self = target == row.Id;
            if (!HasRole(row, slot.Source) || self && (slot.Key == "PARENT_CUSTOMER" || touched?.Target?.Kind == "BP"))
            {
                issues.Add(new(slot.Key, slot.Key == "PARENT_CUSTOMER" ? "RELATION_CYCLE" : "REFERENCE_INVALID", slot.Source));
                continue;
            }

            BusinessPartner related;
            if (self) related = row;
            else
            {
                try
                {
                    var current = await VisibleAsync(access, target, false, ct);
                    if (current.Head?.Legal != legal)
                    {
                        issues.Add(new(slot.Key, "RELATION_SCOPE_MISMATCH", slot.Source));
                        continue;
                    }
                    related = current.Row;
                }
                catch (OrderPriceCorrectionException error) when (error.Status == 404)
                {
                    issues.Add(new(slot.Key, "REFERENCE_INVALID", slot.Source));
                    continue;
                }
                if (slot.Key == "PARENT_CUSTOMER")
                {
                    var issue = await ParentChain(access, legal, row.Id, target, ct);
                    if (issue is not null) { issues.Add(new(slot.Key, issue, slot.Source)); continue; }
                }
            }
            if (!slot.Targets.All(role => HasRole(related, role)))
                issues.Add(new(slot.Key, "REFERENCE_INVALID", slot.Source));
            else codes[slot.Key] = Code(related);
        }
        return (result, codes, issues.ToArray());
    }

    private async Task<string?> ParentChain(OperationsAccessV1 access, string legal, Guid original, Guid first, CancellationToken ct)
    {
        var visited = new HashSet<Guid> { original };
        Guid? target = first;
        for (var step = 0; target is { } id; step++)
        {
            if (!visited.Add(id)) return "RELATION_CYCLE";
            if (step >= 32) return "REFERENCE_UNPROVEN";
            try
            {
                var parent = await VisibleAsync(access, id, false, ct);
                if (parent.Head?.Legal != legal || !parent.Row.CustomerFlg) return "REFERENCE_UNPROVEN";
            }
            catch (OrderPriceCorrectionException ex) when (ex.Status == 404) { return "REFERENCE_UNPROVEN"; }
            target = (await ReadSlotsAsync(id, ct)).SingleOrDefault(x => x.Slot == "PARENT_CUSTOMER")?.TargetId;
        }
        return null;
    }

    private static void ProjectRelations(BusinessPartner row, Dictionary<string, string> codes, IEnumerable<string> changed)
    {
        foreach (var slot in changed)
        {
            var code = codes.GetValueOrDefault(slot);
            switch (slot)
            {
                case "PARENT_CUSTOMER": row.ParentCustomerCd = code; break;
                case "CUSTOMER_AR": row.AccountsReceivableCd = code; break;
                case "AR_BILLING": row.BillingCd = code; row.BillingName = null; break;
                case "BILLING_RECEIPT": row.ReceiptCd = code; row.ReceiptName = null; break;
                case "CUSTOMER_CREDIT": row.CreditMgmtCd = code; break;
                case "BILLING_CREDIT_AR": row.CreditMgmtArCd = code; break;
                case "SUPPLIER_AP" or "AP_SCHEDULE":
                    row.PaymentScheduleCd = row.SupplierFlg && row.AccountsPayableFlg ? null : code; break;
                case "SCHEDULE_PAYEE": row.PaymentCd = code; break;
            }
        }
    }

    private async Task<Issue[]> FinanceIssues(OperationsAccessV1 access, Head? head, string legal,
        FinanceLink[] requests, Guid? financeContext, CancellationToken ct)
    {
        var values = requests.Where(x => x.Reference is not null).ToArray();
        if (values.Length == 0) return [];
        var issues = values.Select(x => new Issue("finance." + x.Kind, "REFERENCE_UNPROVEN")).ToArray();
        if (head is null || financeContext is null) return issues;
        if (await access.TryPartnerActionAsync(BusinessPartnerFinanceReferenceReader.ReadAction, ct) is null) return issues;
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        try
        {
            // 财务原件及字段权须从正式 Owner 读取，并持其门至本次事务提交。
            var version = await LegacyRevision(head.Id, ct);
            var requested = values.Select(x => new BusinessPartnerFinanceUseV1("finance." + x.Kind, x.Reference!)).ToArray();
            var enlisted = await financeReader.ValidateProfileReferencesAsync(access, transaction, head.Id, financeContext.Value,
                legal, Revision(version), requested, ct);
            return enlisted.Decision == "VALID" && enlisted.HeldUntilTransactionEnd ? [] : issues;
        }
        catch (OrderPriceCorrectionException ex) when (ex.Status is 404 or 409 or 412 or 422) { return issues; }
    }
}
