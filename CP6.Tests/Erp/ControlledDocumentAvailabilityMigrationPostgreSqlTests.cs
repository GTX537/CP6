using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_forward_adoption_keeps_old_originals_file_hold_and_read_only_history()
    {
        await using var h=await Seed();var files=new ContentStore();var input=await DocumentInput(h,files);var owner=Documents(h,files);
        await owner.CommandAsync(input);var first=await owner.ReadAsync(input.DocumentId);await owner.CommandAsync(DocumentRevise(first));
        var content=ContentOwner(h,files,new ContentInspector());await content.CommandAsync(ContentCommand(await content.ReadAsync(first.Body.Contents[0].File.Id),"HOLD"));
        var database="w307_doc_upgrade_"+Guid.NewGuid().ToString("N");NpgsqlConnection.ClearAllPools();
        await using var admin=server.Context(new TenantContext());
        admin.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()){Database="postgres"}.ConnectionString);
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database} TEMPLATE {TenantManagementPostgresFixture.Database}");
        await using var upgraded=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        upgraded.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()){Database=database}.ConnectionString);
        Assert.StartsWith("w307_doc_upgrade_",new NpgsqlConnectionStringBuilder(upgraded.Database.GetConnectionString()).Database);
        // Only this new disposable clone models the predecessor layout; existing environments and fixture are untouched.
        await upgraded.Database.ExecuteSqlRawAsync("""
            DROP TABLE cp6_controlled_document_v1.availability CASCADE;
            DROP FUNCTION cp6_controlled_document_v1.register_availability() CASCADE;
            DROP FUNCTION cp6_controlled_document_v1.validate_availability() CASCADE;
            DROP FUNCTION cp6_controlled_document_v1.availability_receipt() CASCADE;
            DROP FUNCTION cp6_controlled_document_v1.availability_complete() CASCADE;
            DROP FUNCTION cp6_controlled_document_v1.availability_ready();
            DELETE FROM public."__EFMigrationsHistory" WHERE "MigrationId"='20261104084000_DocumentAvailabilityV1';
            """);
        Assert.Single(await upgraded.Database.GetPendingMigrationsAsync());await upgraded.Database.MigrateAsync();
        var actual=Documents(h with{Db=upgraded},files);var historical=await actual.ReadAsync(input.DocumentId,"1");
        Assert.Equal(first.OriginalJson,historical.OriginalJson);Assert.Equal(first.Reference,historical.Reference);
        Assert.Equal("AVAILABLE",historical.Availability!.State);Assert.Equal("REGISTER",historical.Availability.Action);
        Assert.Contains(historical.ContentStatus!,x=>x.Availability=="HELD");Assert.Equal("DRAFT",historical.State);
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.DownloadAsync(input.DocumentId,"1",first.Body.Contents[0].OccurrenceId))).Message);
        Assert.Single(await actual.AvailabilityHistoryAsync(input.DocumentId,"1"));Assert.Single(await actual.AvailabilityHistoryAsync(input.DocumentId,"2"));
        // A lost control row cannot be silently recreated by GET.
        await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
            ALTER TABLE cp6_controlled_document_v1.availability DISABLE TRIGGER document_availability_immutable;
            DELETE FROM cp6_controlled_document_v1.availability WHERE tenant_id={h.Tenant} AND document_id={input.DocumentId} AND revision=1;
            ALTER TABLE cp6_controlled_document_v1.availability ENABLE TRIGGER document_availability_immutable;
            """);
        Assert.Equal("DOCUMENT_AVAILABILITY_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.ReadAsync(input.DocumentId,"1"))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_receipt_without_effect_cannot_commit_and_history_cursor_preserves_prior_facts()
    {
        await using var h=await Seed();var files=new ContentStore();var input=await DocumentInput(h,files);await EngineeringAction(h,"plm-controlled-document","availability");
        var owner=Documents(h,files);await owner.CommandAsync(input);var first=await owner.ReadAsync(input.DocumentId);var command=AvailabilityCommand(first,"HOLD");
        var orphan=new ControlledDocumentResultV1(command.OperationId,"CONTROLLED_DOCUMENT",input.DocumentId,"1",first.Reference.Digest,
            new("2",Guid.NewGuid(),"HELD","HOLD",command.Reason,h.User,DateTimeOffset.UtcNow,command.OperationId));
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await new MainOwnerOperationJournalV1(h.Db).Store("Main.Plm.ControlledDocument.v1","Main.Plm.ControlledDocument.commands.v1",command.OperationId,
                h.User,MainOwnerOperationJournalV1.Encode(command),orphan,[input.DocumentId],new{reason="Fault injection: receipt without actual state"},default);
            var error=await Assert.ThrowsAsync<PostgresException>(()=>tx.CommitAsync());Assert.Contains("DOCUMENT_AVAILABILITY_EFFECT_MISSING",error.MessageText);
        }
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);await owner.AvailabilityAsync(command);
        for(var i=0;i<21;i++)
        {
            var state=await owner.ReadAsync(input.DocumentId);await owner.AvailabilityAsync(AvailabilityCommand(state,i%2==0?"RELEASE":"HOLD"));
        }
        var latest=await owner.AvailabilityHistoryAsync(input.DocumentId,"1");Assert.Equal(20,latest.Length);
        var older=await owner.AvailabilityHistoryAsync(input.DocumentId,"1",before:latest[^1].StateVersion);
        Assert.Equal(new[]{"3","2","1"},older.Select(x=>x.StateVersion));Assert.Equal(23,latest.Concat(older).Select(x=>x.StateVersion).Distinct().Count());
        Assert.Equal("HOLD",older[1].Action);Assert.Equal(command.OperationId,older[1].OperationId);
        Assert.Equal(first.OriginalJson,(await owner.ReadAsync(input.DocumentId)).OriginalJson);
    }
}
