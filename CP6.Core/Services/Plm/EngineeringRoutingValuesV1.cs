using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal static class EngineeringRoutingValuesV1
{
    internal const string Owner = "Main.Plm.EngineeringRouting.v1";
    private static readonly Regex Decimal = new(@"\A-?(0|[1-9][0-9]*)(\.[0-9]+)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    internal static void Ref(EngineeringSourceRefV1? value)
    {
        if (value == null || value.Owner != Owner || value.Id == Guid.Empty || !ValidDigest(value.Digest)) throw Error("ROUTING_REFERENCE_INVALID", 400);
        Revision(value.Version);
    }
    internal static void Text(string? value, int max = 4000, bool optional = false)
    { if (!(optional && value == "") && !ValidText(value, max, true)) throw Error("ROUTING_INPUT_INVALID", 400); }
    internal static (BigInteger Value, int Scale) Number(string value)
    {
        if (value == null || value.Length > 2050 || !Decimal.IsMatch(value)) throw Error("ROUTING_DECIMAL_INVALID", 400);
        var dot = value.IndexOf('.'); var scale = dot < 0 ? 0 : value.Length - dot - 1;
        if ((dot < 0 ? value.Length : dot) - (value[0] == '-' ? 1 : 0) > 1024 || scale > 1024) throw Error("ROUTING_DECIMAL_INVALID", 400);
        var number = BigInteger.Parse(value.Replace(".", "", StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (number.IsZero && value[0] == '-') throw Error("ROUTING_DECIMAL_INVALID", 400);
        return (number, scale);
    }
    private static int Compare(string x, string y)
    {
        var a = Number(x); var b = Number(y); var scale = Math.Max(a.Scale, b.Scale);
        return (a.Value * BigInteger.Pow(10, scale - a.Scale)).CompareTo(b.Value * BigInteger.Pow(10, scale - b.Scale));
    }
    internal static string[] Validate(EngineeringRoutingBodyV1? body)
    {
        if (body == null || body.Operations == null || body.Operations.Length > 1000 || body.RevisionOrigin == null
            || body.RevisionOrigin.Owner != EngineeringRevisionValuesV1.RevisionOwner || body.RevisionOrigin.Id == Guid.Empty
            || !ValidDigest(body.RevisionOrigin.Digest)) throw Error("ROUTING_INPUT_INVALID", 400);
        Text(body.Name, 256); Reference(body.ProductRef); Revision(body.RevisionOrigin.Version);
        Text(body.ManufacturingViewRequirement, optional: true);
        if (body.SiteRef is { } site)
        { if (site.Owner != SiteOwner || site.Id == Guid.Empty || !ValidDigest(site.Digest)) throw Error("ROUTING_SITE_INVALID", 400); Revision(site.Version); }
        var findings = new List<string>(); var ids = new HashSet<Guid>(); var orders = new HashSet<int>();
        if (body.Operations.Length == 0) findings.Add("OPERATIONS_REQUIRED");
        if (body.SiteRef == null) findings.Add("SITE_REQUIRED");
        findings.Add("MANUFACTURING_VIEW_OWNER_REQUIRED");
        foreach (var op in body.Operations)
        {
            if (op == null || op.OccurrenceId == Guid.Empty || !ids.Add(op.OccurrenceId) || op.SemanticOrder < 0 || !orders.Add(op.SemanticOrder)
                || op.DependsOn == null || op.DependsOn.Length > 1000 || op.DependsOn.Distinct().Count() != op.DependsOn.Length
                || op.Resources == null || op.Resources.Length > 100 || op.Tools == null || op.Tools.Length > 100
                || op.Parameters == null || op.Parameters.Length > 100 || op.LegacyRowId == Guid.Empty) throw Error("ROUTING_OCCURRENCE_INVALID", 400);
            Text(op.Name, 256); Text(op.ProcessCode, 100, true); Text(op.Instructions, 8000, true); Text(op.DocumentRequirement, 4000, true);
            if (op.AlternativeGroup != null) Text(op.AlternativeGroup, 256);
            EngineeringRevisionValuesV1.Applicability(op.Applicability, op.Reason);
            if (op.ProcessCode == "") findings.Add("PROCESS_REQUIRED:" + op.OccurrenceId);
            if (op.Applicability != "APPLIES" || op.AlternativeGroup != null) findings.Add("APPLICABILITY_OWNER_REQUIRED:" + op.OccurrenceId);
            if (op.Resources.Length == 0) findings.Add("RESOURCE_REQUIRED:" + op.OccurrenceId);
            findings.Add("CONTROLLED_DOCUMENT_OWNER_REQUIRED:" + op.OccurrenceId);
            var sources = new HashSet<(string, Guid)>();
            foreach (var source in op.Resources)
            {
                if (source == null || source.Owner != CP6.Core.Services.Mes.ResourceSourceStoreV1.Owner || source.Kind is not ("MACHINE" or "WORK_CENTER")
                    || source.Id == Guid.Empty || !ValidDigest(source.Digest) || !sources.Add((source.Kind, source.Id))) throw Error("ROUTING_RESOURCE_INVALID", 400);
                Revision(source.Version);
            }
            foreach (var tool in op.Tools)
            {
                if (tool == null || tool.Owner != ToolSpecificationStoreV1.Owner || tool.Id == Guid.Empty || !ValidDigest(tool.Digest)
                    || !sources.Add((tool.Owner, tool.Id))) throw Error("ROUTING_TOOL_REFERENCE_INVALID", 400);
                Revision(tool.Version);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in op.Parameters)
            {
                if (parameter == null || !names.Add(parameter.Name)) throw Error("ROUTING_PARAMETER_INVALID", 400);
                Text(parameter.Name, 256); Text(parameter.Method, 4000, true);
                foreach (var value in new[] { parameter.Value, parameter.Minimum, parameter.Maximum }) if (value != null) Number(value);
                if (parameter.UnitRef != null) CP6.Core.Services.Erp.UomProfileValuesV1.Reference(parameter.UnitRef);
                if (parameter.UnitRef == null) findings.Add("PARAMETER_UNIT_REQUIRED:" + op.OccurrenceId + ":" + parameter.Name);
                if (parameter.Method == "") findings.Add("PARAMETER_METHOD_REQUIRED:" + op.OccurrenceId + ":" + parameter.Name);
                if (parameter.Value == null && parameter.Minimum == null && parameter.Maximum == null) findings.Add("PARAMETER_VALUE_REQUIRED:" + op.OccurrenceId + ":" + parameter.Name);
                if (parameter.Minimum != null && parameter.Maximum != null && Compare(parameter.Minimum, parameter.Maximum) > 0
                    || parameter.Value != null && parameter.Minimum != null && Compare(parameter.Value, parameter.Minimum) < 0
                    || parameter.Value != null && parameter.Maximum != null && Compare(parameter.Value, parameter.Maximum) > 0)
                    findings.Add("PARAMETER_LIMIT_CONFLICT:" + op.OccurrenceId + ":" + parameter.Name);
            }
        }
        var index = body.Operations.ToDictionary(x => x.OccurrenceId); var pending = new Dictionary<Guid, int>();
        var next = ids.ToDictionary(x => x, _ => new List<Guid>());
        foreach (var op in body.Operations)
        {
            pending[op.OccurrenceId] = op.DependsOn.Length;
            foreach (var parent in op.DependsOn)
                if (!index.TryGetValue(parent, out var predecessor)) findings.Add("DEPENDENCY_MISSING:" + op.OccurrenceId);
                else { next[parent].Add(op.OccurrenceId); if (predecessor.SemanticOrder >= op.SemanticOrder) findings.Add("DEPENDENCY_ORDER_CONFLICT:" + op.OccurrenceId); }
        }
        var queue = new Queue<Guid>(pending.Where(x => x.Value == 0).Select(x => x.Key)); var visited = 0;
        while (queue.TryDequeue(out var id)) { visited++; foreach (var successor in next[id]) if (--pending[successor] == 0) queue.Enqueue(successor); }
        if (visited != ids.Count) findings.Add("DEPENDENCY_CYCLE_OR_UNRESOLVED");
        return findings.Distinct(StringComparer.Ordinal).ToArray();
    }
}
