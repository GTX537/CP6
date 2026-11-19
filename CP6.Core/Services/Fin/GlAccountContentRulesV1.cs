using System.Text.RegularExpressions;
using static CP6.Core.Services.Fin.GlAccountWorkflowStoreV1;

namespace CP6.Core.Services.Fin;

internal static partial class GlAccountContentRulesV1
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9_.-]{0,29}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    [GeneratedRegex("^[A-Z]{3}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCode();

    internal static string Code(string? value)
    {
        if (value is null || value.Any(c => c > 127)) throw Error("ACCOUNT_CODE_INVALID", 422);
        var code = value.Trim(' ').ToUpperInvariant();
        return Identifier().IsMatch(code) ? code : throw Error("ACCOUNT_CODE_INVALID", 422);
    }

    internal static GlAccountContentV1 Normalize(GlAccountContentV1? body, DateOnly day, GlBookV1 book,
        bool allowUnprovenDraft = false)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Name) || body.Name.Trim().Length > 100
            || body.Name.Any(char.IsControl) || body.Type is < 1 or > 5 || body.NormalSide is < 1 or > 2
            || body.ParentRootId == Guid.Empty || body.PostingClass is not ("GROUP" or "POSTABLE"))
            throw Error("ACCOUNT_CONTENT_INVALID", 422);
        if (body.From < day || body.ToExclusive <= body.From)
            throw Error("INVALID_EFFECTIVE_INTERVAL", 422);
        if (body.IsControl ? body.SubLedgerType is not ("AP" or "AR" or "INV" or "COST") : body.SubLedgerType is not null)
            throw Error("INVALID_SUBLEDGER_COMBINATION", 422);
        if (body.PostingClass == "GROUP" && (body.IsControl || body.RequirePartner || body.RoleKey is not null))
            throw Error("INVALID_GROUP_COMBINATION", 422);
        if (body.RoleKey is string role && (!Identifier().IsMatch(role) ||
            !allowUnprovenDraft && !book.Roles.Contains(role, StringComparer.Ordinal)))
            throw Error("ROLE_UNPROVEN", 422);
        if (body.Currency is not { } currency)
        {
            if (!allowUnprovenDraft) throw Error("CURRENCY_POLICY_UNPROVEN", 422);
            return body with { Name = body.Name.Trim() };
        }

        if (currency.Codes is null || currency.Codes.Any(code => code is null || !CurrencyCode().IsMatch(code) ||
            !book.Currencies.Contains(code, StringComparer.Ordinal)) ||
            currency.Codes.Distinct(StringComparer.Ordinal).Count() != currency.Codes.Length ||
            (currency.Mode switch
            {
                "BASE_ONLY" => currency.Codes.Length != 0,
                "SINGLE" => currency.Codes.Length != 1,
                "MULTI" => currency.Codes.Length < 2,
                _ => true
            }))
            throw Error("CURRENCY_POLICY_UNPROVEN", 422);
        return body with { Name = body.Name.Trim(), Currency = currency with { Codes = currency.Codes.Order(StringComparer.Ordinal).ToArray() } };
    }

    internal static void RequireProfessional(GlBookV1 book, string ledgerCurrency, DateOnly from, DateOnly? toExclusive)
    {
        if (!book.ProfessionalAvailable) throw Error("PROFESSIONAL_SERVICE_UNAVAILABLE", 503);
        if (book.PolicyRevision == Guid.Empty || string.IsNullOrWhiteSpace(book.BasisKey)
            || !CurrencyCode().IsMatch(ledgerCurrency) || !book.Currencies.Contains(ledgerCurrency, StringComparer.Ordinal))
            throw Error("PROFESSIONAL_POLICY_UNPROVEN", 422);
        if (from < book.PolicyFrom || (book.PolicyToExclusive.HasValue && (!toExclusive.HasValue || toExclusive > book.PolicyToExclusive)))
            throw Error("PROFESSIONAL_INTERVAL_UNPROVEN", 422);
    }
}
