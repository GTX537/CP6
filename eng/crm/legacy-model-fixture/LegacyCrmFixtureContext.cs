// Historical CRM model fixture. Compiled only by CP6.Tests, never published.
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Crm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Crm;

internal sealed class LegacyCrmFixtureContext : CP6Context
{
    internal LegacyCrmFixtureContext(DbContextOptions<CP6Context> options, TenantContext tenant)
        : base(options, tenant) { }
    // ───── CRM + public marketing site foundation ─────
    public DbSet<CrmAccount> CrmAccounts { get; set; }
    public DbSet<CrmContact> CrmContacts { get; set; }
    public DbSet<CrmLead> CrmLeads { get; set; }
    public DbSet<CrmCollaborator> CrmCollaborators { get; set; }
    public DbSet<CrmActivity> CrmActivities { get; set; }
    public DbSet<CrmSourceTouch> CrmSourceTouches { get; set; }
    public DbSet<CrmPublicSubmission> CrmPublicSubmissions { get; set; }
    public DbSet<CrmOpportunity> CrmOpportunities { get; set; }
    public DbSet<CrmStageHistory> CrmStageHistories { get; set; }
    public DbSet<CrmErpLink> CrmErpLinks { get; set; }
    public DbSet<CrmMergeRecord> CrmMergeRecords { get; set; }
    public DbSet<CrmIntakeConfig> CrmIntakeConfigs { get; set; }
    public DbSet<CrmIntakeMember> CrmIntakeMembers { get; set; }
    public DbSet<CrmSite> CrmSites { get; set; }
    public DbSet<CrmSitePage> CrmSitePages { get; set; }
    public DbSet<CrmPageRevision> CrmPageRevisions { get; set; }
    public DbSet<CrmPageTranslation> CrmPageTranslations { get; set; }
    public DbSet<CrmMediaAsset> CrmMediaAssets { get; set; }
    public DbSet<CrmPublicForm> CrmPublicForms { get; set; }
    public DbSet<CrmPublicRoute> CrmPublicRoutes { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ───── CRM + public marketing site foundation ─────
        modelBuilder.Entity<CrmAccount>(e =>
        {
            e.HasIndex(x => x.NormalizedName);
            e.HasIndex(x => x.BusinessPartnerCd);
            e.HasIndex(x => new { x.OwnerUserId, x.IsDeleted });
            e.HasMany(x => x.Contacts).WithOne(x => x.Account).HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<CrmContact>(e =>
        {
            e.HasIndex(x => x.NormalizedEmail);
            e.HasIndex(x => x.NormalizedPhone);
            e.HasIndex(x => new { x.OwnerUserId, x.IsDeleted });
        });
        modelBuilder.Entity<CrmLead>(e =>
        {
            e.HasIndex(x => x.LeadNo).IsUnique();
            e.HasIndex(x => x.NormalizedCompanyName);
            e.HasIndex(x => x.NormalizedEmail);
            e.HasIndex(x => x.NormalizedPhone);
            e.HasIndex(x => new { x.Status, x.OwnerUserId, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.SlaDueAt, x.IsDeleted });
            e.HasIndex(x => x.ConvertedOpportunityId).IsUnique().HasFilter("[ConvertedOpportunityId] IS NOT NULL");
            e.HasOne<CrmAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmLead>().WithMany().HasForeignKey(x => x.MergedIntoLeadId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmCollaborator>(e =>
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.UserId }).IsUnique());
        modelBuilder.Entity<CrmActivity>(e =>
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt }));
        modelBuilder.Entity<CrmSourceTouch>(e =>
        {
            e.HasIndex(x => new { x.LeadId, x.TouchedAt });
            e.HasOne<CrmLead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmPublicSubmission>(e =>
        {
            e.HasIndex(x => new { x.FormId, x.IdempotencyHash }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreateDate });
            e.HasOne<CrmPublicForm>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmLead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmOpportunity>(e =>
        {
            e.HasIndex(x => x.OpportunityNo).IsUnique();
            e.HasIndex(x => x.LeadId).IsUnique();
            e.HasIndex(x => new { x.Stage, x.OwnerUserId, x.IsDeleted });
            e.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
            e.HasOne<CrmLead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmContact>().WithMany().HasForeignKey(x => x.PrimaryContactId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmStageHistory>(e =>
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.ChangedAt }));
        modelBuilder.Entity<CrmErpLink>(e =>
        {
            e.HasIndex(x => new { x.OpportunityId, x.ErpEntityType, x.ErpEntityKey }).IsUnique();
            e.HasIndex(x => new { x.ErpEntityType, x.ErpEntityKey });
            e.HasOne<CrmOpportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmMergeRecord>(e =>
        {
            e.HasIndex(x => x.SourceLeadId).IsUnique();
            e.HasIndex(x => x.TargetLeadId);
            e.HasOne<CrmLead>().WithMany().HasForeignKey(x => x.SourceLeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmLead>().WithMany().HasForeignKey(x => x.TargetLeadId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmIntakeConfig>(e => e.HasIndex(x => x.Enable));
        modelBuilder.Entity<CrmIntakeMember>(e =>
        {
            e.HasIndex(x => new { x.IntakeConfigId, x.UserId }).IsUnique();
            e.HasOne<CrmIntakeConfig>().WithMany().HasForeignKey(x => x.IntakeConfigId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmSite>(e => e.HasIndex(x => x.SiteKey).IsUnique());
        modelBuilder.Entity<CrmSitePage>(e =>
        {
            e.HasIndex(x => new { x.SiteId, x.PageKey }).IsUnique();
            e.HasOne<CrmSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmPageRevision>(e =>
        {
            e.HasIndex(x => new { x.PageId, x.Version }).IsUnique();
            e.HasOne<CrmSitePage>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmPageTranslation>(e =>
        {
            e.HasIndex(x => new { x.RevisionId, x.Locale }).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Locale, x.Slug }).IsUnique();
            e.Property(x => x.BodyJson).HasColumnType("nvarchar(max)");
            e.HasOne<CrmSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmPageRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmMediaAsset>(e =>
        {
            e.HasIndex(x => new { x.SiteId, x.FileHash });
            e.HasOne<CrmSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmPublicForm>(e =>
        {
            e.HasIndex(x => new { x.SiteId, x.Enable });
            e.HasOne<CrmSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CrmIntakeConfig>().WithMany().HasForeignKey(x => x.IntakeConfigId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CrmPublicRoute>(e =>
        {
            // Shared registry: globally unique public identifiers resolve the tenant before tenant-scoped queries.
            e.HasIndex(x => x.PublicKey).IsUnique().HasFilter("[PublicKey] IS NOT NULL");
            e.HasIndex(x => x.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.RouteType, x.TargetId });
        });
        // Core's shared tenant filters and unique-index transformation must run
        // after these historical indexes have been configured, as before retirement.
        base.OnModelCreating(modelBuilder);
    }
}
