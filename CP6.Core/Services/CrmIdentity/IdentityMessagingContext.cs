using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

/// <summary>Independent priority dispatcher budget; enqueue joins the business SQL transaction.</summary>
public sealed class IdentityMessagingContext(DbContextOptions<IdentityMessagingContext> options) : DbContext(options)
{
    public const string Schema = "crm_identity_priority";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.AddCp6TransactionalMessaging(Schema);
}
