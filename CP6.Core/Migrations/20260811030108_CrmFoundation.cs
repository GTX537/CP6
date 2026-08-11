using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class CrmFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Crm_Account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Industry = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BusinessPartnerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_Account", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crm_Activity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityType = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCustomerFacing = table.Column<bool>(type: "bit", nullable: false),
                    NextActionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_Activity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crm_Collaborator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollaborationRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_Collaborator", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crm_IntakeConfig",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultDeptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstResponseSlaMinutes = table.Column<int>(type: "int", nullable: false),
                    WarningBeforeMinutes = table.Column<int>(type: "int", nullable: false),
                    EmailNotificationEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_IntakeConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crm_PublicRoute",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RouteType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PublicKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_PublicRoute", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crm_Site",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefaultLocale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EnabledLocales = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DefaultFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_Site", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crm_StageHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ToStage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_StageHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crm_Contact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PrivacyConsent = table.Column<bool>(type: "bit", nullable: false),
                    PrivacyConsentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrivacyPolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_Contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_Contact_Crm_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Crm_Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Crm_IntakeMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntakeConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_IntakeMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_IntakeMember_Crm_IntakeConfig_IntakeConfigId",
                        column: x => x.IntakeConfigId,
                        principalTable: "Crm_IntakeConfig",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_MediaAsset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StorePath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_MediaAsset", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_MediaAsset_Crm_Site_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Crm_Site",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_PublicForm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IntakeConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrivacyPolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    TokenRotatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_PublicForm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_PublicForm_Crm_IntakeConfig_IntakeConfigId",
                        column: x => x.IntakeConfigId,
                        principalTable: "Crm_IntakeConfig",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_PublicForm_Crm_Site_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Crm_Site",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_SitePage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageType = table.Column<int>(type: "int", nullable: false),
                    PageKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PublishedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_SitePage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_SitePage_Crm_Site_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Crm_Site",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_Lead",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProductInterest = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedCompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceChannel = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SlaDueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FirstResponseAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QualifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisqualificationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrivacyConsent = table.Column<bool>(type: "bit", nullable: false),
                    PrivacyConsentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrivacyPolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsQuarantined = table.Column<bool>(type: "bit", nullable: false),
                    RiskReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConvertedOpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MergedIntoLeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FirstLandingPage = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastLandingPage = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirstUtmCampaign = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastUtmCampaign = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_Lead", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_Lead_Crm_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Crm_Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_Lead_Crm_Contact_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Crm_Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_Lead_Crm_Lead_MergedIntoLeadId",
                        column: x => x.MergedIntoLeadId,
                        principalTable: "Crm_Lead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_PageRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_PageRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_PageRevision_Crm_SitePage_PageId",
                        column: x => x.PageId,
                        principalTable: "Crm_SitePage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_MergeRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetLeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MergedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MergedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_MergeRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_MergeRecord_Crm_Lead_SourceLeadId",
                        column: x => x.SourceLeadId,
                        principalTable: "Crm_Lead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_MergeRecord_Crm_Lead_TargetLeadId",
                        column: x => x.TargetLeadId,
                        principalTable: "Crm_Lead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_Opportunity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrimaryContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExpectedCloseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LostReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WonAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedQuotationNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WinningOrderNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FirstSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_Opportunity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_Opportunity_Crm_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Crm_Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_Opportunity_Crm_Contact_PrimaryContactId",
                        column: x => x.PrimaryContactId,
                        principalTable: "Crm_Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_Opportunity_Crm_Lead_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Crm_Lead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_PublicSubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RiskReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_PublicSubmission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_PublicSubmission_Crm_Lead_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Crm_Lead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_PublicSubmission_Crm_PublicForm_FormId",
                        column: x => x.FormId,
                        principalTable: "Crm_PublicForm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_SourceTouch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Medium = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Campaign = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Term = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LandingPage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Referrer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TouchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_SourceTouch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_SourceTouch_Crm_Lead_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Crm_Lead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_PageTranslation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BodyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SeoTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SeoDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_PageTranslation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_PageTranslation_Crm_PageRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Crm_PageRevision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crm_PageTranslation_Crm_Site_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Crm_Site",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Crm_ErpLink",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpEntityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ErpEntityKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crm_ErpLink", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crm_ErpLink_Crm_Opportunity_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Crm_Opportunity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Account_BusinessPartnerCd",
                table: "Crm_Account",
                column: "BusinessPartnerCd");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Account_NormalizedName",
                table: "Crm_Account",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Account_OwnerUserId_IsDeleted",
                table: "Crm_Account",
                columns: new[] { "OwnerUserId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Activity_EntityType_EntityId_OccurredAt",
                table: "Crm_Activity",
                columns: new[] { "EntityType", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Collaborator_EntityType_EntityId_UserId",
                table: "Crm_Collaborator",
                columns: new[] { "TenantId", "EntityType", "EntityId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Contact_AccountId",
                table: "Crm_Contact",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Contact_NormalizedEmail",
                table: "Crm_Contact",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Contact_NormalizedPhone",
                table: "Crm_Contact",
                column: "NormalizedPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Contact_OwnerUserId_IsDeleted",
                table: "Crm_Contact",
                columns: new[] { "OwnerUserId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_ErpLink_ErpEntityType_ErpEntityKey",
                table: "Crm_ErpLink",
                columns: new[] { "ErpEntityType", "ErpEntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_ErpLink_OpportunityId",
                table: "Crm_ErpLink",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_ErpLink_OpportunityId_ErpEntityType_ErpEntityKey",
                table: "Crm_ErpLink",
                columns: new[] { "TenantId", "OpportunityId", "ErpEntityType", "ErpEntityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_IntakeConfig_Enable",
                table: "Crm_IntakeConfig",
                column: "Enable");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_IntakeMember_IntakeConfigId",
                table: "Crm_IntakeMember",
                column: "IntakeConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_IntakeMember_IntakeConfigId_UserId",
                table: "Crm_IntakeMember",
                columns: new[] { "TenantId", "IntakeConfigId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_AccountId",
                table: "Crm_Lead",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_ContactId",
                table: "Crm_Lead",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_ConvertedOpportunityId",
                table: "Crm_Lead",
                columns: new[] { "TenantId", "ConvertedOpportunityId" },
                unique: true,
                filter: "[ConvertedOpportunityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_LeadNo",
                table: "Crm_Lead",
                columns: new[] { "TenantId", "LeadNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_MergedIntoLeadId",
                table: "Crm_Lead",
                column: "MergedIntoLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_NormalizedCompanyName",
                table: "Crm_Lead",
                column: "NormalizedCompanyName");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_NormalizedEmail",
                table: "Crm_Lead",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_NormalizedPhone",
                table: "Crm_Lead",
                column: "NormalizedPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_Status_OwnerUserId_IsDeleted",
                table: "Crm_Lead",
                columns: new[] { "Status", "OwnerUserId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Lead_Status_SlaDueAt_IsDeleted",
                table: "Crm_Lead",
                columns: new[] { "Status", "SlaDueAt", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_MediaAsset_SiteId_FileHash",
                table: "Crm_MediaAsset",
                columns: new[] { "SiteId", "FileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_MergeRecord_SourceLeadId",
                table: "Crm_MergeRecord",
                columns: new[] { "TenantId", "SourceLeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_MergeRecord_SourceLeadId1",
                table: "Crm_MergeRecord",
                column: "SourceLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_MergeRecord_TargetLeadId",
                table: "Crm_MergeRecord",
                column: "TargetLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Opportunity_AccountId",
                table: "Crm_Opportunity",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Opportunity_LeadId",
                table: "Crm_Opportunity",
                columns: new[] { "TenantId", "LeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Opportunity_LeadId1",
                table: "Crm_Opportunity",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Opportunity_OpportunityNo",
                table: "Crm_Opportunity",
                columns: new[] { "TenantId", "OpportunityNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Opportunity_PrimaryContactId",
                table: "Crm_Opportunity",
                column: "PrimaryContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Opportunity_Stage_OwnerUserId_IsDeleted",
                table: "Crm_Opportunity",
                columns: new[] { "Stage", "OwnerUserId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PageRevision_PageId",
                table: "Crm_PageRevision",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PageRevision_PageId_Version",
                table: "Crm_PageRevision",
                columns: new[] { "TenantId", "PageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PageTranslation_RevisionId",
                table: "Crm_PageTranslation",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PageTranslation_RevisionId_Locale",
                table: "Crm_PageTranslation",
                columns: new[] { "TenantId", "RevisionId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PageTranslation_SiteId",
                table: "Crm_PageTranslation",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PageTranslation_SiteId_Locale_Slug",
                table: "Crm_PageTranslation",
                columns: new[] { "TenantId", "SiteId", "Locale", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicForm_IntakeConfigId",
                table: "Crm_PublicForm",
                column: "IntakeConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicForm_SiteId_Enable",
                table: "Crm_PublicForm",
                columns: new[] { "SiteId", "Enable" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicRoute_PublicKey",
                table: "Crm_PublicRoute",
                column: "PublicKey",
                unique: true,
                filter: "[PublicKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicRoute_TenantId_RouteType_TargetId",
                table: "Crm_PublicRoute",
                columns: new[] { "TenantId", "RouteType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicRoute_TokenHash",
                table: "Crm_PublicRoute",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicSubmission_FormId",
                table: "Crm_PublicSubmission",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicSubmission_FormId_IdempotencyHash",
                table: "Crm_PublicSubmission",
                columns: new[] { "TenantId", "FormId", "IdempotencyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicSubmission_LeadId",
                table: "Crm_PublicSubmission",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_PublicSubmission_Status_CreateDate",
                table: "Crm_PublicSubmission",
                columns: new[] { "Status", "CreateDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_Site_SiteKey",
                table: "Crm_Site",
                columns: new[] { "TenantId", "SiteKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_SitePage_SiteId",
                table: "Crm_SitePage",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Crm_SitePage_SiteId_PageKey",
                table: "Crm_SitePage",
                columns: new[] { "TenantId", "SiteId", "PageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crm_SourceTouch_LeadId_TouchedAt",
                table: "Crm_SourceTouch",
                columns: new[] { "LeadId", "TouchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Crm_StageHistory_EntityType_EntityId_ChangedAt",
                table: "Crm_StageHistory",
                columns: new[] { "EntityType", "EntityId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Crm_Activity");

            migrationBuilder.DropTable(
                name: "Crm_Collaborator");

            migrationBuilder.DropTable(
                name: "Crm_ErpLink");

            migrationBuilder.DropTable(
                name: "Crm_IntakeMember");

            migrationBuilder.DropTable(
                name: "Crm_MediaAsset");

            migrationBuilder.DropTable(
                name: "Crm_MergeRecord");

            migrationBuilder.DropTable(
                name: "Crm_PageTranslation");

            migrationBuilder.DropTable(
                name: "Crm_PublicRoute");

            migrationBuilder.DropTable(
                name: "Crm_PublicSubmission");

            migrationBuilder.DropTable(
                name: "Crm_SourceTouch");

            migrationBuilder.DropTable(
                name: "Crm_StageHistory");

            migrationBuilder.DropTable(
                name: "Crm_Opportunity");

            migrationBuilder.DropTable(
                name: "Crm_PageRevision");

            migrationBuilder.DropTable(
                name: "Crm_PublicForm");

            migrationBuilder.DropTable(
                name: "Crm_Lead");

            migrationBuilder.DropTable(
                name: "Crm_SitePage");

            migrationBuilder.DropTable(
                name: "Crm_IntakeConfig");

            migrationBuilder.DropTable(
                name: "Crm_Contact");

            migrationBuilder.DropTable(
                name: "Crm_Site");

            migrationBuilder.DropTable(
                name: "Crm_Account");
        }
    }
}
