using System.Text.Json.Serialization;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

// Keys are source-authority-qualified identities in an explicit namespace, never display names.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementScopeAxisV1(string Mode,string? Namespace,string[] Keys);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementClauseTermsV1(string Schema,RequirementScopeAxisV1 Customers,
    RequirementScopeAxisV1 Products,RequirementScopeAxisV1 Configurations,DateTimeOffset? From,DateTimeOffset? Until,
    [property:JsonRequired] bool FromUnbounded,[property:JsonRequired] bool UntilUnbounded,
    string Necessity,string Waivability,string Basis);
public sealed record RequirementScopeIntersectionV1(string LeftClause,string RightClause,string State,
    string Customers,string Products,string Configurations,string Time);

internal static class RequirementApplicabilityV1
{
    internal static void Validate(RequirementClauseTermsV1 t)
    {
        if(t.Schema!="requirement-clause-terms/1" || t.Necessity is not("UNKNOWN" or "MANDATORY" or "OPTIONAL")
            || t.Waivability is not("UNKNOWN" or "PROHIBITED" or "SOURCE_DECISION_REQUIRED"))throw Error("REQUIREMENT_CLAUSE_TERMS_INVALID",400);
        EngineeringRequirementValuesV1.Text(t.Basis,true);
        foreach(var a in new[]{t.Customers,t.Products,t.Configurations})
        {
            if(a==null || a.Keys==null || a.Keys.Length>256 || a.Mode is not("UNKNOWN" or "ALL" or "VALUES"))throw Error("REQUIREMENT_SCOPE_AXIS_INVALID",400);
            if(a.Mode=="VALUES")
            {
                EngineeringRequirementValuesV1.Text(a.Namespace,true,256);
                if(a.Keys.Length==0 || a.Keys.Distinct(StringComparer.Ordinal).Count()!=a.Keys.Length)throw Error("REQUIREMENT_SCOPE_AXIS_INVALID",400);
                foreach(var key in a.Keys)EngineeringRequirementValuesV1.Text(key,true,256);
            }
            else if(a.Namespace!=null || a.Keys.Length!=0)throw Error("REQUIREMENT_SCOPE_AXIS_INVALID",400);
        }
        if(t.FromUnbounded && t.From!=null || t.UntilUnbounded && t.Until!=null
            || t.From?.Offset!=null && t.From.Value.Offset!=TimeSpan.Zero
            || t.Until?.Offset!=null && t.Until.Value.Offset!=TimeSpan.Zero
            || t.From!=null && t.Until!=null && t.From>=t.Until)throw Error("REQUIREMENT_SCOPE_TIME_INVALID",400);
    }
    private static string Axis(RequirementScopeAxisV1 a,RequirementScopeAxisV1 b)
    {
        if(a.Mode=="UNKNOWN" || b.Mode=="UNKNOWN")return "UNKNOWN";
        if(a.Mode=="ALL" || b.Mode=="ALL")return "OVERLAP";
        if(a.Namespace!=b.Namespace)return "UNKNOWN";
        return a.Keys.Intersect(b.Keys,StringComparer.Ordinal).Any()?"OVERLAP":"DISJOINT";
    }
    private static string Time(RequirementClauseTermsV1 a,RequirementClauseTermsV1 b)
    {
        // Intervals are UTC [from,until). A known separating bound proves disjointness even if another bound is unknown.
        if(a.Until!=null && b.From!=null && a.Until<=b.From || b.Until!=null && a.From!=null && b.Until<=a.From)return "DISJOINT";
        return (a.From!=null || a.FromUnbounded) && (a.Until!=null || a.UntilUnbounded)
            && (b.From!=null || b.FromUnbounded) && (b.Until!=null || b.UntilUnbounded)?"OVERLAP":"UNKNOWN";
    }
    internal static string Combine(IEnumerable<string> axes)=>axes.Contains("DISJOINT")?"DISJOINT":axes.Contains("UNKNOWN")?"UNKNOWN":"OVERLAP";
    internal static (string State,int PairCount,RequirementScopeIntersectionV1[] Examples) Intersections(RequirementSetMemberObservationV1 left,RequirementSetMemberObservationV1 right)
    {
        var result=new List<RequirementScopeIntersectionV1>();var count=0;var overlap=false;var unknown=false;
        var leftClauses=Clauses(left);var rightClauses=Clauses(right);
        foreach(var l in leftClauses)foreach(var r in rightClauses)
        {
            count++;
            var a=l.Terms;var b=r.Terms;
            if(a==null || b==null){unknown=true;if(result.Count<20)result.Add(new(l.Id,r.Id,"UNKNOWN","UNKNOWN","UNKNOWN","UNKNOWN","UNKNOWN"));continue;}
            var customers=Axis(a.Customers,b.Customers);var products=Axis(a.Products,b.Products);
            var configurations=Axis(a.Configurations,b.Configurations);var time=Time(a,b);
            var state=Combine([customers,products,configurations,time]);overlap|=state=="OVERLAP";unknown|=state=="UNKNOWN";
            if(result.Count<20)result.Add(new(l.Id,r.Id,state,customers,products,configurations,time));
        }
        return (overlap?"OVERLAP":unknown?"UNKNOWN":"DISJOINT",count,result.ToArray());
    }
    internal static RequirementClauseV1[] Clauses(RequirementSetMemberObservationV1 m)=>m.Requirement.Body.Requirement!.ClauseIds
        .Select(id=>m.Source.Body.Source!.Clauses.Single(c=>c.Id==id)).ToArray();
    internal static bool KnownConstraints(RequirementSetMemberObservationV1 m)=>Clauses(m).All(c=>c.Terms is {} t && t.Necessity!="UNKNOWN" && t.Waivability!="UNKNOWN");
}
