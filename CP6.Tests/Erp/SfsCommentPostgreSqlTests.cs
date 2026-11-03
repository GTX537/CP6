using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task SfsComment_unicode_limit_preserves_runtime_projections_and_trailing_spaces_without_partial_vote()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var c=await f.Owner.TaskContextAsync(f.Task);
        var text=string.Concat(Enumerable.Repeat("🙂",999))+" ";
        var input=SfsInput(c) with{Comment=text+" "};
        await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(f.Task,SfsCommand(input)));
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==f.Task)).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
        var raw=SfsCommand(input with{Comment=text});var result=await f.Owner.DecideSfsTaskAsync(f.Task,raw);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(result),System.Text.Json.JsonSerializer.Serialize(await f.Owner.DecideSfsTaskAsync(f.Task,raw)));
        foreach(var table in new[]{"Wf_FlowTask","Wf_FlowFormTo","Wf_FlowHistory"})
        {
            var suffix=table=="Wf_FlowHistory"?" AND \"Action\"='approve'":"";
            var stored=await h.Db.Database.SqlQueryRaw<string>($"SELECT convert_from(pg_catalog.bpcharsend(\"Comment\"),'UTF8') AS \"Value\" FROM public.\"{table}\" WHERE \"InstanceId\"=@p0{suffix}",f.Instance).SingleAsync();
            Assert.Equal(text,stored);
            // Completed native history is immutable. Exercise its actual copied
            // PostgreSQL constraints without trying to amend that original fact.
            await using var tx=await h.Db.Database.BeginTransactionAsync();
            await h.Db.Database.ExecuteSqlRawAsync($"CREATE TEMP TABLE comment_capacity (LIKE public.\"{table}\" INCLUDING CONSTRAINTS)");
            await h.Db.Database.ExecuteSqlRawAsync($"INSERT INTO comment_capacity SELECT * FROM public.\"{table}\" WHERE \"InstanceId\"=@p0",f.Instance);
            var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("UPDATE comment_capacity SET \"Comment\"=@p0",text+" "));
            Assert.Equal("23514",error.SqlState);Assert.Equal("CK_"+table+"_Comment_UnicodeScalarCapacity",error.ConstraintName);
            await tx.RollbackAsync();
        }
    }
}
