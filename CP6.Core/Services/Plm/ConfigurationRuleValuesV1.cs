using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;
namespace CP6.Core.Services.Plm;
internal static class ConfigurationRuleValuesV1
{
    internal const string Owner = "Main.Plm.ConfigurationRule.v1", Scope = "ENGINEERING_OPTIONS_ONLY";
    internal static void Ref(EngineeringSourceRefV1? value)
    {
        if (value == null || value.Owner != Owner) throw Error("ENGINEERING_CONFIGURATION_RULE_OWNER_REQUIRED", 503);
        Reference(new(EngineeringProductValuesV1.Owner, value.Id, value.Version, value.Digest));
    }
    // Compare decimal values without narrowing their supported exact precision. Original bytes remain untouched.
    internal static string Key(EngineeringOptionV1 value) => value.Type switch
    {
        "DECIMAL" => value.Decimal!.Contains('.') ? value.Decimal.TrimEnd('0').TrimEnd('.') : value.Decimal!,
        "BOOLEAN" => value.Boolean!.Value ? "true" : "false", _ => value.Text!
    };
    internal static bool Same(EngineeringOptionV1 a, EngineeringOptionV1 b) => a.Name == b.Name && a.Type == b.Type && Key(a) == Key(b);
    internal static void Options(EngineeringOptionV1[] values) => Configuration(new(values, "UNKNOWN", ""));
    internal static void Validate(ConfigurationRuleBodyV1? body)
    {
        if (body == null || body.Scope != Scope || body.Definitions == null || body.Constraints == null
            || body.Definitions.Length is < 1 or > 256 || body.Constraints.Length > 512) throw Error("CONFIGURATION_RULE_INVALID", 400);
        Text(body.Name, 256); Reference(body.ProductRef);
        var definitions = new Dictionary<string, ConfigurationOptionDefinitionV1>(StringComparer.Ordinal);
        foreach (var d in body.Definitions)
        {
            if (d == null || !ValidText(d.Name, 256) || !definitions.TryAdd(d.Name, d) || d.Type is not ("STRING" or "DECIMAL" or "BOOLEAN")
                || d.Required == null || d.AllowAnyValue == null || d.AllowedValues == null || d.AllowedValues.Length > 256
                || (d.AllowAnyValue.Value ? d.AllowedValues.Length != 0 : d.AllowedValues.Length == 0)) throw Error("CONFIGURATION_DEFINITION_INVALID", 400);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in d.AllowedValues)
            {
                Options([v]);
                if (v.Name != d.Name || v.Type != d.Type || !unique.Add(Key(v))) throw Error("CONFIGURATION_DEFINITION_INVALID", 400);
            }
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in body.Constraints)
        {
            if (c == null || !ValidText(c.Id, 256) || !ids.Add(c.Id) || c.Kind is not ("MUTEX" or "REQUIRES")) throw Error("CONFIGURATION_CONSTRAINT_INVALID", 400);
            Text(c.Reason);
            foreach (var condition in new[] { c.When, c.Then })
            {
                Options([condition]);
                if (!definitions.TryGetValue(condition.Name, out var d) || d.Type != condition.Type
                    || d.AllowAnyValue != true && !d.AllowedValues.Any(v => Same(v, condition))) throw Error("CONFIGURATION_CONSTRAINT_INVALID", 400);
            }
            if (c.When.Name == c.Then.Name) throw Error("CONFIGURATION_CONSTRAINT_INVALID", 400);
        }
    }
    internal static ConfigurationOptionCheckV1 Evaluate(ConfigurationRuleSnapshotV1 rule, EngineeringOptionV1[] options)
    {
        Validate(rule.Body); Options(options);
        var values = options.ToDictionary(x => x.Name, StringComparer.Ordinal); var definitions = rule.Body.Definitions.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var findings = new List<ConfigurationOptionFindingV1>(); bool unknown = false, conflict = false, invalid = false;
        foreach (var d in definitions.Values)
            if (d.Required == true && !values.ContainsKey(d.Name)) { unknown = true; findings.Add(new("REQUIRED_OPTION_MISSING", d.Name)); }
        foreach (var v in values.Values)
            if (!definitions.TryGetValue(v.Name, out var d) || v.Type != d.Type || d.AllowAnyValue != true && !d.AllowedValues.Any(x => Same(x, v)))
            { invalid = true; findings.Add(new("OPTION_VALUE_NOT_ALLOWED", v.Name)); }
        foreach (var c in rule.Body.Constraints)
        {
            if (!values.TryGetValue(c.When.Name, out var trigger) || !Same(trigger, c.When)) continue;
            var exists = values.TryGetValue(c.Then.Name, out var consequence);
            if (c.Kind == "REQUIRES" && !exists) { unknown = true; findings.Add(new("DEPENDENCY_OPTION_MISSING", c.Then.Name, c.Id)); }
            else if (c.Kind == "REQUIRES" && !Same(consequence!, c.Then) || c.Kind == "MUTEX" && exists && Same(consequence!, c.Then))
            { conflict = true; findings.Add(new("OPTION_CONSTRAINT_CONFLICT", c.Then.Name, c.Id)); }
        }
        return new(Scope, rule.Reference, EngineeringProductStoreV1.Digest(EngineeringProductStoreV1.Encode(options)),
            conflict ? "CONFLICT" : unknown ? "UNKNOWN" : invalid ? "NO_MATCH" : "OPTIONS_VALIDATED", findings.ToArray(), DateTimeOffset.UtcNow);
    }
}
