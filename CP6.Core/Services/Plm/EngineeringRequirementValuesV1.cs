using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
internal static class EngineeringRequirementValuesV1
{
    internal const string Owner = "Main.Plm.EngineeringRequirement.v1", Contract = "Main.Plm.EngineeringRequirement.commands.v1";
    internal static void Text(string? s, bool required = false, int max = 8000)
    { if (!required && s == "") return; if (!ValidText(s, max, true)) throw Error("REQUIREMENT_TEXT_INVALID", 400); }
    internal static void Ref(EngineeringSourceRefV1? r)
    { if (r == null || r.Owner != Owner || r.Id == Guid.Empty || !ValidDigest(r.Digest)) throw Error("REQUIREMENT_REFERENCE_INVALID",400); Revision(r.Version); }
    private static readonly Regex Decimal = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static (BigInteger Number, int Scale) Exact(string s)
    {
        if (s.Length is < 1 or > 2050 || !Decimal.IsMatch(s)) throw Error("REQUIREMENT_NUMBER_INVALID",400);
        var dot=s.IndexOf('.'); var scale=dot<0?0:s.Length-dot-1;
        if (scale>1024 || (dot<0?s.Length:dot)-(s.StartsWith('-')?1:0)>1024) throw Error("REQUIREMENT_NUMBER_INVALID",400);
        return (BigInteger.Parse(s.Replace(".",""),CultureInfo.InvariantCulture),scale);
    }
    private static int Compare(string x,string y)
    { var a=Exact(x); var b=Exact(y); return (a.Number*BigInteger.Pow(10,b.Scale)).CompareTo(b.Number*BigInteger.Pow(10,a.Scale)); }
    internal static void Body(EngineeringRequirementBodyV1? b)
    {
        if (b == null) throw Error("REQUIREMENT_BODY_INVALID",400); Text(b.Name,true,512);
        if (b.Kind=="SOURCE")
        {
            var s=b.Source;
            if (s==null || b.Requirement!=null || s.Kind is not ("INTERNAL_PROPOSAL" or "UNRESOLVED") || s.Clauses==null || s.Clauses.Length is <1 or >256)
                throw Error("REQUIREMENT_SOURCE_INVALID",400);
            Text(s.OriginDescription,true); Text(s.SourceLabel); Text(s.AcquisitionBasis);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var c in s.Clauses)
            { if(c==null || !ids.Add(c.Id)) throw Error("REQUIREMENT_CLAUSE_INVALID",400); Text(c.Id,true,128);Text(c.OriginalText,true);Text(c.Locator); }
        }
        else if (b.Kind=="REQUIREMENT")
        {
            var r=b.Requirement;
            if(r==null || b.Source!=null || r.ClauseIds==null || r.ClauseIds.Length is <1 or >256 || r.ClauseIds.Distinct(StringComparer.Ordinal).Count()!=r.ClauseIds.Length
                || r.ClarificationQuestions==null || r.ClarificationQuestions.Length>256 || r.Criteria==null || r.Criteria.Length>256 || r.Relations==null || r.Relations.Length>256)
                throw Error("REQUIREMENT_DEFINITION_INVALID",400);
            Ref(r.Source); foreach(var id in r.ClauseIds) Text(id,true,128);
            Text(r.Interpretation,true);Text(r.ApplicableObject);Text(r.Necessity);
            foreach(var q in r.ClarificationQuestions)Text(q,true);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var c in r.Criteria)
            {
                if(c==null || !ids.Add(c.Id) || c.Kind is not ("QUANTITATIVE" or "QUALITATIVE"))throw Error("REQUIREMENT_CRITERION_INVALID",400);
                Text(c.Id,true,128);Text(c.Object);Text(c.Conditions);Text(c.MethodRequirement);
                foreach(var v in new[]{c.ProposedUnit,c.UnitBasis,c.Precision,c.QualitativeBasis}) if(v!=null)Text(v,true);
                foreach(var v in new[]{c.Lower,c.Upper})if(v!=null)_=Exact(v);
                if(c.Lower==null && c.LowerInclusive!=null || c.Upper==null && c.UpperInclusive!=null
                    || c.Kind=="QUALITATIVE" && (c.Lower!=null || c.Upper!=null || c.ProposedUnit!=null || c.UnitBasis!=null || c.Precision!=null)
                    || c.Kind=="QUANTITATIVE" && c.QualitativeBasis!=null)throw Error("REQUIREMENT_CRITERION_INVALID",400);
                if(c.Lower!=null && c.Upper!=null && (Compare(c.Lower,c.Upper)>0 || Compare(c.Lower,c.Upper)==0 && (c.LowerInclusive==false || c.UpperInclusive==false)))
                    throw Error("REQUIREMENT_INTERVAL_EMPTY",400);
            }
            ids.Clear();
            foreach(var link in r.Relations)
            {
                if(link==null || !ids.Add(link.Id) || link.Kind is not ("PARENT" or "DERIVED" or "CONSTRAINT" or "CONFLICT" or "DEPENDENCY"))throw Error("REQUIREMENT_RELATION_INVALID",400);
                Text(link.Id,true,128);Ref(link.Target);Text(link.CoverageReason);Text(link.Uncovered);Text(link.Necessity);
            }
        }
        else throw Error("REQUIREMENT_KIND_INVALID",400);
    }
    internal static List<string> Missing(EngineeringRequirementBodyV1 b)
    {
        var missing=new List<string>();
        if(b.Source is {} s)
        { if(s.Kind=="UNRESOLVED")missing.Add("SOURCE_UNRESOLVED");if(string.IsNullOrWhiteSpace(s.SourceLabel))missing.Add("SOURCE_LABEL_REQUIRED");if(string.IsNullOrWhiteSpace(s.AcquisitionBasis))missing.Add("ACQUISITION_BASIS_REQUIRED"); }
        if(b.Requirement is {} r)
        {
            missing.Add("REQUIREMENT_APPROVAL_PENDING");
            if(string.IsNullOrWhiteSpace(r.ApplicableObject))missing.Add("APPLICABLE_OBJECT_REQUIRED");
            if(string.IsNullOrWhiteSpace(r.Necessity))missing.Add("NECESSITY_REQUIRED");
            if(r.ClarificationQuestions.Length>0)missing.Add("SOURCE_CLARIFICATION_PENDING");
            if(r.Criteria.Length==0)missing.Add("CRITERIA_REQUIRED");
            foreach(var c in r.Criteria)
            {
                void Need(bool condition,string code){if(condition)missing.Add(code+":"+c.Id);}
                Need(string.IsNullOrWhiteSpace(c.Object),"CRITERION_OBJECT_REQUIRED");Need(string.IsNullOrWhiteSpace(c.Conditions),"CONDITIONS_REQUIRED");Need(string.IsNullOrWhiteSpace(c.MethodRequirement),"METHOD_REQUIREMENT_REQUIRED");
                if(c.Kind=="QUANTITATIVE")
                { Need(c.Lower==null && c.Upper==null,"BOUND_REQUIRED");Need(c.Lower!=null && c.LowerInclusive==null || c.Upper!=null && c.UpperInclusive==null,"ENDPOINT_REQUIRED");Need(c.ProposedUnit==null || c.UnitBasis==null,"UNIT_BASIS_REQUIRED");Need(c.Precision==null,"PRECISION_REQUIRED"); }
                else Need(c.QualitativeBasis==null,"QUALITATIVE_BASIS_REQUIRED");
                missing.Add("CRITERION_REVIEW_PENDING:"+c.Id);
            }
            foreach(var l in r.Relations)
            { if(string.IsNullOrWhiteSpace(l.CoverageReason) || string.IsNullOrWhiteSpace(l.Necessity))missing.Add("COVERAGE_BASIS_REQUIRED:"+l.Id);if(!string.IsNullOrWhiteSpace(l.Uncovered))missing.Add("UNCOVERED:"+l.Id);missing.Add("RELATION_REVIEW_PENDING:"+l.Id); }
        }
        return missing;
    }
}
