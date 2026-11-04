using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Tests.Platform;
using CP6.Persistence.PostgreSql.Migrations.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_forward_migration_preserves_native_controls_existing_results_and_originals_without_GET_history()
    {
        await using var h=await Seed();var f=await RuleWithdrawalApplicationReady(h);var id=f.Source.Candidate.Reference.Id;
        var detail=await RuleReviews(h).SubmissionAsync(id,f.Submission);
        var applied=await RuleReviews(h).ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Actual pre-adoption approval"));
        await using var pending=await Seed();var second=await RuleDecisionSetup(pending);var pid=second.Source.Candidate.Reference.Id;
        var database="w300_rule_upgrade_"+Guid.NewGuid().ToString("N");NpgsqlConnection.ClearAllPools();
        await using var admin=server.Context(new TenantContext());
        admin.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()){Database="postgres"}.ConnectionString);
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database} TEMPLATE {TenantManagementPostgresFixture.Database}");
        await using var upgraded=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        upgraded.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()){Database=database}.ConnectionString);
        Assert.StartsWith("w300_rule_upgrade_",new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()).Database);
        async Task<string[]> Native()=>await upgraded.Database.SqlQueryRaw<string>("SELECT row_to_json(p)::text AS \"Value\" FROM cp6_location_rule_v1.proposals p ORDER BY tenant_id,id").ToArrayAsync();
        var native=await Native();var source=await ReferenceRules(h with{Db=upgraded}).ReadAsync(f.Source.Location,id);
        var payload=await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_v1.submissions WHERE id={f.Submission}").SingleAsync();
        // Model the true previous layout only inside this disposable clone, including its native transition function.
        await upgraded.Database.ExecuteSqlRawAsync("""
            DROP SCHEMA cp6_location_rule_withdrawal_delivery_v1 CASCADE;
            DROP SCHEMA cp6_location_rule_review_control_v1 CASCADE;
            DELETE FROM public."__EFMigrationsHistory" WHERE "MigrationId" IN('20261104040000_LocationRuleReviewWithdrawalV1','20261104041000_LocationRuleBusinessDelegationV1','20261104042000_LocationRuleWithdrawalDeliveryV1');
            """);
        var old=new LocationRuleReviewStateV1().UpOperations.OfType<SqlOperation>().Single().Sql;
        var start=old.IndexOf("CREATE OR REPLACE FUNCTION cp6_location_rule_v1.advance()",StringComparison.Ordinal);
        await upgraded.Database.ExecuteSqlRawAsync(old[start..(old.IndexOf("END $$;",start,StringComparison.Ordinal)+7)]);
        Assert.Equal(3,(await upgraded.Database.GetPendingMigrationsAsync()).Count());await upgraded.Database.MigrateAsync();
        Assert.Equal(native,await Native());
        var owner=RuleReviews(h with{Db=upgraded});var before=await owner.WithdrawalContextAsync(id,f.Submission);Assert.Equal("APPLIED",before.State);
        Assert.Equal(ApprovalCanonicalV2.Bytes(applied),ApprovalCanonicalV2.Bytes((await owner.ApplicationOperationAsync(applied.OperationId,false)).Result!));
        Assert.Equal(source.OriginalJson,(await ReferenceRules(h with{Db=upgraded}).ReadAsync(f.Source.Location,id)).OriginalJson);
        Assert.Equal(payload,await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_v1.submissions WHERE id={f.Submission}").SingleAsync());
        Assert.Equal(before.ControlRowVersion,(await owner.WithdrawalContextAsync(id,f.Submission)).ControlRowVersion);
        await using var pendingDb=server.Context(new TenantContext{CurrentTenantId=pending.Tenant});pendingDb.Database.SetConnectionString(upgraded.Database.GetConnectionString());
        var actual=RuleReviews(pending with{Db=pendingDb});var c=await actual.WithdrawalContextAsync(pid,second.Submission);Assert.Equal("IN_REVIEW",c.State);
        Assert.Equal(c.ControlRowVersion,await upgraded.Database.SqlQuery<string>($"SELECT encode(int8send(revision),'base64') AS \"Value\" FROM cp6_location_rule_v1.proposals WHERE tenant_id={pending.Tenant} AND id={pid}").SingleAsync());
        Assert.Equal(native,await Native());Assert.Equal(0,await upgraded.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals").SingleAsync());
        await upgraded.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_main_owner_v1.operations DISABLE TRIGGER location_rule_withdrawal_command_complete");
        Assert.Equal("LOCATION_RULE_REVIEW_CONTROL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawalContextAsync(pid,second.Submission))).Message);
        Assert.Equal(native,await Native());
    }
}
