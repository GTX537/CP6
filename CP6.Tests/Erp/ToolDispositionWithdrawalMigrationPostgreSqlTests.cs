using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task ToolWithdrawal_forward_adoption_preserves_actual_BLOCKED_and_pending_originals_without_GET_history()
    {
        await using var h=await Seed();var f=await FactSetup(h);var tool=await ApplicationTool(h);var submission=f.Decision.SubmissionId;
        FactAck(await f.Owner.ReceiveAsync(f.Payload));var blocked=await ApplicationAttempt(h,tool);Assert.Equal("BLOCKED",blocked.Receipt.Application);
        await using var pending=await Seed();var second=await IntakeSetup(pending);
        // Only a disposable local test fixture is cloned. No existing environment is changed.
        var database="w299_tool_upgrade_"+Guid.NewGuid().ToString("N");NpgsqlConnection.ClearAllPools();
        await using var admin=server.Context(new TenantContext());
        admin.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()){Database="postgres"}.ConnectionString);
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database} TEMPLATE {TenantManagementPostgresFixture.Database}");
        await using var upgraded=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        upgraded.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()){Database=database}.ConnectionString);
        Assert.StartsWith("w299_tool_upgrade_",new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()).Database);
        var original=ApprovalCanonicalV2.Bytes(await Disposition(h with{Db=upgraded}).ContextAsync(tool));
        var payload=await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE id={submission}").SingleAsync();
        await upgraded.Database.ExecuteSqlRawAsync("""
            DROP SCHEMA cp6_tool_withdrawal_delivery_v1 CASCADE;
            DROP SCHEMA cp6_tool_approval_control_v1 CASCADE;
            DELETE FROM public."__EFMigrationsHistory" WHERE "MigrationId" IN('20261104030000_ToolApprovalWithdrawalV1','20261104031000_ToolBusinessDelegationV1','20261104032000_ToolWithdrawalDeliveryV1');
            """);
        Assert.Equal(3,(await upgraded.Database.GetPendingMigrationsAsync()).Count());await upgraded.Database.MigrateAsync();
        var actual=ToolApproval(h with{Db=upgraded});var before=await actual.WithdrawalContextAsync(tool,submission);
        Assert.Equal("IN_REVIEW",before.State);
        Assert.Equal(ApprovalCanonicalV2.Bytes(blocked),ApprovalCanonicalV2.Bytes((await Applications(h with{Db=upgraded}).OperationAsync(blocked.OperationId,false)).Result!));
        Assert.Equal(original,ApprovalCanonicalV2.Bytes(await Disposition(h with{Db=upgraded}).ContextAsync(tool)));
        Assert.Equal(payload,await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE id={submission}").SingleAsync());
        var pendingSubmission=IntakeBody(second).SubmissionId;
        Assert.Equal("IN_REVIEW",await upgraded.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_tool_approval_control_v1.heads WHERE tenant_id={pending.Tenant} AND submission_id={pendingSubmission}").SingleAsync());
        Assert.Equal("EXISTING_SUBMISSION_ADOPTION",await upgraded.Database.SqlQuery<string>($"SELECT origin AS \"Value\" FROM cp6_tool_approval_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={submission}").SingleAsync());
        Assert.Equal(before.ControlRowVersion,(await actual.WithdrawalContextAsync(tool,submission)).ControlRowVersion);
        Assert.Equal(0,await upgraded.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals").SingleAsync());
        await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
            ALTER TABLE cp6_tool_approval_control_v1.heads DISABLE TRIGGER tool_control_no_delete;
            DELETE FROM cp6_tool_approval_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={submission};
            ALTER TABLE cp6_tool_approval_control_v1.heads ENABLE TRIGGER tool_control_no_delete;
            """);
        Assert.Equal("TOOL_APPROVAL_CONTROL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawalContextAsync(tool,submission))).Message);
        Assert.False(await upgraded.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_tool_approval_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={submission}) AS \"Value\"").SingleAsync());
    }
}
