using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests;

[Collection("TenantManagementPostgres")]
public sealed class AttachmentPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private CP6Context Context(Guid tenant) => server.Context(new TenantContext { CurrentTenantId = tenant });
    private static MemoryStream Bytes(string text = "same-owned-file") => new(Encoding.UTF8.GetBytes(text));

    private static LocalFileStore NewStore()
    {
        var owned = Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!;
        Assert.True(File.Exists(Path.Combine(owned, "ownership.txt")));
        var root = Path.Combine(owned, "attachment-bytes-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(root));
        // The test store is new and belongs to this isolated run. Retain all remaining bytes as evidence.
        return new LocalFileStore(root);
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_unlink_cannot_erase_another_tenants_shared_local_bytes()
    {
        await using var a = Context(Guid.NewGuid()); await using var b = Context(Guid.NewGuid());
        var store = NewStore(); var first = new AttachmentService(a, store); var second = new AttachmentService(b, store);
        var fileA = await first.UploadAsync(Bytes(), "a.txt", "text/plain", "order", "A", null, "alice");
        var fileB = await second.UploadAsync(Bytes(), "b.txt", "text/plain", "order", "B", null, "bob");
        Assert.Equal(fileA.StorePath, fileB.StorePath);
        await first.DeleteAsync(fileA.Id);
        Assert.Empty(await a.Pub_Attachments.AsNoTracking().ToListAsync());
        Assert.Single(await b.Pub_Attachments.AsNoTracking().ToListAsync());
        Assert.True(store.Exists(fileB.StorePath), "A's local reference count cannot authorize deletion of B's bytes.");
        var (_, stream) = await second.DownloadAsync(fileB.Id);
        await using (stream)
        using (var reader = new StreamReader(stream)) Assert.Equal("same-owned-file", await reader.ReadToEndAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_unlink_caller_rollback_restores_readable_reference_and_last_commit_retains_bytes()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        var store = NewStore(); var service = new AttachmentService(db, store);
        var file = await service.UploadAsync(Bytes(), "rollback.txt", null, "order", "A", null, "alice");
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await service.DeleteAsync(file.Id);
            Assert.Empty(await db.Pub_Attachments.AsNoTracking().ToListAsync());
            Assert.True(store.Exists(file.StorePath));
            await tx.RollbackAsync();
        }
        var (_, stream) = await service.DownloadAsync(file.Id);
        await using (stream)
        using (var reader = new StreamReader(stream)) Assert.Equal("same-owned-file", await reader.ReadToEndAsync());
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await service.DeleteAsync(file.Id); await tx.CommitAsync();
        }
        await service.DeleteAsync(file.Id); // Repeated unlink cannot turn into physical disposal.
        await using var observer = Context(tenant);
        Assert.Empty(await observer.Pub_Attachments.ToListAsync());
        Assert.True(store.Exists(file.StorePath));
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_unlink_uses_tenant_predicate_and_does_not_flush_other_pending_records()
    {
        var tenant = Guid.NewGuid(); var other = Guid.NewGuid();
        await using var db = Context(tenant); await using var theirs = Context(other);
        var store = NewStore(); var service = new AttachmentService(db, store);
        var own = await service.UploadAsync(Bytes(), "own.txt", null, "order", "A", null, "alice");
        var foreign = await new AttachmentService(theirs, store).UploadAsync(Bytes("foreign"), "foreign.txt", null, "order", "B", null, "bob");
        var foreignTracked = await db.Pub_Attachments.IgnoreQueryFilters().SingleAsync(a => a.Id == foreign.Id);
        db.Pub_Attachments.Add(new Pub_Attachment { FileName = "pending", BizType = "order" });
        foreignTracked.FileName = "UNSAVED-FOREIGN";
        await service.DeleteAsync(foreign.Id);
        own.FileName = "UNSAVED-OWN"; // Deleting this explicit target discards only this target's local representation.
        await service.DeleteAsync(own.Id);
        Assert.Equal(EntityState.Detached, db.Entry(own).State);
        await using var observer = Context(tenant); Assert.Empty(await observer.Pub_Attachments.ToListAsync());
        await using var otherObserver = Context(other);
        Assert.Equal("foreign.txt", (await otherObserver.Pub_Attachments.SingleAsync()).FileName);
        Assert.True(store.Exists(own.StorePath)); Assert.True(store.Exists(foreign.StorePath));
        Assert.Equal(EntityState.Modified, db.Entry(foreignTracked).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_unlink_database_failure_preserves_prior_caller_work_and_content()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        var store = NewStore(); var service = new AttachmentService(db, store);
        var file = await service.UploadAsync(Bytes(), "held-by-fk.txt", null, "order", "A", null, "alice");
        await db.Database.OpenConnectionAsync();
        // PostgreSQL does not allow a temporary FK to a permanent table. This uniquely named
        // fixture-owned table lives only in the newly created test database and is dropped below.
        var child = "w66_file_reference_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync($"CREATE TABLE public.{child} (attachment_id uuid REFERENCES public.\"Pub_Attachment\"(\"Id\"))");
        try
        {
            await db.Database.ExecuteSqlRawAsync($"INSERT INTO public.{child} VALUES ({{0}})", file.Id);
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Pub_DocSequences.Add(new() { BizKey = "W66_PRIOR", Prefix = "P" }); await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => service.DeleteAsync(file.Id));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            Assert.Single(await db.Pub_Attachments.AsNoTracking().ToListAsync());
            Assert.Single(await db.Pub_DocSequences.AsNoTracking().ToListAsync());
            await tx.CommitAsync();
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TABLE public.{child}"); }
        Assert.True(store.Exists(file.StorePath));
        await using var verify = Context(tenant);
        Assert.Single(await verify.Pub_Attachments.ToListAsync());
        Assert.Single(await verify.Pub_DocSequences.ToListAsync());
    }
}
