using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CP6.Entity.Contracts.Wire;

namespace CP6.Entity.Contracts.ErpCredit;

internal static class NativeCreditSemantics
{
    private static void Require(bool condition, string code) { if (!condition) throw new JsonException(code); }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static ExactDecimal Number(JsonElement value, string name) => ExactDecimal.Parse(Text(value, name));
    private static ExactDecimal Measure(JsonElement ranges)
    {
        var sum = ExactDecimal.Zero;
        foreach (var range in ranges.EnumerateArray())
        {
            var from = Number(range, "from"); var to = Number(range, "to");
            Require(to.CompareTo(from) > 0, "RANGE"); sum += to - from;
        }
        return sum;
    }
    // Framed JSON structural keys avoid collisions from embedded NUL in a legal WMS Id.
    private static string RefKey(JsonElement reference) => JsonSemanticEquality.Key(reference);

    public static void Validate(string root, JsonElement value, bool current)
    {
        switch (root)
        {
            case "Range": Require(Number(value, "from").CompareTo(Number(value, "to")) < 0, "RANGE"); break;
            case "Slice": Require(Number(value, "from").CompareTo(Number(value, "toExclusive")) < 0, "SLICE"); break;
            case "CreditOwnerRequest":
                Require(Measure(value.GetProperty("eligibleRanges")).Equals(Number(value.GetProperty("eligibleQuantity"), "value")), "RANGE_MEASURE"); break;
            case "InvoiceAllocation":
                Require(Measure(value.GetProperty("coveredRanges")).Equals(Number(value.GetProperty("quantity"), "value")), "RANGE_MEASURE");
                var net = value.GetProperty("net"); var tax = value.GetProperty("tax"); var gross = value.GetProperty("gross");
                Require((Number(net, "value") + Number(tax, "value")).Equals(Number(gross, "value")) && Text(net, "currency") == Text(tax, "currency") && Text(net, "currency") == Text(gross, "currency"), "MONEY_INVARIANT"); break;
            case "CommercialOutcomeComponent":
                if (value.TryGetProperty("coveredRanges", out var ranges)) Require(Measure(ranges).Equals(Number(value.GetProperty("quantity"), "value")), "RANGE_MEASURE"); break;
            case "CommercialOutcomeRef": ValidateCommercialOutcome(value); break;
            case "CreditAuthorityBody":
                Require((Number(value, "maxNet") + Number(value, "maxTax")).Equals(Number(value, "maxGross")), "MONEY_INVARIANT"); break;
            case "CommandResult":
                if (Text(value, "kind") == "COMPLETED")
                {
                    var outcome = Text(value, "outcome"); var hasReference = value.GetProperty("resultRef").ValueKind != JsonValueKind.Null;
                    Require(outcome != "APPLIED" || hasReference, "OWNER_RESULT_REQUIRED");
                    Require(!(current ? outcome == "REJECTED" : outcome != "APPLIED") || !hasReference, "OWNER_RESULT_FORBIDDEN");
                }
                break;
            case "SafeOperationDetail":
                Require(NativeSchemaValidator.ExactInteger(value.GetProperty("expectedMemberCount").GetRawText()) == value.GetProperty("members").GetArrayLength(), "MEMBER_COUNT"); break;
            case "FinArObservation": ValidateObservation(value); break;
        }
    }
    private static void ValidateCommercialOutcome(JsonElement value)
    {
        var components = value.GetProperty("components").EnumerateArray().ToArray();
        var singletonCount = components.Count(c => Text(c, "kind") is "RETURN_ONLY" or "REJECTED");
        Require(singletonCount == 0 || (singletonCount == 1 && components.Length == 1), "SINGLETON_BUNDLE");
        var credits = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var component in components.Where(c => Text(c, "kind") == "CREDIT"))
        {
            Require(credits.TryAdd(RefKey(component.GetProperty("componentRef")), component), "DUPLICATE_CREDIT_REF");
            var approved = ExactDecimal.Zero;
            foreach (var allocation in component.GetProperty("invoiceAllocations").EnumerateArray()) approved += Number(allocation.GetProperty("quantity"), "value");
            Require(approved.Equals(Number(component.GetProperty("approvedQuantity"), "value")), "APPROVED_QUANTITY");
        }
        foreach (var component in components.Where(c => Text(c, "kind") == "REFUND_AUTHORIZATION"))
        {
            Require(credits.TryGetValue(RefKey(component.GetProperty("creditComponentRef")), out var credit), "REFUND_CREDIT_LINK");
            var cap = ExactDecimal.Zero; var money = component.GetProperty("authorizedAmount");
            foreach (var allocation in credit.GetProperty("invoiceAllocations").EnumerateArray())
            {
                var gross = allocation.GetProperty("gross");
                Require(Text(gross, "currency") == Text(money, "currency") && Text(component, "currency") == Text(money, "currency"), "CURRENCY_MISMATCH");
                cap += Number(gross, "value");
            }
            Require(Number(money, "value").CompareTo(cap) <= 0, "REFUND_CAP");
        }
        var claimed = new List<(ExactDecimal From, ExactDecimal To)>();
        foreach (var component in components)
        {
            IEnumerable<JsonElement> groups = Text(component, "kind") == "CREDIT" ? component.GetProperty("invoiceAllocations").EnumerateArray().ToArray() :
                component.TryGetProperty("coveredRanges", out _) ? [component] : [];
            foreach (var group in groups) foreach (var range in group.GetProperty("coveredRanges").EnumerateArray())
            {
                var from = Number(range, "from"); var to = Number(range, "to");
                Require(!claimed.Any(prior => from.CompareTo(prior.To) < 0 && prior.From.CompareTo(to) < 0), "RANGE_OVERLAP");
                claimed.Add((from, to));
            }
        }
    }
    private static void ValidateObservation(JsonElement value)
    {
        var exact = value.GetProperty("exactCommandResult");
        switch (Text(value, "state"))
        {
            case "FOUND": Require(JsonSemanticEquality.AreEqual(exact.GetProperty("resultRef"), value.GetProperty("ownerResultRef")), "OWNER_RESULT_PROJECTION"); break;
            case "PENDING": Require(JsonSemanticEquality.AreEqual(exact.GetProperty("operationId"), value.GetProperty("operationId")) && Text(exact, "queryRef") == Text(value, "queryRef"), "PENDING_OWNER_RESULT_PROJECTION"); break;
            case "FAILED_NO_EFFECT":
                var raw = NativeSchemaValidator.DecodeBase64(Text(value, "exactCommandResultCanonicalBase64"));
                Require(CreditIdentity.Sha256(raw) == Text(value, "exactCommandResultDigest"), "EXACT_COMMAND_RESULT_DIGEST");
                using (var parsed = StrictJson.Parse(raw)) Require(JsonSemanticEquality.AreEqual(parsed.RootElement, exact), "EXACT_COMMAND_RESULT_PROJECTION");
                break;
        }
    }
    private readonly record struct ExactDecimal(BigInteger Coefficient, int Scale) : IComparable<ExactDecimal>
    {
        public static ExactDecimal Zero => new(BigInteger.Zero, 0);
        public static ExactDecimal Parse(string text)
        {
            var point = text.IndexOf('.');
            return new(BigInteger.Parse(text.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture), point < 0 ? 0 : text.Length - point - 1);
        }
        private static (BigInteger Left, BigInteger Right, int Scale) Align(ExactDecimal left, ExactDecimal right)
        {
            var scale = Math.Max(left.Scale, right.Scale);
            return (left.Coefficient * BigInteger.Pow(10, scale - left.Scale), right.Coefficient * BigInteger.Pow(10, scale - right.Scale), scale);
        }
        public static ExactDecimal operator +(ExactDecimal left, ExactDecimal right) { var (a, b, s) = Align(left, right); return new(a + b, s); }
        public static ExactDecimal operator -(ExactDecimal left, ExactDecimal right) { var (a, b, s) = Align(left, right); return new(a - b, s); }
        public int CompareTo(ExactDecimal other) { var (a, b, _) = Align(this, other); return a.CompareTo(b); }
        public bool Equals(ExactDecimal other) => CompareTo(other) == 0;
        public override int GetHashCode()
        {
            var coefficient = Coefficient; var scale = Scale;
            while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
            return HashCode.Combine(coefficient, scale);
        }
    }
}
