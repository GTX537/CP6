using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task DocumentWithdrawal_forward_adoption_preserves_existing_applied_and_pending_originals_without_GET_history()
    {
        await using var h=await Seed();var f=await DocumentApplicationSetup(h);FactAck(await f.Receiver.ReceiveAsync(f.Payload));var owner=DocumentReviews(h,f.Document.Files);
        var detail=await owner.SubmissionAsync(f.Document.Document,f.Document.Submission);
        var applied=await owner.ApplyAsync(f.Document.Document,detail.Application.Etag,new(Guid.NewGuid(),f.Document.Submission,null,"Pre-adoption actual application"));
        await using var pending=await Seed();var second=await DocumentDecisionSetup(pending);
        // A private database clone models the predecessor layout while preserving its real
        // DOC/submission/application originals. The shared fixture and existing environments are untouched.
        var database="w297_doc_upgrade_"+Guid.NewGuid().ToString("N");NpgsqlConnection.ClearAllPools();
        await using var admin=server.Context(new TenantContext());
        admin.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()){Database="postgres"}.ConnectionString);
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database} TEMPLATE {TenantManagementPostgresFixture.Database}");
        await using var upgraded=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        upgraded.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()){Database=database}.ConnectionString);
        Assert.StartsWith("w297_doc_upgrade_",new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()).Database);
        var original=(await Documents(h with{Db=upgraded},f.Document.Files).ReadAsync(f.Document.Document)).OriginalJson;
        var payload=await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_v1.submissions WHERE id={f.Document.Submission}").SingleAsync();
        await upgraded.Database.ExecuteSqlRawAsync("""
            DROP SCHEMA cp6_document_withdrawal_delivery_v1 CASCADE;
            DROP SCHEMA cp6_document_review_control_v1 CASCADE;
            DELETE FROM public."__EFMigrationsHistory" WHERE "MigrationId" IN('20261104010000_DocumentReviewWithdrawalV1','20261104011000_DocumentBusinessDelegationV1','20261104012000_DocumentWithdrawalDeliveryV1');
            """);
        Assert.Equal(3,(await upgraded.Database.GetPendingMigrationsAsync()).Count());await upgraded.Database.MigrateAsync();
        var actual=DocumentReviews(h with{Db=upgraded},f.Document.Files);var before=await actual.WithdrawalContextAsync(f.Document.Document,f.Document.Submission);
        Assert.Equal("APPLIED",before.State);Assert.Equal(applied,(await actual.ApplicationOperationAsync(applied.OperationId,false)).Result);
        Assert.Equal(original,(await Documents(h with{Db=upgraded},f.Document.Files).ReadAsync(f.Document.Document)).OriginalJson);
        Assert.Equal(payload,await upgraded.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_v1.submissions WHERE id={f.Document.Submission}").SingleAsync());
        Assert.Equal("IN_REVIEW",await upgraded.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_document_review_control_v1.heads WHERE tenant_id={pending.Tenant} AND submission_id={second.Submission}").SingleAsync());
        Assert.Equal("EXISTING_SUBMISSION_ADOPTION",await upgraded.Database.SqlQuery<string>($"SELECT origin AS \"Value\" FROM cp6_document_review_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={f.Document.Submission}").SingleAsync());
        Assert.Equal(before.ControlRowVersion,(await actual.WithdrawalContextAsync(f.Document.Document,f.Document.Submission)).ControlRowVersion);
        Assert.Equal(0,await upgraded.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_document_review_control_v1.withdrawals").SingleAsync());
        // Missing adopted control is unavailable, never silently reconstructed by a read.
        await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
            ALTER TABLE cp6_document_review_control_v1.heads DISABLE TRIGGER document_control_no_delete;
            DELETE FROM cp6_document_review_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={f.Document.Submission};
            ALTER TABLE cp6_document_review_control_v1.heads ENABLE TRIGGER document_control_no_delete;
            """);
        Assert.Equal("DOCUMENT_REVIEW_CONTROL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawalContextAsync(f.Document.Document,f.Document.Submission))).Message);
        Assert.False(await upgraded.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_document_review_control_v1.heads WHERE tenant_id={h.Tenant} AND submission_id={f.Document.Submission}) AS \"Value\"").SingleAsync());
    }
}
