using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RequirementSetWithdrawal_forward_adoption_preserves_existing_applied_and_pending_originals_without_GET_history()
    {
        await using var h=await Seed();var f=await SetApplicationSetup(h);var d=f.Set;var id=d.Source.Set.Reference.Id;
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var owner=SetReviews(h);var detail=await owner.SubmissionAsync(id,d.Submission);
        var applied=await owner.ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),d.Submission,null,"Actual predecessor application"));
        await using var pending=await Seed();var second=await SetDecisionSetup(pending);
        // Clone only this test-owned PG fixture; no existing environment is modified.
        var database="w298_set_upgrade_"+Guid.NewGuid().ToString("N");NpgsqlConnection.ClearAllPools();
        await using var admin=server.Context(new TenantContext());
        admin.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()){Database="postgres"}.ConnectionString);
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database} TEMPLATE {TenantManagementPostgresFixture.Database}");
        await using var upgraded=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        upgraded.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()){Database=database}.ConnectionString);
        Assert.StartsWith("w298_set_upgrade_",new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()).Database);
        var original=(await Sets(h with{Db=upgraded}).ReadAsync(id)).OriginalJson;
        var payload=await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_v1.submissions WHERE id={d.Submission}").SingleAsync();
        await upgraded.Database.ExecuteSqlRawAsync("""
            DROP SCHEMA cp6_requirement_set_withdrawal_delivery_v1 CASCADE;
            DROP SCHEMA cp6_requirement_set_review_control_v1 CASCADE;
            DELETE FROM public."__EFMigrationsHistory" WHERE "MigrationId" IN('20261104020000_RequirementSetReviewWithdrawalV1','20261104021000_RequirementSetBusinessDelegationV1','20261104022000_RequirementSetWithdrawalDeliveryV1');
            """);
        Assert.Equal(3,(await upgraded.Database.GetPendingMigrationsAsync()).Count());await upgraded.Database.MigrateAsync();
        var actual=SetReviews(h with{Db=upgraded});var before=await actual.WithdrawalContextAsync(id,d.Submission);
        Assert.Equal("APPLIED",before.State);Assert.Equal(applied,(await actual.ApplicationOperationAsync(applied.OperationId,false)).Result);
        Assert.Equal(original,(await Sets(h with{Db=upgraded}).ReadAsync(id)).OriginalJson);
        Assert.Equal(payload,await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_v1.submissions WHERE id={d.Submission}").SingleAsync());
        Assert.Equal("IN_REVIEW",await upgraded.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_requirement_set_review_control_v1.heads WHERE tenant_id={pending.Tenant} AND submission_id={second.Submission}").SingleAsync());
        Assert.Equal("EXISTING_SUBMISSION_ADOPTION",await upgraded.Database.SqlQuery<string>($"SELECT origin AS \"Value\" FROM cp6_requirement_set_review_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={d.Submission}").SingleAsync());
        Assert.Equal(before.ControlRowVersion,(await actual.WithdrawalContextAsync(id,d.Submission)).ControlRowVersion);
        Assert.Equal(0,await upgraded.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals").SingleAsync());
        await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
            ALTER TABLE cp6_requirement_set_review_control_v1.heads DISABLE TRIGGER requirement_set_control_no_delete;
            DELETE FROM cp6_requirement_set_review_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={d.Submission};
            ALTER TABLE cp6_requirement_set_review_control_v1.heads ENABLE TRIGGER requirement_set_control_no_delete;
            """);
        Assert.Equal("REQUIREMENT_SET_REVIEW_CONTROL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawalContextAsync(id,d.Submission))).Message);
        Assert.False(await upgraded.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_requirement_set_review_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={d.Submission}) AS \"Value\"").SingleAsync());
    }
}
