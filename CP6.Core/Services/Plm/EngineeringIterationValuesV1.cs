using System.Globalization;
using System.Numerics;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
internal static class EngineeringIterationValuesV1
{
    internal const string Owner="Main.Plm.EngineeringIteration.v1",Contract="Main.Plm.EngineeringIteration.commands.v1";
    internal static void Text(string? s,bool required=true,int max=8000)=>EngineeringRequirementValuesV1.Text(s,required,max);
    internal static string Next(string value)=>checked(Revision(value)+1).ToString(CultureInfo.InvariantCulture);
    internal static void Project(IterationProjectInputV1 p)
    {
        if(p==null || p.OwnerId==Guid.Empty || p.Members==null || p.Members.Length is <1 or >1024
            || p.Members.Contains(Guid.Empty) || p.Members.Distinct().Count()!=p.Members.Length || !p.Members.Contains(p.OwnerId)
            || p.Milestones==null || p.Milestones.Length>256)throw Error("ITERATION_PROJECT_INVALID",400);
        Text(p.Name,true,512);Text(p.Objective,false);
        if(p.StartDate!=null && p.EndDate!=null && p.EndDate<p.StartDate)throw Error("ITERATION_DATES_INVALID",400);
        if(p.Product!=null)Reference(p.Product);
        if(p.Site!=null) {if(p.Site.Owner!=SiteOwner || p.Site.Id==Guid.Empty || !ValidDigest(p.Site.Digest))throw Error("ITERATION_SITE_INVALID",400);Revision(p.Site.Version);}
        var ids=new HashSet<Guid>();foreach(var m in p.Milestones)
        {if(m==null || m.Id==Guid.Empty || !ids.Add(m.Id))throw Error("ITERATION_MILESTONE_INVALID",400);Text(m.Name,true,512);Text(m.Criterion);}
    }
    internal static void Plan(IterationPlanV1 p)
    {if(p==null || p.OwnerId==Guid.Empty || p.EndDate<p.StartDate)throw Error("ITERATION_PLAN_INVALID",400);Text(p.Name,true,512);Text(p.Goal);}
    internal static void Task(IterationTaskPlanV1 p)
    {
        if(p==null || p.Id==Guid.Empty || p.OwnerId==Guid.Empty || p.ConfirmerId==Guid.Empty || p.OwnerId==p.ConfirmerId)throw Error("ITERATION_TASK_INVALID",400);
        Text(p.Title,true,512);Text(p.Acceptance);if(EngineeringRoutingValuesV1.Number(p.Weight).Value<=0)throw Error("ITERATION_WEIGHT_INVALID",400);
    }
    internal static IterationProgressV1 Progress(IterationTaskV1[] tasks)
    {
        var values=tasks.Where(x=>x.State!="CANCELLED").Select(x=>(Task:x,Number:EngineeringRoutingValuesV1.Number(x.Plan.Weight))).ToArray();
        var scale=values.Length==0?0:values.Max(x=>x.Number.Scale);BigInteger total=0,done=0;
        foreach(var v in values){var weight=v.Number.Value*BigInteger.Pow(10,scale-v.Number.Scale);total+=weight;if(v.Task.State=="DONE")done+=weight;}
        string Decimal(BigInteger x){var s=x.ToString(CultureInfo.InvariantCulture).PadLeft(scale+1,'0');return scale==0?s:s.Insert(s.Length-scale,".");}
        var percent=total==0?null:((done*10000/total)/100).ToString(CultureInfo.InvariantCulture)+"."+((done*10000/total)%100).ToString("D2",CultureInfo.InvariantCulture);
        return new(Decimal(done),Decimal(total),percent);
    }
}
