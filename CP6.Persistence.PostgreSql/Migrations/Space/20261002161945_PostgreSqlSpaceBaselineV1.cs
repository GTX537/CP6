using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Persistence.PostgreSql.Migrations.Space
{
    /// <inheritdoc />
    public partial class PostgreSqlSpaceBaselineV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "Space_AiTenantPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DataPolicy = table.Column<string>(type: "bpchar", unicode: false, maxLength: 32, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AllowedSiteIdsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AllowedProviderAliasesJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    MaxConcurrentRuns = table.Column<int>(type: "integer", nullable: false),
                    ExternalProviderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DailyBudgetMinor = table.Column<long>(type: "bigint", nullable: true),
                    MonthlyBudgetMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AiTenantPolicy", x => x.Id);
                    table.UniqueConstraint("AK_Space_AiTenantPolicy_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Budget", "(\"DailyBudgetMinor\" IS NULL OR \"DailyBudgetMinor\" >= 0) AND (\"MonthlyBudgetMinor\" IS NULL OR \"MonthlyBudgetMinor\" >= 0) AND (\"DailyBudgetMinor\" IS NULL OR \"MonthlyBudgetMinor\" IS NULL OR \"MonthlyBudgetMinor\" >= \"DailyBudgetMinor\")")
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_Budget")
                        .Annotation("CP6:OriginalSql", "([DailyBudgetMinor] IS NULL OR [DailyBudgetMinor] >= 0) AND ([MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= 0) AND ([DailyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= [DailyBudgetMinor])");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Concurrency", "\"MaxConcurrentRuns\" >= 1 AND \"MaxConcurrentRuns\" <= 3")
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_Concurrency")
                        .Annotation("CP6:OriginalSql", "[MaxConcurrentRuns] >= 1 AND [MaxConcurrentRuns] <= 3");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Currency", "(\"DailyBudgetMinor\" IS NULL AND \"MonthlyBudgetMinor\" IS NULL) OR \"Currency\" IS NOT NULL")
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_Currency")
                        .Annotation("CP6:OriginalSql", "([DailyBudgetMinor] IS NULL AND [MonthlyBudgetMinor] IS NULL) OR [Currency] IS NOT NULL");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Currency_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_Currency_AsciiDomain");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Currency_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8')) <= 3")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_Currency_Cp936Capacity");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_DataPolicy_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DataPolicy\"), 'UTF8')) <= 32")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_DataPolicy_Cp936Capacity");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Version", "\"Version\" >= 1")
                        .Annotation("CP6:OriginalName", "CK_Space_AiTenantPolicy_Version")
                        .Annotation("CP6:OriginalSql", "[Version] >= 1");
                });

            migrationBuilder.CreateTable(
                name: "Space_Asset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Category = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Description = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Asset", x => x.Id);
                    table.UniqueConstraint("AK_Space_Asset_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_Asset_AssetCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AssetCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Asset_AssetCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Asset_Category_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Category\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Asset_Category_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Asset_Description_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Description\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Asset_Description_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Asset_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Asset_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Asset_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Asset_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_Asset_ScopeOwner", "(\"Scope\" = 0 AND \"OwnerTenantId\" = '00000000-0000-0000-0000-000000000000') OR (\"Scope\" = 1 AND \"OwnerTenantId\" <> '00000000-0000-0000-0000-000000000000')")
                        .Annotation("CP6:OriginalName", "CK_Space_Asset_ScopeOwner")
                        .Annotation("CP6:OriginalSql", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                });

            migrationBuilder.CreateTable(
                name: "Space_CadSiteProviderConfiguration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationRevision = table.Column<long>(type: "bigint", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeReason = table.Column<string>(type: "bpchar", maxLength: 500, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_CadSiteProviderConfiguration", x => x.Id);
                    table.UniqueConstraint("AK_Space_CadSiteProviderConfiguration_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_CadSiteProviderConfiguration_ChangeReason_4e1249a7a43c", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ChangeReason\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderConfiguration_ChangeReason_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_ExcelMappingProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedName = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExcelMappingProfile", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExcelMappingProfile_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExcelMappingProfile_CurrentVersion", "\"CurrentVersion\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfile_CurrentVersion")
                        .Annotation("CP6:OriginalSql", "[CurrentVersion] > 0");
                    table.CheckConstraint("CK_Space_ExcelMappingProfile_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfile_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ExcelMappingProfile_NormalizedName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"NormalizedName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfile_NormalizedName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ExcelMappingProfile_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfile_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalOrganization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    BusinessPartnerType = table.Column<string>(type: "bpchar", unicode: false, maxLength: 50, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    BusinessPartnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "bpchar", maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    SecurityStamp = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalOrganization", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalOrganization_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalOrganization_BusinessPartner", "(\"BusinessPartnerType\" IS NULL AND \"BusinessPartnerId\" IS NULL) OR (\"BusinessPartnerType\" IS NOT NULL AND \"BusinessPartnerId\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_BusinessPartner")
                        .Annotation("CP6:OriginalSql", "([BusinessPartnerType] IS NULL AND [BusinessPartnerId] IS NULL) OR ([BusinessPartnerType] IS NOT NULL AND [BusinessPartnerId] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_ExternalOrganization_BusinessPartnerType_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"BusinessPartnerType\"), 'UTF8')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_BusinessPartnerType_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ExternalOrganization_Code_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Code\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_Code_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ExternalOrganization_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ExternalOrganization_NormalizedCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"NormalizedCode\"), 'UTF8')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_NormalizedCode_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ExternalOrganization_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ExternalOrganization_Status", "\"Status\" >= 0 AND \"Status\" <= 2")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_Status")
                        .Annotation("CP6:OriginalSql", "[Status] >= 0 AND [Status] <= 2");
                    table.CheckConstraint("CK_Space_ExternalOrganization_Type", "\"Type\" >= 0 AND \"Type\" <= 2")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalOrganization_Type")
                        .Annotation("CP6:OriginalSql", "[Type] >= 0 AND [Type] <= 2");
                });

            migrationBuilder.CreateTable(
                name: "Space_FieldPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedName = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AudienceType = table.Column<short>(type: "smallint", nullable: false),
                    CanExport = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PolicyVersion = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_FieldPolicy", x => x.Id);
                    table.UniqueConstraint("AK_Space_FieldPolicy_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_FieldPolicy_AudienceType", "\"AudienceType\" >= 0 AND \"AudienceType\" <= 2")
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicy_AudienceType")
                        .Annotation("CP6:OriginalSql", "[AudienceType] >= 0 AND [AudienceType] <= 2");
                    table.CheckConstraint("CK_Space_FieldPolicy_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicy_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_FieldPolicy_NormalizedName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"NormalizedName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicy_NormalizedName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_FieldPolicy_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicy_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_FieldPolicy_Status", "\"Status\" >= 0 AND \"Status\" <= 1")
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicy_Status")
                        .Annotation("CP6:OriginalSql", "[Status] >= 0 AND [Status] <= 1");
                    table.CheckConstraint("CK_Space_FieldPolicy_Version", "\"PolicyVersion\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicy_Version")
                        .Annotation("CP6:OriginalSql", "[PolicyVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Space_File",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "bpchar", maxLength: 500, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    OriginalName = table.Column<string>(type: "bpchar", maxLength: 260, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DeclaredContentType = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    DetectedContentType = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Extension = table.Column<string>(type: "bpchar", maxLength: 20, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    ScanEngine = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    SignatureVersion = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ScanResultCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RetentionClass = table.Column<short>(type: "smallint", nullable: false),
                    RetainUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletionRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContentDeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_File", x => x.Id);
                    table.UniqueConstraint("AK_Space_File_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_File_ContentDeletion", "\"ContentDeletedAtUtc\" IS NULL OR (\"State\" = 5 AND \"DeletionRequestedAtUtc\" IS NOT NULL AND \"IsDeleted\" = TRUE)")
                        .Annotation("CP6:OriginalName", "CK_Space_File_ContentDeletion")
                        .Annotation("CP6:OriginalSql", "[ContentDeletedAtUtc] IS NULL OR ([State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [IsDeleted] = 1)");
                    table.CheckConstraint("CK_Space_File_DeclaredContentType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DeclaredContentType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_DeclaredContentType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_File_DetectedContentType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DetectedContentType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_DetectedContentType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_File_Extension_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Extension\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 20")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_Extension_Utf16Capacity");
                    table.CheckConstraint("CK_Space_File_OriginalName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"OriginalName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 260")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_OriginalName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_File_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_File_ScanEngine_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ScanEngine\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_ScanEngine_Utf16Capacity");
                    table.CheckConstraint("CK_Space_File_ScanResultCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ScanResultCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_ScanResultCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_File_Sha256_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"Sha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"Sha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_Sha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_File_Sha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Sha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_Sha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_File_SignatureVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SignatureVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_SignatureVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_File_StorageKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"StorageKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_File_StorageKey_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_IdempotencyRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    IdempotencyKeyHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ResponseJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: false),
                    ReplayUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetainUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_IdempotencyRecord", x => x.Id);
                    table.CheckConstraint("CK_Space_IdempotencyRecord_IdempotencyKeyHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"IdempotencyKeyHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"IdempotencyKeyHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_IdempotencyRecord_IdempotencyKeyHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_IdempotencyRecord_IdempotencyKeyHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"IdempotencyKeyHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_IdempotencyRecord_IdempotencyKeyHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_IdempotencyRecord_Operation_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Operation\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_IdempotencyRecord_Operation_Utf16Capacity");
                    table.CheckConstraint("CK_Space_IdempotencyRecord_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_IdempotencyRecord_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_IdempotencyRecord_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_IdempotencyRecord_RequestHash_Cp936Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_Job",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<short>(type: "smallint", nullable: false),
                    SubjectType = table.Column<short>(type: "smallint", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessKey = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    InputHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedBy = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActiveAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseRevision = table.Column<long>(type: "bigint", nullable: false),
                    ProgressDone = table.Column<long>(type: "bigint", nullable: false),
                    ProgressTotal = table.Column<long>(type: "bigint", nullable: false),
                    ProgressStage = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ResultSummaryJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LastFailureKind = table.Column<short>(type: "smallint", nullable: true),
                    LastErrorCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LastErrorSummary = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RetryOfJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationRequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Job", x => x.Id);
                    table.UniqueConstraint("AK_Space_Job_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_Job_Attempts", "\"AttemptCount\" >= 0 AND \"MaxAttempts\" BETWEEN 1 AND 20 AND \"AttemptCount\" <= \"MaxAttempts\"")
                        .Annotation("CP6:OriginalName", "CK_Space_Job_Attempts")
                        .Annotation("CP6:OriginalSql", "[AttemptCount] >= 0 AND [MaxAttempts] BETWEEN 1 AND 20 AND [AttemptCount] <= [MaxAttempts]");
                    table.CheckConstraint("CK_Space_Job_BusinessKey_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"BusinessKey\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"BusinessKey\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_BusinessKey_AsciiDomain");
                    table.CheckConstraint("CK_Space_Job_BusinessKey_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"BusinessKey\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_BusinessKey_Cp936Capacity");
                    table.CheckConstraint("CK_Space_Job_InputHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"InputHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"InputHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_InputHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_Job_InputHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"InputHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_InputHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_Job_LastErrorCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LastErrorCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_LastErrorCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Job_LastErrorSummary_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LastErrorSummary\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_LastErrorSummary_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Job_Lease", "(\"Status\" = 1 AND \"LockedBy\" IS NOT NULL AND \"LockedAtUtc\" IS NOT NULL AND \"LockExpiresAtUtc\" IS NOT NULL AND \"ActiveAttemptId\" IS NOT NULL) OR (\"Status\" <> 1 AND \"LockedBy\" IS NULL AND \"LockedAtUtc\" IS NULL AND \"LockExpiresAtUtc\" IS NULL AND \"ActiveAttemptId\" IS NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_Job_Lease")
                        .Annotation("CP6:OriginalSql", "([Status] = 1 AND [LockedBy] IS NOT NULL AND [LockedAtUtc] IS NOT NULL AND [LockExpiresAtUtc] IS NOT NULL AND [ActiveAttemptId] IS NOT NULL) OR ([Status] <> 1 AND [LockedBy] IS NULL AND [LockedAtUtc] IS NULL AND [LockExpiresAtUtc] IS NULL AND [ActiveAttemptId] IS NULL)");
                    table.CheckConstraint("CK_Space_Job_LockedBy_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LockedBy\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_LockedBy_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Job_Progress", "\"ProgressDone\" >= 0 AND \"ProgressTotal\" >= 0 AND (\"ProgressTotal\" = 0 OR \"ProgressDone\" <= \"ProgressTotal\")")
                        .Annotation("CP6:OriginalName", "CK_Space_Job_Progress")
                        .Annotation("CP6:OriginalSql", "[ProgressDone] >= 0 AND [ProgressTotal] >= 0 AND ([ProgressTotal] = 0 OR [ProgressDone] <= [ProgressTotal])");
                    table.CheckConstraint("CK_Space_Job_ProgressStage_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProgressStage\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_ProgressStage_Utf16Capacity");
                    table.CheckConstraint("CK_Space_Job_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Job_RowVersion_TokenLength");
                    table.ForeignKey(
                        name: "FK_Space_Job_RetryOf_Tenant",
                        columns: x => new { x.TenantId, x.RetryOfJobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_LayerMappingProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedName = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_LayerMappingProfile", x => x.Id);
                    table.UniqueConstraint("AK_Space_LayerMappingProfile_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_LayerMappingProfile_CurrentVersion", "\"CurrentVersion\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfile_CurrentVersion")
                        .Annotation("CP6:OriginalSql", "[CurrentVersion] > 0");
                    table.CheckConstraint("CK_Space_LayerMappingProfile_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfile_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_LayerMappingProfile_NormalizedName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"NormalizedName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfile_NormalizedName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_LayerMappingProfile_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfile_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Space_PersonnelEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    SourceEventId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PersonExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventKind = table.Column<short>(type: "smallint", nullable: false),
                    WorkState = table.Column<short>(type: "smallint", nullable: true),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    XMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    YMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    ZMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    AccuracyMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    SourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PersonnelEvent", x => x.Id);
                    table.UniqueConstraint("AK_Space_PersonnelEvent_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PersonnelEvent_Accuracy", "\"AccuracyMillimeters\" IS NULL OR (\"AccuracyMillimeters\" >= 0 AND \"XMillimeters\" IS NOT NULL AND \"YMillimeters\" IS NOT NULL AND \"ZMillimeters\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_Accuracy")
                        .Annotation("CP6:OriginalSql", "[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_Kind", "\"EventKind\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_Kind")
                        .Annotation("CP6:OriginalSql", "[EventKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_PayloadHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_PayloadHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PersonnelEvent_PayloadHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_PayloadHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PersonnelEvent_PersonExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"PersonExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_PersonExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PersonnelEvent_Shape", "(\"EventKind\" = 0 AND \"WorkState\" IS NULL AND (\"LocationLogicalId\" IS NOT NULL OR (\"FloorLogicalId\" IS NOT NULL AND \"XMillimeters\" IS NOT NULL AND \"YMillimeters\" IS NOT NULL AND \"ZMillimeters\" IS NOT NULL))) OR (\"EventKind\" = 1 AND \"WorkState\" IS NOT NULL AND \"FloorLogicalId\" IS NULL AND \"LocationLogicalId\" IS NULL AND \"XMillimeters\" IS NULL AND \"YMillimeters\" IS NULL AND \"ZMillimeters\" IS NULL AND \"AccuracyMillimeters\" IS NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_Shape")
                        .Annotation("CP6:OriginalSql", "([EventKind] = 0 AND [WorkState] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [WorkState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_SourceEventId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceEventId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_SourceEventId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PersonnelEvent_SourceId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_SourceId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PersonnelEvent_SourceKind", "\"SourceKind\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_SourceKind")
                        .Annotation("CP6:OriginalSql", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_SourceSequence", "\"SourceSequence\" IS NULL OR \"SourceSequence\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_SourceSequence")
                        .Annotation("CP6:OriginalSql", "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                    table.CheckConstraint("CK_Space_PersonnelEvent_WorkState", "\"WorkState\" IS NULL OR \"WorkState\" BETWEEN 0 AND 4")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelEvent_WorkState")
                        .Annotation("CP6:OriginalSql", "[WorkState] IS NULL OR [WorkState] BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "Space_PersonnelState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    PersonExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    XMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    YMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    ZMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    AccuracyMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    PositionOccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PositionReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PositionSourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    PositionSourceEventId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    PositionEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkState = table.Column<short>(type: "smallint", nullable: false),
                    WorkStateOccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkStateReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkStateSourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    WorkStateSourceEventId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    WorkStateEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PersonnelState", x => x.Id);
                    table.UniqueConstraint("AK_Space_PersonnelState_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PersonnelState_PersonExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"PersonExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelState_PersonExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PersonnelState_PositionSourceEventId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"PositionSourceEventId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelState_PositionSourceEventId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PersonnelState_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelState_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_PersonnelState_SourceId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelState_SourceId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PersonnelState_SourceKind", "\"SourceKind\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelState_SourceKind")
                        .Annotation("CP6:OriginalSql", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_PersonnelState_WorkState", "\"WorkState\" BETWEEN 0 AND 4")
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelState_WorkState")
                        .Annotation("CP6:OriginalSql", "[WorkState] BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_Space_PersonnelState_WorkStateSourceEventId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"WorkStateSourceEventId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PersonnelState_WorkStateSourceEventId_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_RackGenerationProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Description = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackGenerationProfile", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackGenerationProfile_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_RackGenerationProfile_Description_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Description\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfile_Description_Utf16Capacity");
                    table.CheckConstraint("CK_Space_RackGenerationProfile_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfile_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_RackGenerationProfile_ProfileCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProfileCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfile_ProfileCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_RackGenerationProfile_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfile_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_RackGenerationProfile_ScopeOwner", "(\"Scope\" = 0 AND \"OwnerTenantId\" = '00000000-0000-0000-0000-000000000000') OR (\"Scope\" = 1 AND \"OwnerTenantId\" <> '00000000-0000-0000-0000-000000000000')")
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfile_ScopeOwner")
                        .Annotation("CP6:OriginalSql", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                });

            migrationBuilder.CreateTable(
                name: "Space_RuntimeElement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PayloadJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PayloadHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RuntimeElement", x => x.Id);
                    table.CheckConstraint("CK_Space_RuntimeElement_PayloadHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RuntimeElement_PayloadHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_RuntimeElement_PayloadHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RuntimeElement_PayloadHash_Cp936Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_WarehouseTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedTemplateCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Description = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_WarehouseTemplate", x => x.Id);
                    table.UniqueConstraint("AK_Space_WarehouseTemplate_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_WarehouseTemplate_CurrentVersion", "\"CurrentVersion\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplate_CurrentVersion")
                        .Annotation("CP6:OriginalSql", "[CurrentVersion] > 0");
                    table.CheckConstraint("CK_Space_WarehouseTemplate_Description_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Description\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplate_Description_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WarehouseTemplate_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplate_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WarehouseTemplate_NormalizedTemplateCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"NormalizedTemplateCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplate_NormalizedTemplateCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WarehouseTemplate_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplate_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_WarehouseTemplate_TemplateCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"TemplateCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplate_TemplateCode_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_AssetVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    Format = table.Column<short>(type: "smallint", nullable: false),
                    ParameterSchemaJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PreviewRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RenderArtifactRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AssetVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_AssetVersion_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_AssetVersion_ContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AssetVersion_ContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_AssetVersion_ContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AssetVersion_ContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_AssetVersion_PreviewRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"PreviewRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AssetVersion_PreviewRef_Utf16Capacity");
                    table.CheckConstraint("CK_Space_AssetVersion_RenderArtifactRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RenderArtifactRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AssetVersion_RenderArtifactRef_Utf16Capacity");
                    table.CheckConstraint("CK_Space_AssetVersion_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AssetVersion_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_AssetVersion_ScopeOwner", "(\"Scope\" = 0 AND \"OwnerTenantId\" = '00000000-0000-0000-0000-000000000000') OR (\"Scope\" = 1 AND \"OwnerTenantId\" <> '00000000-0000-0000-0000-000000000000')")
                        .Annotation("CP6:OriginalName", "CK_Space_AssetVersion_ScopeOwner")
                        .Annotation("CP6:OriginalSql", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_Space_AssetVersion_VersionNo", "\"VersionNo\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_AssetVersion_VersionNo")
                        .Annotation("CP6:OriginalSql", "[VersionNo] > 0");
                    table.ForeignKey(
                        name: "FK_Space_AssetVersion_Asset_Scope_Owner_Asset",
                        columns: x => new { x.Scope, x.OwnerTenantId, x.AssetId },
                        principalTable: "Space_Asset",
                        principalColumns: new[] { "Scope", "OwnerTenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_CadSiteProviderCertification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKey = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ProviderVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    DeploymentMode = table.Column<short>(type: "smallint", nullable: false),
                    DataBoundary = table.Column<short>(type: "smallint", nullable: false),
                    ApprovalEvidenceReference = table.Column<string>(type: "bpchar", unicode: false, maxLength: 500, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SecretReference = table.Column<string>(type: "bpchar", unicode: false, maxLength: 256, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupportsDwg = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsDxf = table.Column<bool>(type: "boolean", nullable: false),
                    LicensingApproved = table.Column<bool>(type: "boolean", nullable: false),
                    SecurityApproved = table.Column<bool>(type: "boolean", nullable: false),
                    DataRegionApproved = table.Column<bool>(type: "boolean", nullable: false),
                    DeletionRetentionApproved = table.Column<bool>(type: "boolean", nullable: false),
                    QualificationScore = table.Column<int>(type: "integer", nullable: true),
                    QualificationRubricVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    GoldenDatasetSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    FrozenEnvironmentSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    QualificationEvidenceReference = table.Column<string>(type: "bpchar", unicode: false, maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_CadSiteProviderCertification", x => x.Id);
                    table.CheckConstraint("CK_Space_CadProviderCertification_QualificationScore", "\"QualificationScore\" IS NULL OR (\"QualificationScore\" >= 0 AND \"QualificationScore\" <= 100)")
                        .Annotation("CP6:OriginalName", "CK_Space_CadProviderCertification_QualificationScore")
                        .Annotation("CP6:OriginalSql", "[QualificationScore] IS NULL OR ([QualificationScore] >= 0 AND [QualificationScore] <= 100)");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_ApprovalEvid_df8ca1b098cc", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ApprovalEvidenceReference\"), 'UTF8')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_ApprovalEvidenceReference_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_FrozenEnviro_1e0600f005e7", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"FrozenEnvironmentSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_FrozenEnvironmentSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_GoldenDatase_f4a647ddff78", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"GoldenDatasetSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_GoldenDatasetSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_ProviderKey_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ProviderKey\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_ProviderKey_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_ProviderVers_727593433353", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ProviderVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_ProviderVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_Qualificatio_23a620aa3be4", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"QualificationRubricVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_QualificationRubricVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_Qualificatio_4003d20a1fe1", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"QualificationEvidenceReference\"), 'UTF8')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_QualificationEvidenceReference_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadSiteProviderCertification_SecretRefere_d87427deab71", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SecretReference\"), 'UTF8')) <= 256")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadSiteProviderCertification_SecretReference_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_CadProviderCertification_Configuration_Tenant",
                        columns: x => new { x.TenantId, x.ConfigurationId },
                        principalTable: "Space_CadSiteProviderConfiguration",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExcelMappingProfileVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefinitionHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BasedOnProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    BasedOnVersion = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExcelMappingProfileVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExcelMappingProfileVersion_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExcelMappingProfileVersion_Base", "(\"BasedOnProfileId\" IS NULL AND \"BasedOnVersion\" IS NULL) OR (\"BasedOnProfileId\" IS NOT NULL AND \"BasedOnVersion\" > 0)")
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfileVersion_Base")
                        .Annotation("CP6:OriginalSql", "([BasedOnProfileId] IS NULL AND [BasedOnVersion] IS NULL) OR ([BasedOnProfileId] IS NOT NULL AND [BasedOnVersion] > 0)");
                    table.CheckConstraint("CK_Space_ExcelMappingProfileVersion_DefinitionHash_1c48beb70dcf", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfileVersion_DefinitionHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ExcelMappingProfileVersion_DefinitionHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"DefinitionHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"DefinitionHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfileVersion_DefinitionHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ExcelMappingProfileVersion_Version", "\"Version\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_ExcelMappingProfileVersion_Version")
                        .Annotation("CP6:OriginalSql", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_Space_ExcelMappingProfileVersion_Profile_Tenant",
                        columns: x => new { x.TenantId, x.ProfileId },
                        principalTable: "Space_ExcelMappingProfile",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalMembership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SecurityStamp = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalMembership", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalMembership_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalMembership_Role", "\"Role\" >= 0 AND \"Role\" <= 2")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalMembership_Role")
                        .Annotation("CP6:OriginalSql", "[Role] >= 0 AND [Role] <= 2");
                    table.CheckConstraint("CK_Space_ExternalMembership_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalMembership_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ExternalMembership_Status", "\"Status\" >= 0 AND \"Status\" <= 3")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalMembership_Status")
                        .Annotation("CP6:OriginalSql", "[Status] >= 0 AND [Status] <= 3");
                    table.CheckConstraint("CK_Space_ExternalMembership_Validity", "\"ValidToUtc\" IS NULL OR \"ValidToUtc\" > \"ValidFromUtc\"")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalMembership_Validity")
                        .Annotation("CP6:OriginalSql", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                    table.ForeignKey(
                        name: "FK_Space_ExternalMembership_Organization_Tenant",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Space_ExternalOrganization",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldPolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanExport = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    GrantVersion = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrant", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrant_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalGrant_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrant_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ExternalGrant_Status", "\"Status\" >= 0 AND \"Status\" <= 2")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrant_Status")
                        .Annotation("CP6:OriginalSql", "[Status] >= 0 AND [Status] <= 2");
                    table.CheckConstraint("CK_Space_ExternalGrant_Validity", "\"ValidToUtc\" IS NULL OR \"ValidToUtc\" > \"ValidFromUtc\"")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrant_Validity")
                        .Annotation("CP6:OriginalSql", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                    table.CheckConstraint("CK_Space_ExternalGrant_Version", "\"GrantVersion\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrant_Version")
                        .Annotation("CP6:OriginalSql", "[GrantVersion] > 0");
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrant_FieldPolicy_Tenant",
                        columns: x => new { x.TenantId, x.FieldPolicyId },
                        principalTable: "Space_FieldPolicy",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrant_Organization_Tenant",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Space_ExternalOrganization",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_FieldPolicyField",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<short>(type: "smallint", nullable: false),
                    FieldName = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedFieldName = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    MaskingRule = table.Column<short>(type: "smallint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_FieldPolicyField", x => x.Id);
                    table.UniqueConstraint("AK_Space_FieldPolicyField_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_FieldPolicyField_FieldName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FieldName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicyField_FieldName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_FieldPolicyField_MaskingRule", "\"MaskingRule\" >= 0 AND \"MaskingRule\" <= 3")
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicyField_MaskingRule")
                        .Annotation("CP6:OriginalSql", "[MaskingRule] >= 0 AND [MaskingRule] <= 3");
                    table.CheckConstraint("CK_Space_FieldPolicyField_NormalizedFieldName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"NormalizedFieldName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicyField_NormalizedFieldName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_FieldPolicyField_ResourceType", "\"ResourceType\" >= 0 AND \"ResourceType\" <= 2")
                        .Annotation("CP6:OriginalName", "CK_Space_FieldPolicyField_ResourceType")
                        .Annotation("CP6:OriginalSql", "[ResourceType] >= 0 AND [ResourceType] <= 2");
                    table.ForeignKey(
                        name: "FK_Space_FieldPolicyField_Policy_Tenant",
                        columns: x => new { x.TenantId, x.PolicyId },
                        principalTable: "Space_FieldPolicy",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_LayerMappingProfileVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefinitionHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BasedOnProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    BasedOnVersion = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_LayerMappingProfileVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_LayerMappingProfileVersion_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_LayerMappingProfileVersion_Base", "(\"BasedOnProfileId\" IS NULL AND \"BasedOnVersion\" IS NULL) OR (\"BasedOnProfileId\" IS NOT NULL AND \"BasedOnVersion\" > 0)")
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfileVersion_Base")
                        .Annotation("CP6:OriginalSql", "([BasedOnProfileId] IS NULL AND [BasedOnVersion] IS NULL) OR ([BasedOnProfileId] IS NOT NULL AND [BasedOnVersion] > 0)");
                    table.CheckConstraint("CK_Space_LayerMappingProfileVersion_DefinitionHash_5d506cedf595", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfileVersion_DefinitionHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_LayerMappingProfileVersion_DefinitionHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"DefinitionHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"DefinitionHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfileVersion_DefinitionHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_LayerMappingProfileVersion_Version", "\"Version\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_LayerMappingProfileVersion_Version")
                        .Annotation("CP6:OriginalSql", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_Space_LayerMappingProfileVersion_Profile_Tenant",
                        columns: x => new { x.TenantId, x.ProfileId },
                        principalTable: "Space_LayerMappingProfile",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_RackGenerationProfileVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    RackWidthMillimeters = table.Column<int>(type: "integer", nullable: false),
                    RackDepthMillimeters = table.Column<int>(type: "integer", nullable: false),
                    RackHeightMillimeters = table.Column<int>(type: "integer", nullable: false),
                    LevelsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    LocationCount = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackGenerationProfileVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackGenerationProfileVersion_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_ContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfileVersion_ContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_ContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfileVersion_ContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_Dimensions", "\"RackWidthMillimeters\" > 0 AND \"RackDepthMillimeters\" > 0 AND \"RackHeightMillimeters\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfileVersion_Dimensions")
                        .Annotation("CP6:OriginalSql", "[RackWidthMillimeters] > 0 AND [RackDepthMillimeters] > 0 AND [RackHeightMillimeters] > 0");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_LocationCount", "\"LocationCount\" > 0 AND \"LocationCount\" <= 10000000")
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfileVersion_LocationCount")
                        .Annotation("CP6:OriginalSql", "[LocationCount] > 0 AND [LocationCount] <= 10000000");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfileVersion_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_ScopeOwner", "(\"Scope\" = 0 AND \"OwnerTenantId\" = '00000000-0000-0000-0000-000000000000') OR (\"Scope\" = 1 AND \"OwnerTenantId\" <> '00000000-0000-0000-0000-000000000000')")
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfileVersion_ScopeOwner")
                        .Annotation("CP6:OriginalSql", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_VersionNo", "\"VersionNo\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_RackGenerationProfileVersion_VersionNo")
                        .Annotation("CP6:OriginalSql", "[VersionNo] > 0");
                    table.ForeignKey(
                        name: "FK_Space_RackGenerationProfileVersion_Profile_Scope_Owner",
                        columns: x => new { x.Scope, x.OwnerTenantId, x.ProfileId },
                        principalTable: "Space_RackGenerationProfile",
                        principalColumns: new[] { "Scope", "OwnerTenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_WarehouseTemplateVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    FloorCount = table.Column<int>(type: "integer", nullable: false),
                    ZoneCount = table.Column<int>(type: "integer", nullable: false),
                    AisleCount = table.Column<int>(type: "integer", nullable: false),
                    RackCount = table.Column<int>(type: "integer", nullable: false),
                    LocationCount = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_WarehouseTemplateVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_WarehouseTemplateVersion_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_ContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplateVersion_ContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_ContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplateVersion_ContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_Counts", "\"FloorCount\" > 0 AND \"ZoneCount\" >= 0 AND \"AisleCount\" >= 0 AND \"RackCount\" >= 0 AND \"LocationCount\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplateVersion_Counts")
                        .Annotation("CP6:OriginalSql", "[FloorCount] > 0 AND [ZoneCount] >= 0 AND [AisleCount] >= 0 AND [RackCount] >= 0 AND [LocationCount] >= 0");
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_SchemaVersion", "\"SchemaVersion\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplateVersion_SchemaVersion")
                        .Annotation("CP6:OriginalSql", "[SchemaVersion] > 0");
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_VersionNo", "\"VersionNo\" > 0")
                        .Annotation("CP6:OriginalName", "CK_Space_WarehouseTemplateVersion_VersionNo")
                        .Annotation("CP6:OriginalSql", "[VersionNo] > 0");
                    table.ForeignKey(
                        name: "FK_Space_WarehouseTemplateVersion_Template_Tenant",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "Space_WarehouseTemplate",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantFloor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantFloor", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantFloor_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantFloor_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantObject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessObjectType = table.Column<string>(type: "bpchar", maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedBusinessObjectType = table.Column<string>(type: "bpchar", unicode: false, maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BusinessObjectId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedBusinessObjectId = table.Column<string>(type: "bpchar", unicode: false, maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantObject", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantObject_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalGrantObject_BusinessObjectId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"BusinessObjectId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrantObject_BusinessObjectId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ExternalGrantObject_BusinessObjectType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"BusinessObjectType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrantObject_BusinessObjectType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ExternalGrantObject_NormalizedBusinessObj_acbb3f486c63", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"NormalizedBusinessObjectType\"), 'UTF8')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrantObject_NormalizedBusinessObjectType_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ExternalGrantObject_NormalizedBusinessObj_cb68a8e8ece9", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"NormalizedBusinessObjectId\"), 'UTF8')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrantObject_NormalizedBusinessObjectId_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantObject_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantOwner",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedOwnerId = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantOwner", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantOwner_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalGrantOwner_NormalizedOwnerId_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"NormalizedOwnerId\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrantOwner_NormalizedOwnerId_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ExternalGrantOwner_OwnerId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"OwnerId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ExternalGrantOwner_OwnerId_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantOwner_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantZone",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantZone", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantZone_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantZone_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_AiBudgetReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderRequestKey = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PeriodDay = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    ReservedCostMinor = table.Column<long>(type: "bigint", nullable: false),
                    ActualCostMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AiBudgetReservation", x => x.Id);
                    table.UniqueConstraint("AK_Space_AiBudgetReservation_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Cost", "\"ReservedCostMinor\" >= 0 AND (\"ActualCostMinor\" IS NULL OR \"ActualCostMinor\" >= 0)")
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_Cost")
                        .Annotation("CP6:OriginalSql", "[ReservedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Currency", "\"ReservedCostMinor\" = 0 OR \"Currency\" IS NOT NULL")
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_Currency")
                        .Annotation("CP6:OriginalSql", "[ReservedCostMinor] = 0 OR [Currency] IS NOT NULL");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Currency_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_Currency_AsciiDomain");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Currency_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8')) <= 3")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_Currency_Cp936Capacity");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Period", "\"PeriodMonth\" = EXTRACT(YEAR FROM \"PeriodDay\")::integer * 100 + EXTRACT(MONTH FROM \"PeriodDay\")::integer")
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_Period")
                        .Annotation("CP6:OriginalSql", "[PeriodMonth] = YEAR([PeriodDay]) * 100 + MONTH([PeriodDay])");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_ProviderRequestKey_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ProviderRequestKey\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ProviderRequestKey\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_ProviderRequestKey_AsciiDomain");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_ProviderRequestKey_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ProviderRequestKey\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_ProviderRequestKey_Cp936Capacity");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiBudgetReservation_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Space_AisleRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    AisleCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PolygonJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CenterlineJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Direction = table.Column<short>(type: "smallint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AisleRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_AisleRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_AisleRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_AisleRevision_AisleCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AisleCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AisleRevision_AisleCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_AisleRevision_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AisleRevision_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_AisleRevision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AisleRevision_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_AisleRevision_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AisleRevision_SourceRef_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_AiUsageRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "bpchar", maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ProviderModel = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ProviderRequestIdHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    InputUnits = table.Column<long>(type: "bigint", nullable: false),
                    OutputUnits = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedCostMinor = table.Column<long>(type: "bigint", nullable: false),
                    ActualCostMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AiUsageRecord", x => x.Id);
                    table.UniqueConstraint("AK_Space_AiUsageRecord_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_AiUsageRecord_Cost", "\"EstimatedCostMinor\" >= 0 AND (\"ActualCostMinor\" IS NULL OR \"ActualCostMinor\" >= 0)")
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_Cost")
                        .Annotation("CP6:OriginalSql", "[EstimatedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)");
                    table.CheckConstraint("CK_Space_AiUsageRecord_Currency_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_Currency_AsciiDomain");
                    table.CheckConstraint("CK_Space_AiUsageRecord_Currency_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Currency\"), 'UTF8')) <= 3")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_Currency_Cp936Capacity");
                    table.CheckConstraint("CK_Space_AiUsageRecord_Latency", "\"LatencyMs\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_Latency")
                        .Annotation("CP6:OriginalSql", "[LatencyMs] >= 0");
                    table.CheckConstraint("CK_Space_AiUsageRecord_ProviderCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProviderCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_ProviderCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_AiUsageRecord_ProviderModel_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProviderModel\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_ProviderModel_Utf16Capacity");
                    table.CheckConstraint("CK_Space_AiUsageRecord_ProviderRequestIdHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ProviderRequestIdHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ProviderRequestIdHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_ProviderRequestIdHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_AiUsageRecord_ProviderRequestIdHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ProviderRequestIdHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_ProviderRequestIdHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_AiUsageRecord_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_AiUsageRecord_Units", "\"InputUnits\" >= 0 AND \"OutputUnits\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_AiUsageRecord_Units")
                        .Annotation("CP6:OriginalSql", "[InputUnits] >= 0 AND [OutputUnits] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Space_Artifact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArtifactType = table.Column<short>(type: "smallint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "bpchar", maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Artifact", x => x.Id);
                    table.UniqueConstraint("AK_Space_Artifact_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_Artifact_SchemaVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SchemaVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Artifact_SchemaVersion_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_Artifact_File_Tenant",
                        columns: x => new { x.TenantId, x.FileId },
                        principalTable: "Space_File",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_Artifact_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_JobAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    WorkerId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    InputHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ProcessorVersion = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ResourceUsageJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    FailureKind = table.Column<short>(type: "smallint", nullable: true),
                    ErrorCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    SanitizedError = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    DiagnosticArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_JobAttempt", x => x.Id);
                    table.UniqueConstraint("AK_Space_JobAttempt_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_JobAttempt_ErrorCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ErrorCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobAttempt_ErrorCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_JobAttempt_InputHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"InputHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"InputHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobAttempt_InputHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_JobAttempt_InputHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"InputHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobAttempt_InputHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_JobAttempt_OutcomeTime", "(\"Outcome\" = 0 AND \"FinishedAtUtc\" IS NULL) OR (\"Outcome\" <> 0 AND \"FinishedAtUtc\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_JobAttempt_OutcomeTime")
                        .Annotation("CP6:OriginalSql", "([Outcome] = 0 AND [FinishedAtUtc] IS NULL) OR ([Outcome] <> 0 AND [FinishedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_JobAttempt_ProcessorVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProcessorVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobAttempt_ProcessorVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_JobAttempt_SanitizedError_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SanitizedError\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobAttempt_SanitizedError_Utf16Capacity");
                    table.CheckConstraint("CK_Space_JobAttempt_WorkerId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"WorkerId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobAttempt_WorkerId_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_JobAttempt_DiagnosticArtifact_Tenant",
                        columns: x => new { x.TenantId, x.DiagnosticArtifactId },
                        principalTable: "Space_Artifact",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_JobAttempt_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_JobStep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepNo = table.Column<int>(type: "integer", nullable: false),
                    StepCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckpointJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    OutputHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_JobStep", x => x.Id);
                    table.CheckConstraint("CK_Space_JobStep_OutputHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"OutputHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"OutputHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobStep_OutputHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_JobStep_OutputHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"OutputHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobStep_OutputHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_JobStep_StatusTime", "(\"Status\" = 0 AND \"FinishedAtUtc\" IS NULL) OR (\"Status\" <> 0 AND \"FinishedAtUtc\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_JobStep_StatusTime")
                        .Annotation("CP6:OriginalSql", "([Status] = 0 AND [FinishedAtUtc] IS NULL) OR ([Status] <> 0 AND [FinishedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_JobStep_StepCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"StepCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_JobStep_StepCode_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_JobStep_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_JobAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_CadParsePreparation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedUnit = table.Column<string>(type: "bpchar", maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ConfirmedScaleToMillimeters = table.Column<decimal>(type: "numeric(18,9)", precision: 18, scale: 9, nullable: false),
                    CoordinateMetadataJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CoordinateTransformSha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    MappingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    MappingDefinitionSha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    MappingPreviewSha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    MappingReplaySnapshotJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SemanticPreviewSha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ProviderKey = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ProviderVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ReadyForParsing = table.Column<bool>(type: "boolean", nullable: false),
                    BaseContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    BaseContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_CadParsePreparation", x => x.Id);
                    table.CheckConstraint("CK_Space_CadParsePreparation_BaseContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"BaseContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"BaseContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_BaseContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_CadParsePreparation_BaseContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"BaseContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_BaseContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_ConfirmedUnit_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ConfirmedUnit\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_ConfirmedUnit_Utf16Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_CoordinateTransformSh_ab36f1421567", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"CoordinateTransformSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_CoordinateTransformSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_CoordinateTransformSh_c285774aa6e9", "octet_length(convert_from(pg_catalog.bpcharsend(\"CoordinateTransformSha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"CoordinateTransformSha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_CoordinateTransformSha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_CadParsePreparation_MappingDefinitionSha2_be76c0b0ffae", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"MappingDefinitionSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_MappingDefinitionSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_MappingDefinitionSha2_c33e87ac909f", "octet_length(convert_from(pg_catalog.bpcharsend(\"MappingDefinitionSha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"MappingDefinitionSha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_MappingDefinitionSha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_CadParsePreparation_MappingPreviewSha256_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"MappingPreviewSha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"MappingPreviewSha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_MappingPreviewSha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_CadParsePreparation_MappingPreviewSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"MappingPreviewSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_MappingPreviewSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_ProviderKey_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ProviderKey\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_ProviderKey_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_ProviderVersion_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ProviderVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_ProviderVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_SemanticPreviewSha256_6de7b2991639", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SemanticPreviewSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_SemanticPreviewSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_CadParsePreparation_SemanticPreviewSha256_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"SemanticPreviewSha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SemanticPreviewSha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_SemanticPreviewSha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_CadParsePreparation_SourceSha256_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"SourceSha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SourceSha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_SourceSha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_CadParsePreparation_SourceSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SourceSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_CadParsePreparation_SourceSha256_Cp936Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_DesignAttribute",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectType = table.Column<string>(type: "bpchar", maxLength: 20, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ObjectLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Namespace = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Key = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Value = table.Column<string>(type: "bpchar", maxLength: 4000, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Unit = table.Column<string>(type: "bpchar", maxLength: 50, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_DesignAttribute", x => x.Id);
                    table.UniqueConstraint("AK_Space_DesignAttribute_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.CheckConstraint("CK_Space_DesignAttribute_Key_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Key\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DesignAttribute_Key_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DesignAttribute_Namespace_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Namespace\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DesignAttribute_Namespace_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DesignAttribute_ObjectType", "\"ObjectType\" IN ('Rack', 'RackLevel', 'Location')")
                        .Annotation("CP6:OriginalName", "CK_Space_DesignAttribute_ObjectType")
                        .Annotation("CP6:OriginalSql", "[ObjectType] IN ('Rack', 'RackLevel', 'Location')");
                    table.CheckConstraint("CK_Space_DesignAttribute_ObjectType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ObjectType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 20")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DesignAttribute_ObjectType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DesignAttribute_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DesignAttribute_SourceRef_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DesignAttribute_Unit_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Unit\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DesignAttribute_Unit_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DesignAttribute_Value_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Value\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 4000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DesignAttribute_Value_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_DeviceAlarmState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DeviceMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlarmExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AlarmCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    AlarmSeverity = table.Column<short>(type: "smallint", nullable: true),
                    AlarmMessage = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    SourceEventId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_DeviceAlarmState", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceAlarmState_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceAlarmState_ActiveShape", "\"IsActive\" = FALSE OR (\"AlarmCode\" IS NOT NULL AND \"AlarmSeverity\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_ActiveShape")
                        .Annotation("CP6:OriginalSql", "[IsActive] = 0 OR ([AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_AlarmCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AlarmCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_AlarmCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_AlarmExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AlarmExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_AlarmExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_AlarmMessage_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AlarmMessage\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_AlarmMessage_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_DeviceExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DeviceExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_DeviceExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_Severity", "\"AlarmSeverity\" IS NULL OR \"AlarmSeverity\" BETWEEN 0 AND 2")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_Severity")
                        .Annotation("CP6:OriginalSql", "[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_SourceEventId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceEventId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_SourceEventId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_SourceId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_SourceId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_SourceKind", "\"SourceKind\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_SourceKind")
                        .Annotation("CP6:OriginalSql", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_SourceSequence", "\"SourceSequence\" IS NULL OR \"SourceSequence\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceAlarmState_SourceSequence")
                        .Annotation("CP6:OriginalSql", "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Space_DeviceEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    SourceEventId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DeviceMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DeviceKind = table.Column<short>(type: "smallint", nullable: false),
                    ElementLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKind = table.Column<short>(type: "smallint", nullable: false),
                    OperatingState = table.Column<short>(type: "smallint", nullable: true),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    XMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    YMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    ZMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    AccuracyMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    AlarmExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    AlarmCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    AlarmSeverity = table.Column<short>(type: "smallint", nullable: true),
                    AlarmMessage = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    SourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_DeviceEvent", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceEvent_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceEvent_Accuracy", "\"AccuracyMillimeters\" IS NULL OR (\"AccuracyMillimeters\" >= 0 AND \"XMillimeters\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_Accuracy")
                        .Annotation("CP6:OriginalSql", "[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceEvent_AlarmCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AlarmCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_AlarmCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceEvent_AlarmExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AlarmExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_AlarmExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceEvent_AlarmMessage_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AlarmMessage\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_AlarmMessage_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceEvent_AlarmSeverity", "\"AlarmSeverity\" IS NULL OR \"AlarmSeverity\" BETWEEN 0 AND 2")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_AlarmSeverity")
                        .Annotation("CP6:OriginalSql", "[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_Space_DeviceEvent_CoordinateTriple", "(\"XMillimeters\" IS NULL AND \"YMillimeters\" IS NULL AND \"ZMillimeters\" IS NULL) OR (\"XMillimeters\" IS NOT NULL AND \"YMillimeters\" IS NOT NULL AND \"ZMillimeters\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_CoordinateTriple")
                        .Annotation("CP6:OriginalSql", "([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceEvent_DeviceExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DeviceExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_DeviceExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceEvent_DeviceKind", "\"DeviceKind\" BETWEEN 0 AND 7")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_DeviceKind")
                        .Annotation("CP6:OriginalSql", "[DeviceKind] BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_Space_DeviceEvent_Kind", "\"EventKind\" BETWEEN 0 AND 3")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_Kind")
                        .Annotation("CP6:OriginalSql", "[EventKind] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_Space_DeviceEvent_OperatingState", "\"OperatingState\" IS NULL OR \"OperatingState\" BETWEEN 0 AND 6")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_OperatingState")
                        .Annotation("CP6:OriginalSql", "[OperatingState] IS NULL OR [OperatingState] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_Space_DeviceEvent_PayloadHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_PayloadHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_DeviceEvent_PayloadHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_PayloadHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_DeviceEvent_Shape", "(\"EventKind\" = 0 AND \"OperatingState\" IS NULL AND \"AlarmExternalId\" IS NULL AND \"AlarmCode\" IS NULL AND \"AlarmSeverity\" IS NULL AND \"AlarmMessage\" IS NULL AND (\"LocationLogicalId\" IS NOT NULL OR (\"FloorLogicalId\" IS NOT NULL AND \"XMillimeters\" IS NOT NULL))) OR (\"EventKind\" = 1 AND \"OperatingState\" IS NOT NULL AND \"FloorLogicalId\" IS NULL AND \"LocationLogicalId\" IS NULL AND \"XMillimeters\" IS NULL AND \"YMillimeters\" IS NULL AND \"ZMillimeters\" IS NULL AND \"AccuracyMillimeters\" IS NULL AND \"AlarmExternalId\" IS NULL AND \"AlarmCode\" IS NULL AND \"AlarmSeverity\" IS NULL AND \"AlarmMessage\" IS NULL) OR (\"EventKind\" = 2 AND \"OperatingState\" IS NULL AND \"FloorLogicalId\" IS NULL AND \"LocationLogicalId\" IS NULL AND \"XMillimeters\" IS NULL AND \"YMillimeters\" IS NULL AND \"ZMillimeters\" IS NULL AND \"AccuracyMillimeters\" IS NULL AND \"AlarmExternalId\" IS NOT NULL AND \"AlarmCode\" IS NOT NULL AND \"AlarmSeverity\" IS NOT NULL) OR (\"EventKind\" = 3 AND \"OperatingState\" IS NULL AND \"FloorLogicalId\" IS NULL AND \"LocationLogicalId\" IS NULL AND \"XMillimeters\" IS NULL AND \"YMillimeters\" IS NULL AND \"ZMillimeters\" IS NULL AND \"AccuracyMillimeters\" IS NULL AND \"AlarmExternalId\" IS NOT NULL AND \"AlarmCode\" IS NULL AND \"AlarmSeverity\" IS NULL AND \"AlarmMessage\" IS NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_Shape")
                        .Annotation("CP6:OriginalSql", "([EventKind] = 0 AND [OperatingState] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [OperatingState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL) OR ([EventKind] = 2 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL) OR ([EventKind] = 3 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL)");
                    table.CheckConstraint("CK_Space_DeviceEvent_SourceEventId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceEventId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_SourceEventId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceEvent_SourceId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_SourceId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceEvent_SourceKind", "\"SourceKind\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_SourceKind")
                        .Annotation("CP6:OriginalSql", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_DeviceEvent_SourceSequence", "\"SourceSequence\" IS NULL OR \"SourceSequence\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceEvent_SourceSequence")
                        .Annotation("CP6:OriginalSql", "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Space_DeviceMapping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DeviceKind = table.Column<short>(type: "smallint", nullable: false),
                    ElementLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementType = table.Column<string>(type: "bpchar", maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ValidatedModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidatedFloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_DeviceMapping", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceMapping_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceMapping_DeviceExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DeviceExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceMapping_DeviceExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceMapping_DeviceKind", "\"DeviceKind\" BETWEEN 0 AND 7")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceMapping_DeviceKind")
                        .Annotation("CP6:OriginalSql", "[DeviceKind] BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_Space_DeviceMapping_ElementType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ElementType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceMapping_ElementType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceMapping_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceMapping_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_DeviceMapping_SourceId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceMapping_SourceId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceMapping_SourceKind", "\"SourceKind\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceMapping_SourceKind")
                        .Annotation("CP6:OriginalSql", "[SourceKind] IN (0, 1)");
                });

            migrationBuilder.CreateTable(
                name: "Space_DeviceState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DeviceMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    XMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    YMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    ZMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    AccuracyMillimeters = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    PositionOccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PositionReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PositionSourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    PositionSourceEventId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    PositionEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperatingState = table.Column<short>(type: "smallint", nullable: false),
                    OperatingStateOccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperatingStateReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperatingStateSourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    OperatingStateSourceEventId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    OperatingStateEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_DeviceState", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceState_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceState_Accuracy", "\"AccuracyMillimeters\" IS NULL OR (\"AccuracyMillimeters\" >= 0 AND \"XMillimeters\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_Accuracy")
                        .Annotation("CP6:OriginalSql", "[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceState_CoordinateTriple", "(\"XMillimeters\" IS NULL AND \"YMillimeters\" IS NULL AND \"ZMillimeters\" IS NULL) OR (\"XMillimeters\" IS NOT NULL AND \"YMillimeters\" IS NOT NULL AND \"ZMillimeters\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_CoordinateTriple")
                        .Annotation("CP6:OriginalSql", "([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceState_DeviceExternalId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DeviceExternalId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_DeviceExternalId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceState_OperatingState", "\"OperatingState\" BETWEEN 0 AND 6")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_OperatingState")
                        .Annotation("CP6:OriginalSql", "[OperatingState] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_Space_DeviceState_OperatingStateSourceEventId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"OperatingStateSourceEventId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_OperatingStateSourceEventId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceState_PositionSourceEventId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"PositionSourceEventId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_PositionSourceEventId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceState_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_DeviceState_SourceId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_SourceId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_DeviceState_SourceKind", "\"SourceKind\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_DeviceState_SourceKind")
                        .Annotation("CP6:OriginalSql", "[SourceKind] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_Space_DeviceState_Mapping_Tenant",
                        columns: x => new { x.TenantId, x.DeviceMappingId },
                        principalTable: "Space_DeviceMapping",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_DispatchRecommendation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Outcome = table.Column<string>(type: "bpchar", unicode: false, maxLength: 30, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ExaminedTaskCount = table.Column<int>(type: "integer", nullable: false),
                    EligibleTaskCount = table.Column<int>(type: "integer", nullable: false),
                    ExaminedPersonCount = table.Column<int>(type: "integer", nullable: false),
                    EligiblePersonCount = table.Column<int>(type: "integer", nullable: false),
                    EligiblePairCount = table.Column<int>(type: "integer", nullable: false),
                    MatchableAssignmentCount = table.Column<int>(type: "integer", nullable: false),
                    ReturnedAssignmentCount = table.Column<int>(type: "integer", nullable: false),
                    IsTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    ExclusionSamplesTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    RequestJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourcesJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ExclusionsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ExclusionSamplesJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AssignmentsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    LimitationsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_DispatchRecommendation", x => x.Id);
                    table.UniqueConstraint("AK_Space_DispatchRecommendation_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DispatchRecommendation_Counts", "\"ExaminedTaskCount\" >= 0 AND \"EligibleTaskCount\" >= 0 AND \"ExaminedPersonCount\" >= 0 AND \"EligiblePersonCount\" >= 0 AND \"EligiblePairCount\" >= 0 AND \"MatchableAssignmentCount\" >= 0 AND \"ReturnedAssignmentCount\" >= 0 AND \"EligibleTaskCount\" <= \"ExaminedTaskCount\" AND \"EligiblePersonCount\" <= \"ExaminedPersonCount\" AND \"MatchableAssignmentCount\" <= \"EligibleTaskCount\" AND \"MatchableAssignmentCount\" <= \"EligiblePersonCount\" AND \"MatchableAssignmentCount\" <= \"EligiblePairCount\" AND \"ReturnedAssignmentCount\" <= \"MatchableAssignmentCount\" AND ((\"IsTruncated\" = TRUE AND \"ReturnedAssignmentCount\" < \"MatchableAssignmentCount\") OR (\"IsTruncated\" = FALSE AND \"ReturnedAssignmentCount\" = \"MatchableAssignmentCount\"))")
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_Counts")
                        .Annotation("CP6:OriginalSql", "[ExaminedTaskCount] >= 0 AND [EligibleTaskCount] >= 0 AND [ExaminedPersonCount] >= 0 AND [EligiblePersonCount] >= 0 AND [EligiblePairCount] >= 0 AND [MatchableAssignmentCount] >= 0 AND [ReturnedAssignmentCount] >= 0 AND [EligibleTaskCount] <= [ExaminedTaskCount] AND [EligiblePersonCount] <= [ExaminedPersonCount] AND [MatchableAssignmentCount] <= [EligibleTaskCount] AND [MatchableAssignmentCount] <= [EligiblePersonCount] AND [MatchableAssignmentCount] <= [EligiblePairCount] AND [ReturnedAssignmentCount] <= [MatchableAssignmentCount] AND (([IsTruncated] = 1 AND [ReturnedAssignmentCount] < [MatchableAssignmentCount]) OR ([IsTruncated] = 0 AND [ReturnedAssignmentCount] = [MatchableAssignmentCount]))");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_DefinitionVersion_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionVersion\"), 'UTF8')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_DefinitionVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_Evidence", "\"Outcome\" IN ('NoAssignment', 'AssignmentsGenerated') AND (CASE WHEN \"RequestJson\" IS NULL THEN NULL ELSE (\"RequestJson\" IS JSON OBJECT OR \"RequestJson\" IS JSON ARRAY) END) AND (CASE WHEN \"SourcesJson\" IS NULL THEN NULL ELSE (\"SourcesJson\" IS JSON OBJECT OR \"SourcesJson\" IS JSON ARRAY) END) AND (CASE WHEN \"ExclusionsJson\" IS NULL THEN NULL ELSE (\"ExclusionsJson\" IS JSON OBJECT OR \"ExclusionsJson\" IS JSON ARRAY) END) AND (CASE WHEN \"ExclusionSamplesJson\" IS NULL THEN NULL ELSE (\"ExclusionSamplesJson\" IS JSON OBJECT OR \"ExclusionSamplesJson\" IS JSON ARRAY) END) AND (CASE WHEN \"AssignmentsJson\" IS NULL THEN NULL ELSE (\"AssignmentsJson\" IS JSON OBJECT OR \"AssignmentsJson\" IS JSON ARRAY) END) AND (CASE WHEN \"LimitationsJson\" IS NULL THEN NULL ELSE (\"LimitationsJson\" IS JSON OBJECT OR \"LimitationsJson\" IS JSON ARRAY) END)")
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_Evidence")
                        .Annotation("CP6:OriginalSql", "[Outcome] IN ('NoAssignment', 'AssignmentsGenerated') AND ISJSON([RequestJson]) = 1 AND ISJSON([SourcesJson]) = 1 AND ISJSON([ExclusionsJson]) = 1 AND ISJSON([ExclusionSamplesJson]) = 1 AND ISJSON([AssignmentsJson]) = 1 AND ISJSON([LimitationsJson]) = 1");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_Immutable", "char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), 'hex') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_Immutable")
                        .Annotation("CP6:OriginalSql", "LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_Outcome_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Outcome\"), 'UTF8')) <= 30")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_Outcome_Cp936Capacity");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_WarehouseCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"WarehouseCode\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_DispatchRecommendation_WarehouseCode_Cp936Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_EditLease",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HolderDisplayName = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ClientInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRenewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_EditLease", x => x.Id);
                    table.CheckConstraint("CK_Space_EditLease_HolderDisplayName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"HolderDisplayName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_EditLease_HolderDisplayName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_EditLease_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_EditLease_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Space_EditLeaseTakeoverAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousLeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousOwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewLeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TakenOverByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "bpchar", maxLength: 500, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestSource = table.Column<string>(type: "bpchar", maxLength: 500, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TakenOverAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_EditLeaseTakeoverAudit", x => x.Id);
                    table.CheckConstraint("CK_Space_EditLeaseTakeoverAudit_Reason_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Reason\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_EditLeaseTakeoverAudit_Reason_Utf16Capacity");
                    table.CheckConstraint("CK_Space_EditLeaseTakeoverAudit_RequestSource_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RequestSource\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_EditLeaseTakeoverAudit_RequestSource_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_ElementAttribute",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Namespace = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Key = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ValueType = table.Column<string>(type: "bpchar", maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Value = table.Column<string>(type: "bpchar", maxLength: 8000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Unit = table.Column<string>(type: "bpchar", maxLength: 50, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementAttribute", x => x.Id);
                    table.CheckConstraint("CK_Space_ElementAttribute_Key_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Key\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementAttribute_Key_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ElementAttribute_Namespace_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Namespace\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementAttribute_Namespace_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ElementAttribute_Unit_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Unit\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementAttribute_Unit_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ElementAttribute_Value_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Value\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 8000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementAttribute_Value_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ElementAttribute_ValueType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ValueType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementAttribute_ValueType_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_ElementCommandBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedFloorRevision = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedContentRevision = table.Column<long>(type: "bigint", nullable: true),
                    ExpectedContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ChangesetSha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ResultFloorRevision = table.Column<long>(type: "bigint", nullable: true),
                    ResultVersionContentRevision = table.Column<long>(type: "bigint", nullable: true),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ResponseJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementCommandBatch", x => x.Id);
                    table.UniqueConstraint("AK_Space_ElementCommandBatch_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ElementCommandBatch_ChangesetSha256_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ChangesetSha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ChangesetSha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandBatch_ChangesetSha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_ElementCommandBatch_ChangesetSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ChangesetSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandBatch_ChangesetSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ElementCommandBatch_ExpectedContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ExpectedContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ExpectedContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandBatch_ExpectedContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ElementCommandBatch_ExpectedContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ExpectedContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandBatch_ExpectedContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ElementCommandBatch_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandBatch_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ElementCommandBatch_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandBatch_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ElementCommandBatch_Result", "(\"ResultFloorRevision\" IS NULL AND \"ResultVersionContentRevision\" IS NULL AND \"ResponseJson\" IS NULL) OR (\"ResultFloorRevision\" IS NOT NULL AND \"ResultVersionContentRevision\" IS NOT NULL AND \"ResponseJson\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandBatch_Result")
                        .Annotation("CP6:OriginalSql", "([ResultFloorRevision] IS NULL AND [ResultVersionContentRevision] IS NULL AND [ResponseJson] IS NULL) OR ([ResultFloorRevision] IS NOT NULL AND [ResultVersionContentRevision] IS NOT NULL AND [ResponseJson] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "Space_ElementCommandRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    CommandType = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TargetLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BeforeJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AfterJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementCommandRecord", x => x.Id);
                    table.CheckConstraint("CK_Space_ElementCommandRecord_CommandType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"CommandType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementCommandRecord_CommandType_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ElementCommandRecord_Batch_Tenant",
                        columns: x => new { x.TenantId, x.CommandBatchId },
                        principalTable: "Space_ElementCommandBatch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ElementRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ElementType = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    GeometryJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ModelAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelAssetScope = table.Column<short>(type: "smallint", nullable: true),
                    ModelAssetOwnerTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    X = table.Column<int>(type: "integer", nullable: false),
                    Y = table.Column<int>(type: "integer", nullable: false),
                    Z = table.Column<int>(type: "integer", nullable: false),
                    RotationZ = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    BusinessCode = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LinkedEntityType = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LinkedLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsManualCorrectionLocked = table.Column<bool>(type: "boolean", nullable: false),
                    UserCorrectionVersion = table.Column<long>(type: "bigint", nullable: false),
                    ManualCorrectionUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ManualCorrectionUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_ElementRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_ElementRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_ElementRevision_BusinessCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"BusinessCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_BusinessCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ElementRevision_ElementType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ElementType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_ElementType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ElementRevision_Geometry", "\"RotationZ\" >= 0 AND \"RotationZ\" < 360 AND \"Width\" >= 0 AND \"Height\" >= 0 AND \"Depth\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_Geometry")
                        .Annotation("CP6:OriginalSql", "[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Height] >= 0 AND [Depth] >= 0");
                    table.CheckConstraint("CK_Space_ElementRevision_LinkedEntityType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LinkedEntityType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_LinkedEntityType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ElementRevision_ManualCorrection", "\"UserCorrectionVersion\" >= 0 AND (\"IsManualCorrectionLocked\" = FALSE OR (\"SourceId\" IS NOT NULL AND \"SourceRef\" IS NOT NULL AND \"UserCorrectionVersion\" > 0 AND \"ManualCorrectionUpdatedBy\" IS NOT NULL AND \"ManualCorrectionUpdatedAtUtc\" IS NOT NULL))")
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_ManualCorrection")
                        .Annotation("CP6:OriginalSql", "[UserCorrectionVersion] >= 0 AND ([IsManualCorrectionLocked] = 0 OR ([SourceId] IS NOT NULL AND [SourceRef] IS NOT NULL AND [UserCorrectionVersion] > 0 AND [ManualCorrectionUpdatedBy] IS NOT NULL AND [ManualCorrectionUpdatedAtUtc] IS NOT NULL))");
                    table.CheckConstraint("CK_Space_ElementRevision_ModelAssetScope", "(\"ModelAssetId\" IS NULL AND \"ModelAssetScope\" IS NULL AND \"ModelAssetOwnerTenantId\" IS NULL) OR (\"ModelAssetId\" IS NOT NULL AND \"ModelAssetScope\" IS NOT NULL AND \"ModelAssetOwnerTenantId\" IS NOT NULL AND ((\"ModelAssetScope\" = 0 AND \"ModelAssetOwnerTenantId\" = '00000000-0000-0000-0000-000000000000') OR (\"ModelAssetScope\" = 1 AND \"ModelAssetOwnerTenantId\" = \"TenantId\")))")
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_ModelAssetScope")
                        .Annotation("CP6:OriginalSql", "([ModelAssetId] IS NULL AND [ModelAssetScope] IS NULL AND [ModelAssetOwnerTenantId] IS NULL) OR ([ModelAssetId] IS NOT NULL AND [ModelAssetScope] IS NOT NULL AND [ModelAssetOwnerTenantId] IS NOT NULL AND (([ModelAssetScope] = 0 AND [ModelAssetOwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([ModelAssetScope] = 1 AND [ModelAssetOwnerTenantId] = [TenantId])))");
                    table.CheckConstraint("CK_Space_ElementRevision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ElementRevision_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ElementRevision_SourceRef_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ElementRevision_AssetVersion_Scope_Owner_Version",
                        columns: x => new { x.ModelAssetScope, x.ModelAssetOwnerTenantId, x.ModelAssetId },
                        principalTable: "Space_AssetVersion",
                        principalColumns: new[] { "Scope", "OwnerTenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ElementRevision_Parent_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.ParentLogicalId },
                        principalTable: "Space_ElementRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_FloorRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    FloorCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Elevation = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    BoundaryJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CoordinateSystem = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    UnderlaySourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnderlayCalibrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnderlayScale = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    UnderlayOffsetX = table.Column<int>(type: "integer", nullable: false),
                    UnderlayOffsetY = table.Column<int>(type: "integer", nullable: false),
                    UnderlayRotationZ = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_FloorRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_FloorRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_FloorRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_FloorRevision_CoordinateSystem_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"CoordinateSystem\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FloorRevision_CoordinateSystem_Utf16Capacity");
                    table.CheckConstraint("CK_Space_FloorRevision_FloorCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FloorCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FloorRevision_FloorCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_FloorRevision_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FloorRevision_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_FloorRevision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FloorRevision_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_FloorRevision_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_FloorRevision_SourceRef_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_GenerationLockedFact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasedOnRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKey = table.Column<string>(type: "bpchar", maxLength: 256, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ProposalType = table.Column<string>(type: "bpchar", maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    FieldPath = table.Column<string>(type: "bpchar", maxLength: 256, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ValueJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    MatchMethod = table.Column<short>(type: "smallint", nullable: false),
                    MatchScore = table.Column<decimal>(type: "numeric(6,5)", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_GenerationLockedFact", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationLockedFact_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationLockedFact_FieldPath_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FieldPath\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 256")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationLockedFact_FieldPath_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationLockedFact_Match", "\"MatchScore\" >= 0 AND \"MatchScore\" <= 1 AND \"RunId\" <> \"BasedOnRunId\"")
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationLockedFact_Match")
                        .Annotation("CP6:OriginalSql", "[MatchScore] >= 0 AND [MatchScore] <= 1 AND [RunId] <> [BasedOnRunId]");
                    table.CheckConstraint("CK_Space_GenerationLockedFact_ProposalType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProposalType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationLockedFact_ProposalType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationLockedFact_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationLockedFact_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_GenerationLockedFact_SourceHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationLockedFact_SourceHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationLockedFact_SourceHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationLockedFact_SourceHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_GenerationLockedFact_SourceKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 256")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationLockedFact_SourceKey_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_GenerationProposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    SourceHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceKey = table.Column<string>(type: "bpchar", maxLength: 256, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ProposalType = table.Column<string>(type: "bpchar", maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SuggestedGeometryJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SuggestedAttributesJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SuggestedRelationsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceRefsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    EvidenceJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    FieldProvenanceJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(6,5)", nullable: false),
                    ConfidenceBand = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    HasBlockingIssue = table.Column<bool>(type: "boolean", nullable: false),
                    HumanPatchJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LockedFieldsJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    AppliedLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadPurgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_GenerationProposal", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationProposal_Tenant_Run_Id", x => new { x.TenantId, x.RunId, x.Id });
                    table.UniqueConstraint("AK_Space_GenerationProposal_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationProposal_Confidence", "\"ConfidenceScore\" >= 0 AND \"ConfidenceScore\" <= 1")
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationProposal_Confidence")
                        .Annotation("CP6:OriginalSql", "[ConfidenceScore] >= 0 AND [ConfidenceScore] <= 1");
                    table.CheckConstraint("CK_Space_GenerationProposal_ProposalType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProposalType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationProposal_ProposalType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationProposal_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationProposal_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_GenerationProposal_SourceHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationProposal_SourceHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationProposal_SourceHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationProposal_SourceHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_GenerationProposal_SourceKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 256")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationProposal_SourceKey_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_GenerationRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BaseContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BusinessKeyHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BasedOnRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    MappingProfileVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RackGenerationProfileVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleVersion = table.Column<string>(type: "bpchar", maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PolicySnapshot = table.Column<short>(type: "smallint", nullable: false),
                    ProviderConfigVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderCode = table.Column<string>(type: "bpchar", maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ProviderModel = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    InputSchemaVersion = table.Column<string>(type: "bpchar", maxLength: 32, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    OutputSchemaVersion = table.Column<string>(type: "bpchar", maxLength: 32, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetFloorLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureCode = table.Column<string>(type: "bpchar", maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    FailureSummary = table.Column<string>(type: "bpchar", maxLength: 1024, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    DegradedReason = table.Column<string>(type: "bpchar", maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    CancelRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelPending = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedContentRevision = table.Column<long>(type: "bigint", nullable: true),
                    ApplyJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplyCommandBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplyReviewEtag = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ApplyExpectedRunRowVersion = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ApplyPlanHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ApplyPreparedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedCountsJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RetentionHoldUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PayloadPurgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_GenerationRun", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationRun_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationRun_ApplyExpectedRunRowVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ApplyExpectedRunRowVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_ApplyExpectedRunRowVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_ApplyPlanHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ApplyPlanHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ApplyPlanHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_ApplyPlanHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationRun_ApplyPlanHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ApplyPlanHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_ApplyPlanHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_ApplyReviewEtag_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ApplyReviewEtag\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ApplyReviewEtag\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_ApplyReviewEtag_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationRun_ApplyReviewEtag_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ApplyReviewEtag\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_ApplyReviewEtag_Cp936Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_BusinessKeyHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"BusinessKeyHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"BusinessKeyHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_BusinessKeyHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationRun_BusinessKeyHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"BusinessKeyHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_BusinessKeyHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_DegradedReason_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DegradedReason\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_DegradedReason_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_FailureCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FailureCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_FailureCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_FailureSummary_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FailureSummary\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1024")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_FailureSummary_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_IdempotencyKeyHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"IdempotencyKeyHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"IdempotencyKeyHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_IdempotencyKeyHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationRun_IdempotencyKeyHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"IdempotencyKeyHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_IdempotencyKeyHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_InputSchemaVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"InputSchemaVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 32")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_InputSchemaVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_OutputSchemaVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"OutputSchemaVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 32")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_OutputSchemaVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_Progress", "\"Progress\" >= 0 AND \"Progress\" <= 100")
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_Progress")
                        .Annotation("CP6:OriginalSql", "[Progress] >= 0 AND [Progress] <= 100");
                    table.CheckConstraint("CK_Space_GenerationRun_ProviderCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProviderCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_ProviderCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_ProviderModel_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ProviderModel\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_ProviderModel_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_GenerationRun_RuleVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RuleVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_RuleVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationRun_SourceHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_SourceHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationRun_SourceHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SourceHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationRun_SourceHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_ApplyJob_Tenant",
                        columns: x => new { x.TenantId, x.ApplyJobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_BasedOn_Tenant",
                        columns: x => new { x.TenantId, x.BasedOnRunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_TargetFloor_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.TargetFloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_GenerationStagingElement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementType = table.Column<string>(type: "bpchar", maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    NormalizedPayloadJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ValidationStatus = table.Column<short>(type: "smallint", nullable: false),
                    ValidationHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_GenerationStagingElement", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationStagingElement_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationStagingElement_ElementType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ElementType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationStagingElement_ElementType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_GenerationStagingElement_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationStagingElement_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_GenerationStagingElement_Validation", "(\"ValidationStatus\" = 0 AND \"ValidationHash\" IS NULL) OR (\"ValidationStatus\" = 1 AND \"ValidationHash\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationStagingElement_Validation")
                        .Annotation("CP6:OriginalSql", "([ValidationStatus] = 0 AND [ValidationHash] IS NULL) OR ([ValidationStatus] = 1 AND [ValidationHash] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_GenerationStagingElement_ValidationHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ValidationHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ValidationHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationStagingElement_ValidationHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_GenerationStagingElement_ValidationHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ValidationHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_GenerationStagingElement_ValidationHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_GenerationStaging_Floor_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationStaging_Proposal_Tenant_Run",
                        columns: x => new { x.TenantId, x.RunId, x.ProposalId },
                        principalTable: "Space_GenerationProposal",
                        principalColumns: new[] { "TenantId", "RunId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationStaging_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ProposalDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionType = table.Column<short>(type: "smallint", nullable: false),
                    BeforeJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AfterJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LockedFieldsJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ReasonCode = table.Column<string>(type: "bpchar", maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Comment = table.Column<string>(type: "bpchar", maxLength: 512, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    DecisionBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ProposalDecision", x => x.Id);
                    table.UniqueConstraint("AK_Space_ProposalDecision_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ProposalDecision_Comment_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Comment\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 512")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ProposalDecision_Comment_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ProposalDecision_ReasonCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ReasonCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ProposalDecision_ReasonCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ProposalDecision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ProposalDecision_RowVersion_TokenLength");
                    table.ForeignKey(
                        name: "FK_Space_ProposalDecision_Proposal_Tenant_Run",
                        columns: x => new { x.TenantId, x.RunId, x.ProposalId },
                        principalTable: "Space_GenerationProposal",
                        principalColumns: new[] { "TenantId", "RunId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ProposalDecision_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_TenantAiWorkSlot",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotNo = table.Column<int>(type: "integer", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseOwner = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_TenantAiWorkSlot", x => new { x.TenantId, x.SlotNo });
                    table.CheckConstraint("CK_Space_TenantAiWorkSlot_Lease", "(\"RunId\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL) OR (\"RunId\" IS NOT NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_TenantAiWorkSlot_Lease")
                        .Annotation("CP6:OriginalSql", "([RunId] IS NULL AND [LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL) OR ([RunId] IS NOT NULL AND [LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_TenantAiWorkSlot_LeaseOwner_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LeaseOwner\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_TenantAiWorkSlot_LeaseOwner_Utf16Capacity");
                    table.CheckConstraint("CK_Space_TenantAiWorkSlot_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_TenantAiWorkSlot_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_TenantAiWorkSlot_SlotNo", "\"SlotNo\" >= 1 AND \"SlotNo\" <= 3")
                        .Annotation("CP6:OriginalName", "CK_Space_TenantAiWorkSlot_SlotNo")
                        .Annotation("CP6:OriginalSql", "[SlotNo] >= 1 AND [SlotNo] <= 3");
                    table.ForeignKey(
                        name: "FK_Space_TenantAiWorkSlot_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_HistoricalRepublish",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    HistoricalVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedPublishedVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessIdempotencyKey = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Reason = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ApprovalReference = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_HistoricalRepublish", x => x.Id);
                    table.UniqueConstraint("AK_Space_HistoricalRepublish_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_HistoricalRepublish_ApprovalReference_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ApprovalReference\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_HistoricalRepublish_ApprovalReference_Utf16Capacity");
                    table.CheckConstraint("CK_Space_HistoricalRepublish_BusinessIdempotencyKe_81520a5219c7", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"BusinessIdempotencyKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_HistoricalRepublish_BusinessIdempotencyKey_Utf16Capacity");
                    table.CheckConstraint("CK_Space_HistoricalRepublish_Reason_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Reason\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_HistoricalRepublish_Reason_Utf16Capacity");
                    table.CheckConstraint("CK_Space_HistoricalRepublish_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_HistoricalRepublish_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_HistoricalRepublish_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_HistoricalRepublish_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_HistoricalRepublish_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_HistoricalRepublish_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_HistoricalRepublish_Status", "\"Status\" IN (0, 1, 2, 3, 4)")
                        .Annotation("CP6:OriginalName", "CK_Space_HistoricalRepublish_Status")
                        .Annotation("CP6:OriginalSql", "[Status] IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_LocationExternalBinding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdapterId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    WarehouseCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ExternalLocationId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BindingMode = table.Column<short>(type: "smallint", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_LocationExternalBinding", x => x.Id);
                    table.UniqueConstraint("AK_Space_LocationExternalBinding_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.CheckConstraint("CK_Space_LocationExternalBinding_AdapterId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AdapterId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationExternalBinding_AdapterId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_LocationExternalBinding_ExternalLocationI_d0cc9db152cd", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ExternalLocationId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationExternalBinding_ExternalLocationId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_LocationExternalBinding_Mode", "\"BindingMode\" IN (0, 1)")
                        .Annotation("CP6:OriginalName", "CK_Space_LocationExternalBinding_Mode")
                        .Annotation("CP6:OriginalSql", "[BindingMode] IN (0, 1)");
                    table.CheckConstraint("CK_Space_LocationExternalBinding_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationExternalBinding_SourceRef_Utf16Capacity");
                    table.CheckConstraint("CK_Space_LocationExternalBinding_WarehouseCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"WarehouseCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationExternalBinding_WarehouseCode_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Space_LocationRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RackLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationCode = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ColumnNo = table.Column<int>(type: "integer", nullable: false),
                    LevelNo = table.Column<int>(type: "integer", nullable: false),
                    DepthNo = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    MaxLoad = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    LocationType = table.Column<string>(type: "bpchar", maxLength: 20, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    CodeOrigin = table.Column<short>(type: "smallint", nullable: false),
                    ExternalBindingState = table.Column<short>(type: "smallint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_LocationRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_LocationRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_LocationRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_LocationRevision_Dimensions", "\"ColumnNo\" > 0 AND \"LevelNo\" > 0 AND \"DepthNo\" > 0 AND \"Width\" > 0 AND \"Height\" > 0 AND \"Depth\" > 0 AND (\"MaxLoad\" IS NULL OR \"MaxLoad\" >= 0)")
                        .Annotation("CP6:OriginalName", "CK_Space_LocationRevision_Dimensions")
                        .Annotation("CP6:OriginalSql", "[ColumnNo] > 0 AND [LevelNo] > 0 AND [DepthNo] > 0 AND [Width] > 0 AND [Height] > 0 AND [Depth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)");
                    table.CheckConstraint("CK_Space_LocationRevision_LocationCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LocationCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationRevision_LocationCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_LocationRevision_LocationType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LocationType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 20")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationRevision_LocationType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_LocationRevision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationRevision_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_LocationRevision_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_LocationRevision_SourceRef_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_LocationRevision_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_Model",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<short>(type: "smallint", nullable: false),
                    CutoverState = table.Column<short>(type: "smallint", nullable: false),
                    CutoverOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveDraftVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentPublishedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastMaterializedHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Model", x => x.Id);
                    table.UniqueConstraint("AK_Space_Model_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_Model_LastMaterializedHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"LastMaterializedHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"LastMaterializedHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Model_LastMaterializedHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_Model_LastMaterializedHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"LastMaterializedHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Model_LastMaterializedHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_Model_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_Model_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Space_ModelVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Purpose = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    BasedOnVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationSource = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    SourceTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTemplateVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTemplateContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    CloneOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RuleSetVersion = table.Column<string>(type: "bpchar", maxLength: 50, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ValidatedHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    WmsCapabilityHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ModelVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_ModelVersion_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.UniqueConstraint("AK_Space_ModelVersion_TenantId_ModelId_Id", x => new { x.TenantId, x.ModelId, x.Id });
                    table.CheckConstraint("CK_Space_ModelVersion_ContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_ContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ModelVersion_ContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_ContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ModelVersion_CreationSource", "\"CreationSource\" IN (0, 1, 2, 3) AND ((((\"CreationSource\" = 0 AND \"BasedOnVersionId\" IS NULL) OR (\"CreationSource\" = 1 AND \"BasedOnVersionId\" IS NOT NULL)) AND \"SourceTemplateId\" IS NULL AND \"SourceTemplateVersionId\" IS NULL AND \"SourceTemplateContentHash\" IS NULL) OR (\"CreationSource\" IN (2, 3) AND \"BasedOnVersionId\" IS NULL AND \"SourceTemplateId\" IS NOT NULL AND \"SourceTemplateVersionId\" IS NOT NULL AND \"SourceTemplateContentHash\" IS NOT NULL))")
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_CreationSource")
                        .Annotation("CP6:OriginalSql", "[CreationSource] IN (0, 1, 2, 3) AND (((([CreationSource] = 0 AND [BasedOnVersionId] IS NULL) OR ([CreationSource] = 1 AND [BasedOnVersionId] IS NOT NULL)) AND [SourceTemplateId] IS NULL AND [SourceTemplateVersionId] IS NULL AND [SourceTemplateContentHash] IS NULL) OR ([CreationSource] IN (2, 3) AND [BasedOnVersionId] IS NULL AND [SourceTemplateId] IS NOT NULL AND [SourceTemplateVersionId] IS NOT NULL AND [SourceTemplateContentHash] IS NOT NULL))");
                    table.CheckConstraint("CK_Space_ModelVersion_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelVersion_Purpose", "\"Purpose\" IN (0, 1) AND (\"Purpose\" = 0 OR (\"Status\" NOT IN (3, 4, 5, 6) AND \"PublishedAtUtc\" IS NULL AND \"PublishedBy\" IS NULL))")
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_Purpose")
                        .Annotation("CP6:OriginalSql", "[Purpose] IN (0, 1) AND ([Purpose] = 0 OR ([Status] NOT IN (3, 4, 5, 6) AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL))");
                    table.CheckConstraint("CK_Space_ModelVersion_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ModelVersion_RuleSetVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RuleSetVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_RuleSetVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelVersion_SourceTemplateContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"SourceTemplateContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SourceTemplateContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_SourceTemplateContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ModelVersion_SourceTemplateContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SourceTemplateContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_SourceTemplateContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ModelVersion_ValidatedHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ValidatedHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ValidatedHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_ValidatedHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ModelVersion_ValidatedHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ValidatedHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_ValidatedHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ModelVersion_WmsCapabilityHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"WmsCapabilityHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"WmsCapabilityHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_WmsCapabilityHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ModelVersion_WmsCapabilityHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"WmsCapabilityHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelVersion_WmsCapabilityHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ModelVersion_BasedOn_Tenant_Model_Version",
                        columns: x => new { x.TenantId, x.ModelId, x.BasedOnVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelVersion_Space_Model_Tenant_Model",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ModelSource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<short>(type: "smallint", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "bpchar", maxLength: 260, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Sha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ParserVersion = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    MappingProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    MappingProfileVersion = table.Column<long>(type: "bigint", nullable: true),
                    Unit = table.Column<string>(type: "bpchar", maxLength: 50, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ScaleToMillimeters = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    TransformJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    ImportedCommandBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ModelSource", x => x.Id);
                    table.UniqueConstraint("AK_Space_ModelSource_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.CheckConstraint("CK_Space_ModelSource_DisplayName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DisplayName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 260")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelSource_DisplayName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelSource_ParserVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ParserVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelSource_ParserVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelSource_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelSource_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ModelSource_Sha256_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"Sha256\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"Sha256\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelSource_Sha256_AsciiDomain");
                    table.CheckConstraint("CK_Space_ModelSource_Sha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Sha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelSource_Sha256_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ModelSource_Unit_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Unit\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelSource_Unit_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ModelSource_File_Tenant",
                        columns: x => new { x.TenantId, x.FileId },
                        principalTable: "Space_File",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelSource_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningScenarioBranch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasePublishedVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CloneJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefinitionVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningScenarioBranch", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningScenarioBranch_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PlanningScenarioBranch_DefinitionVersion_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningScenarioBranch_DefinitionVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningScenarioBranch_Immutable", "\"BasePublishedVersionId\" <> \"ScenarioVersionId\" AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), 'hex') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningScenarioBranch_Immutable")
                        .Annotation("CP6:OriginalSql", "[BasePublishedVersionId] <> [ScenarioVersionId] AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PlanningScenarioBranch_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningScenarioBranch_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PlanningScenarioBranch_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningScenarioBranch_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningScenarioBranch_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningScenarioBranch_RequestHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_BaseVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.BasePublishedVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_CloneJob_Tenant",
                        columns: x => new { x.TenantId, x.CloneJobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_ScenarioVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PutawayRecommendation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Outcome = table.Column<string>(type: "bpchar", unicode: false, maxLength: 30, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ExaminedLocationCount = table.Column<int>(type: "integer", nullable: false),
                    EligibleCandidateCount = table.Column<int>(type: "integer", nullable: false),
                    ReturnedCandidateCount = table.Column<int>(type: "integer", nullable: false),
                    IsTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    ExclusionSamplesTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    RequestJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourcesJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ExclusionsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ExclusionSamplesJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CandidatesJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    LimitationsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PutawayRecommendation", x => x.Id);
                    table.UniqueConstraint("AK_Space_PutawayRecommendation_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PutawayRecommendation_Counts", "\"ExaminedLocationCount\" >= 0 AND \"EligibleCandidateCount\" >= 0 AND \"ReturnedCandidateCount\" >= 0 AND \"EligibleCandidateCount\" <= \"ExaminedLocationCount\" AND \"ReturnedCandidateCount\" <= \"EligibleCandidateCount\" AND ((\"IsTruncated\" = TRUE AND \"ReturnedCandidateCount\" < \"EligibleCandidateCount\") OR (\"IsTruncated\" = FALSE AND \"ReturnedCandidateCount\" = \"EligibleCandidateCount\"))")
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_Counts")
                        .Annotation("CP6:OriginalSql", "[ExaminedLocationCount] >= 0 AND [EligibleCandidateCount] >= 0 AND [ReturnedCandidateCount] >= 0 AND [EligibleCandidateCount] <= [ExaminedLocationCount] AND [ReturnedCandidateCount] <= [EligibleCandidateCount] AND (([IsTruncated] = 1 AND [ReturnedCandidateCount] < [EligibleCandidateCount]) OR ([IsTruncated] = 0 AND [ReturnedCandidateCount] = [EligibleCandidateCount]))");
                    table.CheckConstraint("CK_Space_PutawayRecommendation_DefinitionVersion_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionVersion\"), 'UTF8')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_DefinitionVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PutawayRecommendation_Evidence", "\"Outcome\" IN ('NoCandidate', 'CandidatesGenerated') AND (CASE WHEN \"RequestJson\" IS NULL THEN NULL ELSE (\"RequestJson\" IS JSON OBJECT OR \"RequestJson\" IS JSON ARRAY) END) AND (CASE WHEN \"SourcesJson\" IS NULL THEN NULL ELSE (\"SourcesJson\" IS JSON OBJECT OR \"SourcesJson\" IS JSON ARRAY) END) AND (CASE WHEN \"ExclusionsJson\" IS NULL THEN NULL ELSE (\"ExclusionsJson\" IS JSON OBJECT OR \"ExclusionsJson\" IS JSON ARRAY) END) AND (CASE WHEN \"ExclusionSamplesJson\" IS NULL THEN NULL ELSE (\"ExclusionSamplesJson\" IS JSON OBJECT OR \"ExclusionSamplesJson\" IS JSON ARRAY) END) AND (CASE WHEN \"CandidatesJson\" IS NULL THEN NULL ELSE (\"CandidatesJson\" IS JSON OBJECT OR \"CandidatesJson\" IS JSON ARRAY) END) AND (CASE WHEN \"LimitationsJson\" IS NULL THEN NULL ELSE (\"LimitationsJson\" IS JSON OBJECT OR \"LimitationsJson\" IS JSON ARRAY) END)")
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_Evidence")
                        .Annotation("CP6:OriginalSql", "[Outcome] IN ('NoCandidate', 'CandidatesGenerated') AND ISJSON([RequestJson]) = 1 AND ISJSON([SourcesJson]) = 1 AND ISJSON([ExclusionsJson]) = 1 AND ISJSON([ExclusionSamplesJson]) = 1 AND ISJSON([CandidatesJson]) = 1 AND ISJSON([LimitationsJson]) = 1");
                    table.CheckConstraint("CK_Space_PutawayRecommendation_Immutable", "char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), 'hex') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_Immutable")
                        .Annotation("CP6:OriginalSql", "LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PutawayRecommendation_Outcome_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Outcome\"), 'UTF8')) <= 30")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_Outcome_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PutawayRecommendation_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PutawayRecommendation_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PutawayRecommendation_WarehouseCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"WarehouseCode\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PutawayRecommendation_WarehouseCode_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PutawayRecommendation_Version_Tenant",
                        columns: x => new { x.TenantId, x.PublishedVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ValidationRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RuleSetVersion = table.Column<string>(type: "bpchar", maxLength: 50, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AdapterId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CapabilityHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    BlockingCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    InfoCount = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FailureCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    FailureSummary = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ValidationRun", x => x.Id);
                    table.UniqueConstraint("AK_Space_ValidationRun_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ValidationRun_AdapterId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AdapterId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_AdapterId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ValidationRun_CapabilityHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"CapabilityHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"CapabilityHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_CapabilityHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ValidationRun_CapabilityHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"CapabilityHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_CapabilityHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ValidationRun_ContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_ContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ValidationRun_ContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_ContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ValidationRun_Counts", "\"BlockingCount\" >= 0 AND \"WarningCount\" >= 0 AND \"InfoCount\" >= 0 AND (\"Status\" <> 2 OR \"BlockingCount\" = 0) AND (\"Status\" <> 3 OR \"BlockingCount\" > 0)")
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_Counts")
                        .Annotation("CP6:OriginalSql", "[BlockingCount] >= 0 AND [WarningCount] >= 0 AND [InfoCount] >= 0 AND ([Status] <> 2 OR [BlockingCount] = 0) AND ([Status] <> 3 OR [BlockingCount] > 0)");
                    table.CheckConstraint("CK_Space_ValidationRun_FailureCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FailureCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_FailureCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ValidationRun_FailureSummary_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FailureSummary\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_FailureSummary_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ValidationRun_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ValidationRun_RuleSetVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RuleSetVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_RuleSetVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ValidationRun_StatusTime", "(\"Status\" = 0 AND \"StartedAtUtc\" IS NULL AND \"FinishedAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"StartedAtUtc\" IS NOT NULL AND \"FinishedAtUtc\" IS NULL) OR (\"Status\" IN (2, 3, 4) AND \"FinishedAtUtc\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_ValidationRun_StatusTime")
                        .Annotation("CP6:OriginalSql", "([Status] = 0 AND [StartedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([Status] IN (2, 3, 4) AND [FinishedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Space_ValidationRun_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ValidationRun_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_WmsAdoption",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdapterId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DataSource = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DataSourceKind = table.Column<string>(type: "bpchar", maxLength: 20, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    WmsLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalLocationId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    WmsLocationCode = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    WmsIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalVersion = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    WmsStateHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    LastObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoundLocationCode = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    BoundAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_WmsAdoption", x => x.Id);
                    table.UniqueConstraint("AK_Space_WmsAdoption_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_WmsAdoption_AdapterId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AdapterId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_AdapterId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsAdoption_BoundLocationCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"BoundLocationCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_BoundLocationCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsAdoption_DataSource_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DataSource\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_DataSource_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsAdoption_DataSourceKind_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DataSourceKind\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 20")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_DataSourceKind_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsAdoption_ExternalLocationId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ExternalLocationId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_ExternalLocationId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsAdoption_ExternalVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ExternalVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_ExternalVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsAdoption_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_WmsAdoption_WmsLocationCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"WmsLocationCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_WmsLocationCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsAdoption_WmsStateHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"WmsStateHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"WmsStateHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_WmsStateHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_WmsAdoption_WmsStateHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"WmsStateHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsAdoption_WmsStateHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_WmsAdoption_ModelVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_UnderlayCalibration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    PixelWidth = table.Column<int>(type: "integer", nullable: false),
                    PixelHeight = table.Column<int>(type: "integer", nullable: false),
                    Point1PixelX = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    Point1PixelY = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    Point1WorldX = table.Column<int>(type: "integer", nullable: false),
                    Point1WorldY = table.Column<int>(type: "integer", nullable: false),
                    Point2PixelX = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    Point2PixelY = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    Point2WorldX = table.Column<int>(type: "integer", nullable: false),
                    Point2WorldY = table.Column<int>(type: "integer", nullable: false),
                    ValidationPixelX = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ValidationPixelY = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ValidationWorldX = table.Column<int>(type: "integer", nullable: false),
                    ValidationWorldY = table.Column<int>(type: "integer", nullable: false),
                    MillimetersPerPixel = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    OffsetX = table.Column<int>(type: "integer", nullable: false),
                    OffsetY = table.Column<int>(type: "integer", nullable: false),
                    RotationZ = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    ValidationErrorMillimeters = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ErrorThresholdMillimeters = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_UnderlayCalibration", x => x.Id);
                    table.UniqueConstraint("AK_Space_UnderlayCalibration_Tenant_Version_Floor_Source_Id", x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId, x.SourceId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_UnderlayCalibration_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ZoneRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ZoneType = table.Column<short>(type: "smallint", nullable: false),
                    PolygonJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Color = table.Column<string>(type: "bpchar", maxLength: 50, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    CapabilityFlags = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ZoneRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_ZoneRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_ZoneRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_ZoneRevision_CapabilityFlags_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"CapabilityFlags\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ZoneRevision_CapabilityFlags_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ZoneRevision_Color_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Color\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ZoneRevision_Color_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ZoneRevision_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ZoneRevision_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ZoneRevision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ZoneRevision_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_ZoneRevision_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ZoneRevision_SourceRef_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ZoneRevision_ZoneCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ZoneCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ZoneRevision_ZoneCode_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ZoneRevision_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ZoneRevision_Space_ModelSource_TenantId_M_14c5cace7f7c",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ZoneRevision_Space_ModelVersion_TenantId__cb171225a3b2",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningHistoricalDataset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    HistoricalFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HistoricalToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplayStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplaySpeedFactor = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    TaskCount = table.Column<int>(type: "integer", nullable: false),
                    SourceDatasetHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefinitionVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DeidentificationVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningHistoricalDataset", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningHistoricalDataset_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.UniqueConstraint("AK_Space_PlanningHistoricalDataset_Tenant_Id_Branc_617ebfa47327", x => new { x.TenantId, x.Id, x.BranchId, x.ModelId, x.ScenarioVersionId });
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_DefinitionVersi_bc3ed22cf8f5", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_DefinitionVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_Deidentificatio_88452438acec", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DeidentificationVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_DeidentificationVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_Invariants", "\"HistoricalFromUtc\" < \"HistoricalToUtc\" AND \"ReplaySpeedFactor\" > 0 AND \"ReplaySpeedFactor\" <= 1000 AND \"TaskCount\" BETWEEN 1 AND 10000 AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), 'hex') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_Invariants")
                        .Annotation("CP6:OriginalSql", "[HistoricalFromUtc] < [HistoricalToUtc] AND [ReplaySpeedFactor] > 0 AND [ReplaySpeedFactor] <= 1000 AND [TaskCount] BETWEEN 1 AND 10000 AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_SourceDatasetHa_67dc3d8e17ca", "octet_length(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_SourceDatasetHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_SourceDatasetHa_a71d12775fb2", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalDataset_SourceDatasetHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalDataset_Branch_Tenant",
                        columns: x => new { x.TenantId, x.BranchId },
                        principalTable: "Space_PlanningScenarioBranch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalDataset_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalDataset_ScenarioVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ModelIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    GenerationRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    GenerationProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "bpchar", maxLength: 50, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    FieldPath = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    EvidenceJson = table.Column<string>(type: "bpchar", nullable: false, defaultValue: "{}", collation: "cp6_ci_as_provider_v1"),
                    Severity = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TargetLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageArgsJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SuggestedActionCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ResolutionCommandBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionKind = table.Column<short>(type: "smallint", nullable: false),
                    ResolutionDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgementReason = table.Column<string>(type: "bpchar", maxLength: 1000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    PayloadPurgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ModelIssue", x => x.Id);
                    table.CheckConstraint("CK_Space_ModelIssue_AcknowledgementReason_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AcknowledgementReason\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 1000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_AcknowledgementReason_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelIssue_Category_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Category\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 50")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_Category_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelIssue_Code_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Code\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_Code_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelIssue_Context", "\"ModelVersionId\" IS NOT NULL OR \"SourceId\" IS NOT NULL OR \"JobId\" IS NOT NULL")
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_Context")
                        .Annotation("CP6:OriginalSql", "[ModelVersionId] IS NOT NULL OR [SourceId] IS NOT NULL OR [JobId] IS NOT NULL");
                    table.CheckConstraint("CK_Space_ModelIssue_FieldPath_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"FieldPath\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_FieldPath_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelIssue_GenerationScope", "(\"GenerationProposalId\" IS NULL OR \"GenerationRunId\" IS NOT NULL) AND (\"ResolutionDecisionId\" IS NULL OR \"GenerationProposalId\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_GenerationScope")
                        .Annotation("CP6:OriginalSql", "([GenerationProposalId] IS NULL OR [GenerationRunId] IS NOT NULL) AND ([ResolutionDecisionId] IS NULL OR [GenerationProposalId] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_ModelIssue_Resolution", "(\"Status\" <> 1 AND \"ResolutionKind\" = 0 AND \"ResolutionCommandBatchId\" IS NULL AND \"ResolutionDecisionId\" IS NULL) OR (\"Status\" = 1 AND ((\"ResolutionKind\" = 1 AND \"ResolutionCommandBatchId\" IS NOT NULL AND \"ResolutionDecisionId\" IS NULL) OR (\"ResolutionKind\" IN (2, 3) AND \"ResolutionCommandBatchId\" IS NULL AND \"ResolutionDecisionId\" IS NOT NULL)))")
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_Resolution")
                        .Annotation("CP6:OriginalSql", "([Status] <> 1 AND [ResolutionKind] = 0 AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NULL) OR ([Status] = 1 AND (([ResolutionKind] = 1 AND [ResolutionCommandBatchId] IS NOT NULL AND [ResolutionDecisionId] IS NULL) OR ([ResolutionKind] IN (2, 3) AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NOT NULL)))");
                    table.CheckConstraint("CK_Space_ModelIssue_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_SourceRef_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelIssue_SourceVersion", "\"SourceId\" IS NULL OR \"ModelVersionId\" IS NOT NULL")
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_SourceVersion")
                        .Annotation("CP6:OriginalSql", "[SourceId] IS NULL OR [ModelVersionId] IS NOT NULL");
                    table.CheckConstraint("CK_Space_ModelIssue_SuggestedActionCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SuggestedActionCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_SuggestedActionCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ModelIssue_ValidationScope", "\"ValidationRunId\" IS NULL OR (\"ModelVersionId\" IS NOT NULL AND \"JobId\" IS NOT NULL)")
                        .Annotation("CP6:OriginalName", "CK_Space_ModelIssue_ValidationScope")
                        .Annotation("CP6:OriginalSql", "[ValidationRunId] IS NULL OR ([ModelVersionId] IS NOT NULL AND [JobId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_GenerationRun_Tenant",
                        columns: x => new { x.TenantId, x.GenerationRunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_Proposal_Tenant_Run",
                        columns: x => new { x.TenantId, x.GenerationRunId, x.GenerationProposalId },
                        principalTable: "Space_GenerationProposal",
                        principalColumns: new[] { "TenantId", "RunId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_ResolutionDecision_Tenant",
                        columns: x => new { x.TenantId, x.ResolutionDecisionId },
                        principalTable: "Space_ProposalDecision",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_ValidationRun_Tenant",
                        columns: x => new { x.TenantId, x.ValidationRunId },
                        principalTable: "Space_ValidationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PublishPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AdapterId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CapabilityHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PlanHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    PlanJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PublishPlan", x => x.Id);
                    table.UniqueConstraint("AK_Space_PublishPlan_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PublishPlan_AdapterId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AdapterId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishPlan_AdapterId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishPlan_CapabilityHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"CapabilityHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"CapabilityHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishPlan_CapabilityHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishPlan_CapabilityHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"CapabilityHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishPlan_CapabilityHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PublishPlan_ContentHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishPlan_ContentHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishPlan_ContentHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ContentHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishPlan_ContentHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PublishPlan_PlanHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"PlanHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"PlanHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishPlan_PlanHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishPlan_PlanHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PlanHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishPlan_PlanHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PublishPlan_TargetVersion_Tenant",
                        columns: x => new { x.TenantId, x.TargetVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PublishPlan_ValidationRun_Tenant",
                        columns: x => new { x.TenantId, x.ValidationRunId },
                        principalTable: "Space_ValidationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_RackRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    AisleLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    RackCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RackType = table.Column<string>(type: "bpchar", maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    X = table.Column<int>(type: "integer", nullable: false),
                    Y = table.Column<int>(type: "integer", nullable: false),
                    Z = table.Column<int>(type: "integer", nullable: false),
                    RotationZ = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_RackRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_RackRevision_Geometry", "\"RotationZ\" >= 0 AND \"RotationZ\" < 360 AND \"Width\" >= 0 AND \"Depth\" >= 0 AND \"Height\" >= 0")
                        .Annotation("CP6:OriginalName", "CK_Space_RackRevision_Geometry")
                        .Annotation("CP6:OriginalSql", "[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Depth] >= 0 AND [Height] >= 0");
                    table.CheckConstraint("CK_Space_RackRevision_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackRevision_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_RackRevision_RackCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RackCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackRevision_RackCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_RackRevision_RackType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RackType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackRevision_RackType_Utf16Capacity");
                    table.CheckConstraint("CK_Space_RackRevision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackRevision_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_RackRevision_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackRevision_SourceRef_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Aisle_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.AisleLogicalId },
                        principalTable: "Space_AisleRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Space_ModelSource_TenantId_M_6c5348cbb03b",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Space_ModelVersion_TenantId__81eb54cdb3a6",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Zone_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.ZoneLogicalId },
                        principalTable: "Space_ZoneRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningHistoricalTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    TaskToken = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    WorkerToken = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TaskType = table.Column<short>(type: "smallint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    OriginalCreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OriginalCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplayCreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplayCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromLocationLogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningHistoricalTask", x => x.Id);
                    table.CheckConstraint("CK_Space_PlanningHistoricalTask_Invariants", "\"SequenceNo\" > 0 AND \"Quantity\" > 0 AND \"OriginalCreatedAtUtc\" <= \"OriginalCompletedAtUtc\" AND \"ReplayCreatedAtUtc\" <= \"ReplayCompletedAtUtc\" AND \"ToLocationLogicalId\" <> '00000000-0000-0000-0000-000000000000' AND (\"FromLocationLogicalId\" IS NULL OR \"FromLocationLogicalId\" <> '00000000-0000-0000-0000-000000000000') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"TaskToken\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"TaskToken\"), 'UTF8'), 'hex') AND (\"WorkerToken\" IS NULL OR (char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"WorkerToken\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"WorkerToken\"), 'UTF8'), 'hex'))) AND \"TaskType\" BETWEEN 0 AND 4 AND \"Outcome\" BETWEEN 0 AND 2 AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalTask_Invariants")
                        .Annotation("CP6:OriginalSql", "[SequenceNo] > 0 AND [Quantity] > 0 AND [OriginalCreatedAtUtc] <= [OriginalCompletedAtUtc] AND [ReplayCreatedAtUtc] <= [ReplayCompletedAtUtc] AND [ToLocationLogicalId] <> '00000000-0000-0000-0000-000000000000' AND ([FromLocationLogicalId] IS NULL OR [FromLocationLogicalId] <> '00000000-0000-0000-0000-000000000000') AND LEN([TaskToken]) = 64 AND [TaskToken] NOT LIKE '%[^0-9a-f]%' AND ([WorkerToken] IS NULL OR (LEN([WorkerToken]) = 64 AND [WorkerToken] NOT LIKE '%[^0-9a-f]%')) AND [TaskType] BETWEEN 0 AND 4 AND [Outcome] BETWEEN 0 AND 2 AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PlanningHistoricalTask_TaskToken_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"TaskToken\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"TaskToken\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalTask_TaskToken_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningHistoricalTask_TaskToken_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"TaskToken\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalTask_TaskToken_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningHistoricalTask_WorkerToken_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"WorkerToken\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"WorkerToken\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalTask_WorkerToken_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningHistoricalTask_WorkerToken_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"WorkerToken\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningHistoricalTask_WorkerToken_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalTask_Dataset_Tenant",
                        columns: x => new { x.TenantId, x.DatasetId },
                        principalTable: "Space_PlanningHistoricalDataset",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningSimulationRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefinitionVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DatasetRequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ResultHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    GeometryBasis = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CurrencyCode = table.Column<string>(type: "character(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefaultQuantityCapacity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DefaultConcurrentTaskCapacity = table.Column<int>(type: "integer", nullable: false),
                    LocationCapacityOverrideCount = table.Column<int>(type: "integer", nullable: false),
                    ThroughputWindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    DistanceCostPerMeter = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    LaborCostPerHour = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    CongestionCostPerTaskHour = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    TaskCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedTaskCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedQuantity = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceEligibleTaskCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDistanceMeters = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceCoveragePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    PeakConcurrentTasks = table.Column<int>(type: "integer", nullable: false),
                    CongestionSeconds = table.Column<long>(type: "bigint", nullable: false),
                    CongestionTaskSeconds = table.Column<long>(type: "bigint", nullable: false),
                    OverloadedLocationCount = table.Column<int>(type: "integer", nullable: false),
                    PeakCapacityUtilizationPercent = table.Column<decimal>(type: "numeric(38,4)", precision: 38, scale: 4, nullable: false),
                    AverageCompletedTasksPerHour = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    PeakCompletedTasksPerHour = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    AverageCompletedQuantityPerHour = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    PeakCompletedQuantityPerHour = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    LaborHours = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceCost = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    LaborCost = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    CongestionCost = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningSimulationRun", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningSimulationRun_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.UniqueConstraint("AK_Space_PlanningSimulationRun_Tenant_Id_Branch_Version", x => new { x.TenantId, x.Id, x.BranchId, x.ScenarioVersionId });
                    table.UniqueConstraint("AK_Space_PlanningSimulationRun_Tenant_Id_Site", x => new { x.TenantId, x.Id, x.SiteId });
                    table.UniqueConstraint("AK_Space_PlanningSimulationRun_Tenant_Id_Version", x => new { x.TenantId, x.Id, x.ScenarioVersionId });
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_CurrencyCode_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_CurrencyCode_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_CurrencyCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8')) <= 3")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_CurrencyCode_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_DatasetRequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"DatasetRequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"DatasetRequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_DatasetRequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_DatasetRequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DatasetRequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_DatasetRequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_DefinitionVersion_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_DefinitionVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_GeometryBasis_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"GeometryBasis\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_GeometryBasis_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_Invariants", "\"ScenarioContentRevision\" >= 0 AND \"DefaultQuantityCapacity\" > 0 AND \"DefaultConcurrentTaskCapacity\" BETWEEN 1 AND 10000 AND \"LocationCapacityOverrideCount\" BETWEEN 0 AND 10000 AND \"ThroughputWindowMinutes\" BETWEEN 1 AND 1440 AND \"DistanceCostPerMeter\" >= 0 AND \"LaborCostPerHour\" >= 0 AND \"CongestionCostPerTaskHour\" >= 0 AND \"TaskCount\" BETWEEN 1 AND 10000 AND \"CompletedTaskCount\" BETWEEN 0 AND \"TaskCount\" AND \"CompletedQuantity\" >= 0 AND \"DistanceEligibleTaskCount\" BETWEEN 0 AND \"TaskCount\" AND \"TotalDistanceMeters\" >= 0 AND \"DistanceCoveragePercent\" BETWEEN 0 AND 100 AND \"PeakConcurrentTasks\" >= 0 AND \"CongestionSeconds\" >= 0 AND \"CongestionTaskSeconds\" >= 0 AND \"OverloadedLocationCount\" >= 0 AND \"PeakCapacityUtilizationPercent\" >= 0 AND \"AverageCompletedTasksPerHour\" >= 0 AND \"PeakCompletedTasksPerHour\" >= 0 AND \"AverageCompletedQuantityPerHour\" >= 0 AND \"PeakCompletedQuantityPerHour\" >= 0 AND \"LaborHours\" >= 0 AND \"DistanceCost\" >= 0 AND \"LaborCost\" >= 0 AND \"CongestionCost\" >= 0 AND \"TotalCost\" >= 0 AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"DatasetRequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"DatasetRequestHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"ResultHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"ResultHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 3 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8'), 'alpha') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_Invariants")
                        .Annotation("CP6:OriginalSql", "[ScenarioContentRevision] >= 0 AND [DefaultQuantityCapacity] > 0 AND [DefaultConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [LocationCapacityOverrideCount] BETWEEN 0 AND 10000 AND [ThroughputWindowMinutes] BETWEEN 1 AND 1440 AND [DistanceCostPerMeter] >= 0 AND [LaborCostPerHour] >= 0 AND [CongestionCostPerTaskHour] >= 0 AND [TaskCount] BETWEEN 1 AND 10000 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [CompletedQuantity] >= 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [PeakConcurrentTasks] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [AverageCompletedQuantityPerHour] >= 0 AND [PeakCompletedQuantityPerHour] >= 0 AND [LaborHours] >= 0 AND [DistanceCost] >= 0 AND [LaborCost] >= 0 AND [CongestionCost] >= 0 AND [TotalCost] >= 0 AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([DatasetRequestHash]) = 64 AND [DatasetRequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ResultHash]) = 64 AND [ResultHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_ResultHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ResultHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ResultHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_ResultHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_ResultHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ResultHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationRun_ResultHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_Branch_Tenant",
                        columns: x => new { x.TenantId, x.BranchId },
                        principalTable: "Space_PlanningScenarioBranch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_Dataset_Tenant",
                        columns: x => new { x.TenantId, x.DatasetId, x.BranchId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_PlanningHistoricalDataset",
                        principalColumns: new[] { "TenantId", "Id", "BranchId", "ModelId", "ScenarioVersionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_ScenarioVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PublishAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdapterId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CurrentStep = table.Column<short>(type: "smallint", nullable: false),
                    BusinessIdempotencyKey = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    OwnsPublishSlot = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalReference = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    WmsCommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RuntimeActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Summary = table.Column<string>(type: "bpchar", maxLength: 2000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    QueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ManualRetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastRetriedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRetriedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PublishAttempt", x => x.Id);
                    table.UniqueConstraint("AK_Space_PublishAttempt_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PublishAttempt_AdapterId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AdapterId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_AdapterId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishAttempt_ApprovalReference_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ApprovalReference\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_ApprovalReference_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishAttempt_BusinessIdempotencyKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"BusinessIdempotencyKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_BusinessIdempotencyKey_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishAttempt_LastErrorCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LastErrorCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_LastErrorCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishAttempt_Recovery", "\"ManualRetryCount\" >= 0 AND (CASE WHEN \"RequestJson\" IS NULL THEN NULL ELSE (\"RequestJson\" IS JSON OBJECT OR \"RequestJson\" IS JSON ARRAY) END) AND ((\"ManualRetryCount\" = 0 AND \"LastRetriedAtUtc\" IS NULL AND \"LastRetriedBy\" IS NULL) OR (\"ManualRetryCount\" > 0 AND \"LastRetriedAtUtc\" IS NOT NULL AND \"LastRetriedBy\" IS NOT NULL))")
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_Recovery")
                        .Annotation("CP6:OriginalSql", "[ManualRetryCount] >= 0 AND ISJSON([RequestJson]) = 1 AND (([ManualRetryCount] = 0 AND [LastRetriedAtUtc] IS NULL AND [LastRetriedBy] IS NULL) OR ([ManualRetryCount] > 0 AND [LastRetriedAtUtc] IS NOT NULL AND [LastRetriedBy] IS NOT NULL))");
                    table.CheckConstraint("CK_Space_PublishAttempt_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishAttempt_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PublishAttempt_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_PublishAttempt_Slot", "(\"OwnsPublishSlot\" = TRUE AND \"FinishedAtUtc\" IS NULL) OR (\"OwnsPublishSlot\" = FALSE)")
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_Slot")
                        .Annotation("CP6:OriginalSql", "([OwnsPublishSlot] = 1 AND [FinishedAtUtc] IS NULL) OR ([OwnsPublishSlot] = 0)");
                    table.CheckConstraint("CK_Space_PublishAttempt_Summary_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Summary\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 2000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAttempt_Summary_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PublishAttempt_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PublishAttempt_Plan_Tenant",
                        columns: x => new { x.TenantId, x.PublishPlanId },
                        principalTable: "Space_PublishPlan",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_RackLevelRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RackLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    LevelNo = table.Column<int>(type: "integer", nullable: false),
                    BottomZ = table.Column<int>(type: "integer", nullable: false),
                    ClearHeight = table.Column<int>(type: "integer", nullable: false),
                    BinCount = table.Column<int>(type: "integer", nullable: false),
                    DepthCount = table.Column<int>(type: "integer", nullable: false),
                    CellWidth = table.Column<int>(type: "integer", nullable: false),
                    CellDepth = table.Column<int>(type: "integer", nullable: false),
                    BeamHeight = table.Column<int>(type: "integer", nullable: false),
                    MaxLoad = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRef = table.Column<string>(type: "bpchar", maxLength: 500, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackLevelRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackLevelRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_RackLevelRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_RackLevelRevision_Dimensions", "\"LevelNo\" > 0 AND \"BottomZ\" >= 0 AND \"ClearHeight\" > 0 AND \"BinCount\" > 0 AND \"DepthCount\" > 0 AND \"CellWidth\" > 0 AND \"CellDepth\" > 0 AND \"BeamHeight\" >= 0 AND (\"MaxLoad\" IS NULL OR \"MaxLoad\" >= 0)")
                        .Annotation("CP6:OriginalName", "CK_Space_RackLevelRevision_Dimensions")
                        .Annotation("CP6:OriginalSql", "[LevelNo] > 0 AND [BottomZ] >= 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND [BeamHeight] >= 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)");
                    table.CheckConstraint("CK_Space_RackLevelRevision_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackLevelRevision_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Space_RackLevelRevision_SourceRef_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SourceRef\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 500")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_RackLevelRevision_SourceRef_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_RackLevelRevision_Rack_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.RackLogicalId },
                        principalTable: "Space_RackRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackLevelRevision_Space_ModelSource_Tenan_b1c9728d3007",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackLevelRevision_Space_ModelVersion_Tena_d10c2adb0afc",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningComparison",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasePublishedVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaselineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefinitionVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ComparisonHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SourceDatasetHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CurrencyCode = table.Column<string>(type: "character(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    HistoricalFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HistoricalToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RunCount = table.Column<int>(type: "integer", nullable: false),
                    MinimumDistanceCoveragePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    MaximumPeakCapacityUtilizationPercent = table.Column<decimal>(type: "numeric(38,4)", precision: 38, scale: 4, nullable: false),
                    MaximumCongestionTaskHours = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    MaximumTotalCost = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningComparison", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningComparison_Tenant_Id_Site", x => new { x.TenantId, x.Id, x.SiteId });
                    table.CheckConstraint("CK_Space_PlanningComparison_ComparisonHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_ComparisonHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningComparison_ComparisonHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_ComparisonHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningComparison_CurrencyCode_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_CurrencyCode_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningComparison_CurrencyCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8')) <= 3")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_CurrencyCode_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningComparison_DefinitionVersion_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_DefinitionVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningComparison_Invariants", "\"RunCount\" BETWEEN 2 AND 10 AND \"HistoricalFromUtc\" < \"HistoricalToUtc\" AND \"MinimumDistanceCoveragePercent\" BETWEEN 0 AND 100 AND \"MaximumPeakCapacityUtilizationPercent\" >= 0 AND \"MaximumCongestionTaskHours\" >= 0 AND (\"MaximumTotalCost\" IS NULL OR \"MaximumTotalCost\" >= 0) AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 3 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"CurrencyCode\"), 'UTF8'), 'alpha') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_Invariants")
                        .Annotation("CP6:OriginalSql", "[RunCount] BETWEEN 2 AND 10 AND [HistoricalFromUtc] < [HistoricalToUtc] AND [MinimumDistanceCoveragePercent] BETWEEN 0 AND 100 AND [MaximumPeakCapacityUtilizationPercent] >= 0 AND [MaximumCongestionTaskHours] >= 0 AND ([MaximumTotalCost] IS NULL OR [MaximumTotalCost] >= 0) AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PlanningComparison_Name_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Name\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_Name_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PlanningComparison_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningComparison_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_RequestHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningComparison_SourceDatasetHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_SourceDatasetHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningComparison_SourceDatasetHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"SourceDatasetHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparison_SourceDatasetHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparison_BaseVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.BasePublishedVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparison_BaselineRun_Tenant",
                        columns: x => new { x.TenantId, x.BaselineRunId, x.SiteId },
                        principalTable: "Space_PlanningSimulationRun",
                        principalColumns: new[] { "TenantId", "Id", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparison_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningSimulationLocationResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationLogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedTaskCount = table.Column<int>(type: "integer", nullable: false),
                    TotalQuantity = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceEligibleTaskCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDistanceMeters = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    QuantityCapacity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ConcurrentTaskCapacity = table.Column<int>(type: "integer", nullable: false),
                    PeakConcurrentTasks = table.Column<int>(type: "integer", nullable: false),
                    PeakConcurrentQuantity = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    CapacityUtilizationPercent = table.Column<decimal>(type: "numeric(38,4)", precision: 38, scale: 4, nullable: false),
                    CongestionSeconds = table.Column<long>(type: "bigint", nullable: false),
                    CongestionTaskSeconds = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningSimulationLocationResult", x => x.Id);
                    table.CheckConstraint("CK_Space_PlanningSimulationLocationResult_Invariants", "\"TaskCount\" > 0 AND \"CompletedTaskCount\" BETWEEN 0 AND \"TaskCount\" AND \"TotalQuantity\" > 0 AND \"DistanceEligibleTaskCount\" BETWEEN 0 AND \"TaskCount\" AND \"TotalDistanceMeters\" >= 0 AND \"QuantityCapacity\" > 0 AND \"ConcurrentTaskCapacity\" BETWEEN 1 AND 10000 AND \"PeakConcurrentTasks\" >= 0 AND \"PeakConcurrentQuantity\" >= 0 AND \"CapacityUtilizationPercent\" >= 0 AND \"CongestionSeconds\" >= 0 AND \"CongestionTaskSeconds\" >= 0 AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningSimulationLocationResult_Invariants")
                        .Annotation("CP6:OriginalSql", "[TaskCount] > 0 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalQuantity] > 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [QuantityCapacity] > 0 AND [ConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [PeakConcurrentTasks] >= 0 AND [PeakConcurrentQuantity] >= 0 AND [CapacityUtilizationPercent] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationLocation_Location_Tenant",
                        columns: x => new { x.TenantId, x.ScenarioVersionId, x.LocationLogicalId },
                        principalTable: "Space_LocationRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationLocation_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId, x.ScenarioVersionId },
                        principalTable: "Space_PlanningSimulationRun",
                        principalColumns: new[] { "TenantId", "Id", "ScenarioVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PublishAuditEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventNo = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<short>(type: "smallint", nullable: false),
                    AttemptStatus = table.Column<short>(type: "smallint", nullable: false),
                    Step = table.Column<short>(type: "smallint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "bpchar", maxLength: 300, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Summary = table.Column<string>(type: "bpchar", maxLength: 2000, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ErrorCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    EvidenceJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    EvidenceHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PreviousEventHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    EventHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PublishAuditEvent", x => x.Id);
                    table.CheckConstraint("CK_Space_PublishAuditEvent_DeduplicationKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"DeduplicationKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 300")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_DeduplicationKey_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_ErrorCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ErrorCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_ErrorCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_EventHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"EventHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"EventHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_EventHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_EventHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"EventHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_EventHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_EvidenceHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"EvidenceHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"EvidenceHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_EvidenceHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_EvidenceHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"EvidenceHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_EvidenceHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_Invariants", "\"EventNo\" > 0 AND (CASE WHEN \"EvidenceJson\" IS NULL THEN NULL ELSE (\"EvidenceJson\" IS JSON OBJECT OR \"EvidenceJson\" IS JSON ARRAY) END) AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"EvidenceHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"EvidenceHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"EventHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"EventHash\"), 'UTF8'), 'hex') AND (\"PreviousEventHash\" IS NULL OR (char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"PreviousEventHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"PreviousEventHash\"), 'UTF8'), 'hex')))")
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_Invariants")
                        .Annotation("CP6:OriginalSql", "[EventNo] > 0 AND ISJSON([EvidenceJson]) = 1 AND LEN([EvidenceHash]) = 64 AND [EvidenceHash] NOT LIKE '%[^0-9a-f]%' AND LEN([EventHash]) = 64 AND [EventHash] NOT LIKE '%[^0-9a-f]%' AND ([PreviousEventHash] IS NULL OR (LEN([PreviousEventHash]) = 64 AND [PreviousEventHash] NOT LIKE '%[^0-9a-f]%'))");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_PreviousEventHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"PreviousEventHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"PreviousEventHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_PreviousEventHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_PreviousEventHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PreviousEventHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_PreviousEventHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PublishAuditEvent_Summary_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Summary\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 2000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishAuditEvent_Summary_Utf16Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PublishAuditEvent_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_PublishAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PublishAuditEvent_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PublishBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNo = table.Column<int>(type: "integer", nullable: false),
                    OperationKey = table.Column<string>(type: "bpchar", maxLength: 300, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PayloadHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ExternalOperationId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ResultJson = table.Column<string>(type: "bpchar", nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    BatchAttemptNo = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PublishBatch", x => x.Id);
                    table.UniqueConstraint("AK_Space_PublishBatch_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PublishBatch_ExternalOperationId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ExternalOperationId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishBatch_ExternalOperationId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishBatch_OperationKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"OperationKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 300")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishBatch_OperationKey_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PublishBatch_PayloadHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishBatch_PayloadHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PublishBatch_PayloadHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PublishBatch_PayloadHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PublishBatch_Recovery", "\"AttemptCount\" >= 0 AND \"BatchAttemptNo\" >= 0 AND (CASE WHEN \"RequestJson\" IS NULL THEN NULL ELSE (\"RequestJson\" IS JSON OBJECT OR \"RequestJson\" IS JSON ARRAY) END)")
                        .Annotation("CP6:OriginalName", "CK_Space_PublishBatch_Recovery")
                        .Annotation("CP6:OriginalSql", "[AttemptCount] >= 0 AND [BatchAttemptNo] >= 0 AND ISJSON([RequestJson]) = 1");
                    table.ForeignKey(
                        name: "FK_Space_PublishBatch_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_PublishAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ReconciliationIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedStateHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    WmsStateHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RuntimeStateHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    Classification = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Summary = table.Column<string>(type: "bpchar", maxLength: 2000, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Resolution = table.Column<string>(type: "bpchar", maxLength: 4000, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ReconciliationIssue", x => x.Id);
                    table.CheckConstraint("CK_Space_ReconciliationIssue_ExpectedStateHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ExpectedStateHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ExpectedStateHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_ExpectedStateHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ReconciliationIssue_ExpectedStateHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ExpectedStateHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_ExpectedStateHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ReconciliationIssue_Resolution_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Resolution\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 4000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_Resolution_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ReconciliationIssue_RuntimeStateHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RuntimeStateHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RuntimeStateHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_RuntimeStateHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ReconciliationIssue_RuntimeStateHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RuntimeStateHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_RuntimeStateHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_ReconciliationIssue_Summary_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Summary\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 2000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_Summary_Utf16Capacity");
                    table.CheckConstraint("CK_Space_ReconciliationIssue_WmsStateHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"WmsStateHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"WmsStateHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_WmsStateHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_ReconciliationIssue_WmsStateHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"WmsStateHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_ReconciliationIssue_WmsStateHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_ReconciliationIssue_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_PublishAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningComparisonEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    RunName = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RunResultHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    IsBaseline = table.Column<bool>(type: "boolean", nullable: false),
                    DistanceCoveragePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    TotalDistanceMeters = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    CongestionTaskSeconds = table.Column<long>(type: "bigint", nullable: false),
                    OverloadedLocationCount = table.Column<int>(type: "integer", nullable: false),
                    PeakCapacityUtilizationPercent = table.Column<decimal>(type: "numeric(38,4)", precision: 38, scale: 4, nullable: false),
                    AverageCompletedTasksPerHour = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    PeakCompletedTasksPerHour = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceDeltaMeters = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    CongestionTaskSecondsDelta = table.Column<long>(type: "bigint", nullable: false),
                    OverloadedLocationCountDelta = table.Column<int>(type: "integer", nullable: false),
                    PeakCapacityUtilizationDeltaPercentagePoints = table.Column<decimal>(type: "numeric(38,4)", precision: 38, scale: 4, nullable: false),
                    AverageCompletedTasksPerHourDelta = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    TotalCostDelta = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    RiskCount = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningComparisonEntry", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningComparisonEntry_Comparison_Id_Run", x => new { x.TenantId, x.ComparisonId, x.Id, x.RunId });
                    table.UniqueConstraint("AK_Space_PlanningComparisonEntry_Comparison_Run", x => new { x.TenantId, x.ComparisonId, x.RunId });
                    table.CheckConstraint("CK_Space_PlanningComparisonEntry_Invariants", "\"SequenceNo\" BETWEEN 1 AND 10 AND \"ScenarioContentRevision\" >= 0 AND \"DistanceCoveragePercent\" BETWEEN 0 AND 100 AND \"TotalDistanceMeters\" >= 0 AND \"CongestionTaskSeconds\" >= 0 AND \"OverloadedLocationCount\" >= 0 AND \"PeakCapacityUtilizationPercent\" >= 0 AND \"AverageCompletedTasksPerHour\" >= 0 AND \"PeakCompletedTasksPerHour\" >= 0 AND \"TotalCost\" >= 0 AND \"RiskCount\" BETWEEN 0 AND 10 AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RunResultHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RunResultHash\"), 'UTF8'), 'hex') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparisonEntry_Invariants")
                        .Annotation("CP6:OriginalSql", "[SequenceNo] BETWEEN 1 AND 10 AND [ScenarioContentRevision] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [TotalDistanceMeters] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [TotalCost] >= 0 AND [RiskCount] BETWEEN 0 AND 10 AND LEN([RunResultHash]) = 64 AND [RunResultHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PlanningComparisonEntry_RunName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"RunName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparisonEntry_RunName_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PlanningComparisonEntry_RunResultHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RunResultHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RunResultHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparisonEntry_RunResultHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningComparisonEntry_RunResultHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RunResultHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparisonEntry_RunResultHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparisonEntry_Comparison_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SiteId },
                        principalTable: "Space_PlanningComparison",
                        principalColumns: new[] { "TenantId", "Id", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparisonEntry_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId, x.BranchId, x.ScenarioVersionId },
                        principalTable: "Space_PlanningSimulationRun",
                        principalColumns: new[] { "TenantId", "Id", "BranchId", "ScenarioVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_WmsReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationCode = table.Column<string>(type: "bpchar", maxLength: 256, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Action = table.Column<short>(type: "smallint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    ExternalLocationId = table.Column<string>(type: "bpchar", maxLength: 200, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ExternalVersion = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ResponseHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ErrorCode = table.Column<string>(type: "bpchar", maxLength: 100, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_WmsReceipt", x => x.Id);
                    table.CheckConstraint("CK_Space_WmsReceipt_ErrorCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ErrorCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsReceipt_ErrorCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsReceipt_ExternalLocationId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ExternalLocationId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsReceipt_ExternalLocationId_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsReceipt_ExternalVersion_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ExternalVersion\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsReceipt_ExternalVersion_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsReceipt_LocationCode_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LocationCode\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 256")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsReceipt_LocationCode_Utf16Capacity");
                    table.CheckConstraint("CK_Space_WmsReceipt_ResponseHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ResponseHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ResponseHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsReceipt_ResponseHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_WmsReceipt_ResponseHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ResponseHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_WmsReceipt_ResponseHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_WmsReceipt_Batch_Tenant",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "Space_PublishBatch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningComparisonRisk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningComparisonRisk", x => x.Id);
                    table.CheckConstraint("CK_Space_PlanningComparisonRisk_Code_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Code\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparisonRisk_Code_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningComparisonRisk_Invariants", "\"Severity\" BETWEEN 1 AND 3 AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"Code\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) BETWEEN 1 AND 100 AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningComparisonRisk_Invariants")
                        .Annotation("CP6:OriginalSql", "[Severity] BETWEEN 1 AND 3 AND LEN([Code]) BETWEEN 1 AND 100 AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparisonRisk_Entry_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.EntryId, x.RunId },
                        principalTable: "Space_PlanningComparisonEntry",
                        principalColumns: new[] { "TenantId", "ComparisonId", "Id", "RunId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningDecisionRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersedesDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Rationale = table.Column<string>(type: "bpchar", maxLength: 2000, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ComparisonHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    RequestHash = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    DefinitionVersion = table.Column<string>(type: "bpchar", unicode: false, maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningDecisionRecord", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningDecisionRecord_Comparison_Id", x => new { x.TenantId, x.ComparisonId, x.Id });
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_ComparisonHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningDecisionRecord_ComparisonHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_ComparisonHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningDecisionRecord_ComparisonHash_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_DefinitionVersion_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"DefinitionVersion\"), 'UTF8')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningDecisionRecord_DefinitionVersion_Cp936Capacity");
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_Invariants", "\"Outcome\" BETWEEN 1 AND 3 AND ((\"Outcome\" = 1 AND \"SelectedRunId\" IS NOT NULL) OR (\"Outcome\" IN (2, 3) AND \"SelectedRunId\" IS NULL)) AND (\"SupersedesDecisionId\" IS NULL OR \"SupersedesDecisionId\" <> \"Id\") AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"Rationale\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) BETWEEN 1 AND 2000 AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"ComparisonHash\"), 'UTF8'), 'hex') AND char_length(regexp_replace((rtrim(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), ' '))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) = 64 AND public.cp6_text_range_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'), 'hex') AND \"IsDeleted\" = FALSE")
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningDecisionRecord_Invariants")
                        .Annotation("CP6:OriginalSql", "[Outcome] BETWEEN 1 AND 3 AND (([Outcome] = 1 AND [SelectedRunId] IS NOT NULL) OR ([Outcome] IN (2, 3) AND [SelectedRunId] IS NULL)) AND ([SupersedesDecisionId] IS NULL OR [SupersedesDecisionId] <> [Id]) AND LEN([Rationale]) BETWEEN 1 AND 2000 AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_Rationale_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"Rationale\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 2000")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningDecisionRecord_Rationale_Utf16Capacity");
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_RequestHash_AsciiDomain", "octet_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) = char_length(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8'))")
                        .Annotation("CP6:FixedAsciiDomain", true)
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningDecisionRecord_RequestHash_AsciiDomain");
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_RequestHash_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"RequestHash\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Space_PlanningDecisionRecord_RequestHash_Cp936Capacity");
                    table.ForeignKey(
                        name: "FK_Space_PlanningDecisionRecord_Comparison_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SiteId },
                        principalTable: "Space_PlanningComparison",
                        principalColumns: new[] { "TenantId", "Id", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningDecisionRecord_SelectedRun_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SelectedRunId },
                        principalTable: "Space_PlanningComparisonEntry",
                        principalColumns: new[] { "TenantId", "ComparisonId", "RunId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningDecisionRecord_Supersedes_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SupersedesDecisionId },
                        principalTable: "Space_PlanningDecisionRecord",
                        principalColumns: new[] { "TenantId", "ComparisonId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiBudgetReservation_Tenant_Day",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "Currency", "PeriodDay", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiBudgetReservation_Tenant_Month",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "Currency", "PeriodMonth", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AiBudgetReservation_TenantId_RunId",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "UX_AiBudgetReservation_Tenant_Request",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "ProviderRequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_AisleRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_AisleRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_AisleRevision_Zone_Code_Active",
                table: "Space_AisleRevision",
                columns: new[] { "TenantId", "ModelVersionId", "ZoneLogicalId", "AisleCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_AiTenantPolicy_Tenant_Active",
                table: "Space_AiTenantPolicy",
                column: "TenantId",
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_AiTenantPolicy_Tenant_Version",
                table: "Space_AiTenantPolicy",
                columns: new[] { "TenantId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsage_Tenant_Retention",
                table: "Space_AiUsageRecord",
                columns: new[] { "TenantId", "ArchivedAtUtc", "RecordedAtUtc", "Id" },
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsage_Tenant_Run_Recorded",
                table: "Space_AiUsageRecord",
                columns: new[] { "TenantId", "RunId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AiUsage_Tenant_ProviderRequest",
                table: "Space_AiUsageRecord",
                columns: new[] { "TenantId", "ProviderRequestIdHash" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_File_Active",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "FileId" },
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_Job_Active",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "JobId" },
                filter: "\"JobId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_Version",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "ModelVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_Version_Source_Active",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                filter: "\"SourceId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Asset_Scope_Owner_Category",
                table: "Space_Asset",
                columns: new[] { "Scope", "OwnerTenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_Asset_Scope_Owner_Code_Active",
                table: "Space_Asset",
                columns: new[] { "Scope", "OwnerTenantId", "AssetCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_AssetVersion_Scope_Owner_Asset_VersionNo",
                table: "Space_AssetVersion",
                columns: new[] { "Scope", "OwnerTenantId", "AssetId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_CadParsePreparation_Source_Expiry",
                table: "Space_CadParsePreparation",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_CadProviderCertification_Site_Expiry",
                table: "Space_CadSiteProviderCertification",
                columns: new[] { "TenantId", "SiteId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderCertification_Provider",
                table: "Space_CadSiteProviderCertification",
                columns: new[] { "TenantId", "ConfigurationId", "ProviderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderCertification_Role",
                table: "Space_CadSiteProviderCertification",
                columns: new[] { "TenantId", "ConfigurationId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderConfiguration_Current",
                table: "Space_CadSiteProviderConfiguration",
                columns: new[] { "TenantId", "SiteId" },
                unique: true,
                filter: "\"IsCurrent\" = TRUE AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderConfiguration_Site_Revision",
                table: "Space_CadSiteProviderConfiguration",
                columns: new[] { "TenantId", "SiteId", "ConfigurationRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_DesignAttribute_TenantId_ModelVersionId_SourceId",
                table: "Space_DesignAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DesignAttribute_Target_Key_Active",
                table: "Space_DesignAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "ObjectType", "ObjectLogicalId", "Namespace", "Key" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceAlarmState_Tenant_Site_Active_Severity_Time",
                table: "Space_DeviceAlarmState",
                columns: new[] { "TenantId", "SiteId", "IsActive", "AlarmSeverity", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceAlarmState_TenantId_DeviceMappingId",
                table: "Space_DeviceAlarmState",
                columns: new[] { "TenantId", "DeviceMappingId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceAlarmState_Tenant_Site_Source_Device_Alarm",
                table: "Space_DeviceAlarmState",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId", "AlarmExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceEvent_Tenant_Site_Alarm_Time",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "SiteId", "AlarmExternalId", "OccurredAtUtc" },
                filter: "\"AlarmExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceEvent_Tenant_Site_Source_Device_Time",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceEvent_TenantId_DeviceMappingId",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "DeviceMappingId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceEvent_Tenant_Site_Source_Event",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceMapping_TenantId_ValidatedModelVers_2058d0b81e19",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "ValidatedModelVersionId", "ElementLogicalId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceMapping_Tenant_Site_Source_Device",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceMapping_Tenant_Site_Source_Element",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "SiteId", "SourceId", "ElementLogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceState_Tenant_Site_State_Time",
                table: "Space_DeviceState",
                columns: new[] { "TenantId", "SiteId", "OperatingState", "OperatingStateOccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceState_TenantId_DeviceMappingId",
                table: "Space_DeviceState",
                columns: new[] { "TenantId", "DeviceMappingId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceState_Tenant_Site_Source_Device",
                table: "Space_DeviceState",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_DispatchRecommendation_Tenant_Site_Generated",
                table: "Space_DispatchRecommendation",
                columns: new[] { "TenantId", "SiteId", "GeneratedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DispatchRecommendation_TenantId_PublishedVersionId",
                table: "Space_DispatchRecommendation",
                columns: new[] { "TenantId", "PublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_EditLease_Version_Floor",
                table: "Space_EditLease",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_EditLeaseTakeoverAudit_Floor_TakenOver",
                table: "Space_EditLeaseTakeoverAudit",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "TakenOverAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ElementAttribute_Element_Key_Active",
                table: "Space_ElementAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "ElementRevisionId", "Namespace", "Key" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementCommandBatch_Floor_Applied",
                table: "Space_ElementCommandBatch",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "AppliedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ElementCommandRecord_Batch_Sequence",
                table: "Space_ElementCommandRecord",
                columns: new[] { "TenantId", "CommandBatchId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_Floor_Type",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "ElementType" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_ModelAssetScope_ModelAsse_b34982bd4fa0",
                table: "Space_ElementRevision",
                columns: new[] { "ModelAssetScope", "ModelAssetOwnerTenantId", "ModelAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_TenantId_ModelVersionId_P_bf289873dd94",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "ParentLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExcelMappingProfile_CurrentName",
                table: "Space_ExcelMappingProfile",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExcelMappingProfileVersion_DefinitionHash",
                table: "Space_ExcelMappingProfileVersion",
                columns: new[] { "TenantId", "DefinitionHash" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExcelMappingProfileVersion_Profile_Version",
                table: "Space_ExcelMappingProfileVersion",
                columns: new[] { "TenantId", "ProfileId", "Version" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalGrant_Organization_Site_Status",
                table: "Space_ExternalGrant",
                columns: new[] { "TenantId", "OrganizationId", "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalGrant_Organization_Status_Validity",
                table: "Space_ExternalGrant",
                columns: new[] { "TenantId", "OrganizationId", "Status", "ValidFromUtc", "ValidToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalGrant_TenantId_FieldPolicyId",
                table: "Space_ExternalGrant",
                columns: new[] { "TenantId", "FieldPolicyId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantFloor_Current",
                table: "Space_ExternalGrantFloor",
                columns: new[] { "TenantId", "GrantId", "FloorLogicalId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantObject_Current",
                table: "Space_ExternalGrantObject",
                columns: new[] { "TenantId", "GrantId", "NormalizedBusinessObjectType", "NormalizedBusinessObjectId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantOwner_Current",
                table: "Space_ExternalGrantOwner",
                columns: new[] { "TenantId", "GrantId", "NormalizedOwnerId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantZone_Current",
                table: "Space_ExternalGrantZone",
                columns: new[] { "TenantId", "GrantId", "ZoneLogicalId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalMembership_Tenant_User_Status_Validity",
                table: "Space_ExternalMembership",
                columns: new[] { "TenantId", "UserId", "Status", "ValidFromUtc", "ValidToUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalMembership_Tenant_Organization_User_Current",
                table: "Space_ExternalMembership",
                columns: new[] { "TenantId", "OrganizationId", "UserId" },
                unique: true,
                filter: "\"Status\" <> 3 AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalOrganization_Tenant_Status_Name",
                table: "Space_ExternalOrganization",
                columns: new[] { "TenantId", "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalOrganization_Tenant_Type_Code",
                table: "Space_ExternalOrganization",
                columns: new[] { "TenantId", "Type", "NormalizedCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalOrganization_Tenant_Type_Partner",
                table: "Space_ExternalOrganization",
                columns: new[] { "TenantId", "Type", "BusinessPartnerType", "BusinessPartnerId" },
                unique: true,
                filter: "\"BusinessPartnerId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_FieldPolicy_CurrentName",
                table: "Space_FieldPolicy",
                columns: new[] { "TenantId", "AudienceType", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_FieldPolicyField_Current",
                table: "Space_FieldPolicyField",
                columns: new[] { "TenantId", "PolicyId", "ResourceType", "NormalizedFieldName" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_File_Tenant_PendingObjectDeletion",
                table: "Space_File",
                columns: new[] { "TenantId", "DeletionRequestedAtUtc", "ContentDeletedAtUtc" },
                filter: "\"State\" = 5 AND \"DeletionRequestedAtUtc\" IS NOT NULL AND \"ContentDeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_File_Tenant_Retention",
                table: "Space_File",
                columns: new[] { "TenantId", "RetainUntilUtc", "State" },
                filter: "\"RetainUntilUtc\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_File_Tenant_State",
                table: "Space_File",
                columns: new[] { "TenantId", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_File_StorageKey",
                table: "Space_File",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_File_Tenant_Hash_Retention_Reusable",
                table: "Space_File",
                columns: new[] { "TenantId", "Sha256", "RetentionClass" },
                unique: true,
                filter: "\"Sha256\" IS NOT NULL AND \"State\" IN (1, 2, 3) AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_TenantId_ModelVersionId_Log_e14b437a839b",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "LogicalId", "UnderlaySourceId", "UnderlayCalibrationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_TenantId_ModelVersionId_UnderlaySourceId",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "UnderlaySourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_Version_Level",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "Level" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_FloorRevision_Version_Code_Active",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationLockedFact_Tenant_Decision_Run",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "SourceDecisionId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationLockedFact_TenantId_BasedOnRunI_ee796a533f79",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "BasedOnRunId", "SourceProposalId" });

            migrationBuilder.CreateIndex(
                name: "UX_GenerationLockedFact_Tenant_Run_Source_Type_Field",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "RunId", "SourceKey", "ProposalType", "FieldPath" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Proposal_Tenant_Purge_Run",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "PayloadPurgedAtUtc", "RunId", "Id" },
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Proposal_Tenant_Run_Status_Band_Type",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "RunId", "Status", "ConfidenceBand", "ProposalType", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationProposal_TenantId_ModelVersionId",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "ModelVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Proposal_Tenant_Run_Source_Type",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "RunId", "SourceKey", "ProposalType" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Job",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Retention",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "PayloadPurgedAtUtc", "IsCurrent", "Status", "CreatedAtUtc", "Id" },
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Site_Status_Created",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "SiteId", "Status", "CreatedAtUtc" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Version_Current",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_ApplyJobId",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ApplyJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_BasedOnRunId",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "BasedOnRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_ModelVersionId_SourceId",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_ModelVersionId_Tar_6530650021ab",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "TargetFloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "UX_GenerationRun_Tenant_Business_Current",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "BusinessKeyHash" },
                unique: true,
                filter: "\"IsCurrent\" = TRUE AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationStagingElement_TenantId_ModelVe_882829c04dc5",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "UX_GenerationStaging_Tenant_Run_Logical",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "RunId", "LogicalId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_GenerationStaging_Tenant_Run_Proposal",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "RunId", "ProposalId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_GenerationStaging_Tenant_Run_Sequence",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "RunId", "SequenceNo" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_Tenant_Site_Requested",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "SiteId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_JobId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ModelId_Expe_32ec9759e0ac",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "ExpectedPublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ModelId_Hist_c9ba8f3f5131",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "HistoricalVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ModelId_TargetVersionId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "TargetVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ValidationRunId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ValidationRunId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_HistoricalRepublish_Tenant_Idempotency",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "BusinessIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_HistoricalRepublish_Tenant_PublishAttempt",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "PublishAttemptId" },
                unique: true,
                filter: "\"PublishAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_IdempotencyRecord_Tenant_Retention",
                table: "Space_IdempotencyRecord",
                columns: new[] { "TenantId", "RetainUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_IdempotencyRecord_Tenant_Principal_Operation_Key",
                table: "Space_IdempotencyRecord",
                columns: new[] { "TenantId", "PrincipalId", "Operation", "IdempotencyKeyHash" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_Tenant_Claim",
                table: "Space_Job",
                columns: new[] { "TenantId", "Status", "NextAttemptAtUtc", "LockExpiresAtUtc", "Priority", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_Tenant_Correlation",
                table: "Space_Job",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_Tenant_Subject",
                table: "Space_Job",
                columns: new[] { "TenantId", "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_TenantId_RetryOfJobId",
                table: "Space_Job",
                columns: new[] { "TenantId", "RetryOfJobId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_Job_Tenant_Type_BusinessKey_Active",
                table: "Space_Job",
                columns: new[] { "TenantId", "JobType", "BusinessKey" },
                unique: true,
                filter: "\"Status\" IN (0, 1) AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_JobAttempt_Tenant_Job_Started",
                table: "Space_JobAttempt",
                columns: new[] { "TenantId", "JobId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_JobAttempt_TenantId_DiagnosticArtifactId",
                table: "Space_JobAttempt",
                columns: new[] { "TenantId", "DiagnosticArtifactId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_JobAttempt_Tenant_Job_AttemptNo",
                table: "Space_JobAttempt",
                columns: new[] { "TenantId", "JobId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_JobStep_Tenant_Attempt_StepCode",
                table: "Space_JobStep",
                columns: new[] { "TenantId", "AttemptId", "StepCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_JobStep_Tenant_Attempt_StepNo",
                table: "Space_JobStep",
                columns: new[] { "TenantId", "AttemptId", "StepNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_LayerMappingProfile_CurrentName",
                table: "Space_LayerMappingProfile",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LayerMappingProfileVersion_DefinitionHash",
                table: "Space_LayerMappingProfileVersion",
                columns: new[] { "TenantId", "DefinitionHash" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_LayerMappingProfileVersion_Profile_Version",
                table: "Space_LayerMappingProfileVersion",
                columns: new[] { "TenantId", "ProfileId", "Version" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationExternalBinding_TenantId_ModelVer_72c512052839",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_LocationExternalBinding_External_Active",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "AdapterId", "WarehouseCode", "ExternalLocationId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_LocationExternalBinding_Primary_Active",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "LocationLogicalId" },
                unique: true,
                filter: "\"BindingMode\" = 0 AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationRevision_Rack_Position_Active",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "RackLogicalId", "LevelNo", "ColumnNo", "DepthNo" },
                filter: "\"RackLogicalId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationRevision_TenantId_ModelVersionId__4e948767ad47",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_LocationRevision_Version_Code_Active",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "LocationCode" },
                unique: true,
                filter: "\"LocationCode\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Model_TenantId_Id_ActiveDraftVersionId",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "ActiveDraftVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Model_TenantId_Id_CurrentPublishedVersionId",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "CurrentPublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_Model_Tenant_ActiveDraft",
                table: "Space_Model",
                columns: new[] { "TenantId", "ActiveDraftVersionId" },
                unique: true,
                filter: "\"ActiveDraftVersionId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_Model_Tenant_CurrentPublished",
                table: "Space_Model",
                columns: new[] { "TenantId", "CurrentPublishedVersionId" },
                unique: true,
                filter: "\"CurrentPublishedVersionId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_Model_Tenant_Site_Active",
                table: "Space_Model",
                columns: new[] { "TenantId", "SiteId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_ModelIssue_Tenant_Purge_Run",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "PayloadPurgedAtUtc", "GenerationRunId", "Id" },
                filter: "\"GenerationRunId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Job_Status",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "JobId", "Status" },
                filter: "\"JobId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Run_Proposal_Status",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "GenerationRunId", "GenerationProposalId", "Status", "Severity" },
                filter: "\"GenerationRunId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Validation_Severity_Code",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ValidationRunId", "Severity", "Code", "Id" },
                filter: "\"ValidationRunId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Version_Status",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ModelVersionId", "Status", "Severity", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_TenantId_ModelVersionId_SourceId",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_TenantId_ResolutionDecisionId",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ResolutionDecisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelSource_Tenant_File_Active",
                table: "Space_ModelSource",
                columns: new[] { "TenantId", "FileId" },
                filter: "\"FileId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelSource_Tenant_SourceHash",
                table: "Space_ModelSource",
                columns: new[] { "TenantId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ModelSource_Version_Hash_Type_Active",
                table: "Space_ModelSource",
                columns: new[] { "TenantId", "ModelVersionId", "Sha256", "SourceType" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelVersion_Tenant_BasedOn",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "BasedOnVersionId" },
                filter: "\"BasedOnVersionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelVersion_Tenant_Model_Status",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelVersion_TenantId_ModelId_BasedOnVersionId",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "BasedOnVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ModelVersion_Tenant_Model_CloneOperation",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "CloneOperationId" },
                unique: true,
                filter: "\"CloneOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ModelVersion_Tenant_Model_VersionNo",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PersonnelEvent_Tenant_Site_Source_Person_Time",
                table: "Space_PersonnelEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "PersonExternalId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PersonnelEvent_Tenant_Site_Source_Event",
                table: "Space_PersonnelEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PersonnelState_Tenant_Site_WorkState_Time",
                table: "Space_PersonnelState",
                columns: new[] { "TenantId", "SiteId", "WorkState", "WorkStateOccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PersonnelState_Tenant_Site_Source_Person",
                table: "Space_PersonnelState",
                columns: new[] { "TenantId", "SiteId", "SourceId", "PersonExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparison_Site_Created",
                table: "Space_PlanningComparison",
                columns: new[] { "TenantId", "SiteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparison_TenantId_BaselineRunId_SiteId",
                table: "Space_PlanningComparison",
                columns: new[] { "TenantId", "BaselineRunId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparison_TenantId_ModelId_BaseP_9ceaecae50f7",
                table: "Space_PlanningComparison",
                columns: new[] { "TenantId", "ModelId", "BasePublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparisonEntry_TenantId_ComparisonId_SiteId",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "ComparisonId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparisonEntry_TenantId_RunId_Br_d6070a1cab36",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "RunId", "BranchId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningComparisonEntry_Comparison_Baseline",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "ComparisonId", "IsBaseline" },
                unique: true,
                filter: "\"IsBaseline\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningComparisonEntry_Comparison_Sequence",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "ComparisonId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparisonRisk_TenantId_Compariso_08b1c426a464",
                table: "Space_PlanningComparisonRisk",
                columns: new[] { "TenantId", "ComparisonId", "EntryId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningComparisonRisk_Entry_Code",
                table: "Space_PlanningComparisonRisk",
                columns: new[] { "TenantId", "EntryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningDecisionRecord_Comparison_Created",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningDecisionRecord_TenantId_Compariso_884702463dfb",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "SelectedRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningDecisionRecord_TenantId_ComparisonId_SiteId",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningDecisionRecord_Supersedes",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "SupersedesDecisionId" },
                unique: true,
                filter: "\"SupersedesDecisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningHistoricalDataset_Branch_Created",
                table: "Space_PlanningHistoricalDataset",
                columns: new[] { "TenantId", "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningHistoricalDataset_TenantId_ModelI_91daa3972d38",
                table: "Space_PlanningHistoricalDataset",
                columns: new[] { "TenantId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningHistoricalTask_Dataset_Sequence",
                table: "Space_PlanningHistoricalTask",
                columns: new[] { "TenantId", "DatasetId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningHistoricalTask_Dataset_Token",
                table: "Space_PlanningHistoricalTask",
                columns: new[] { "TenantId", "DatasetId", "TaskToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningScenarioBranch_Site_Created",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "SiteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningScenarioBranch_TenantId_ModelId_B_b20aa92be1f6",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "ModelId", "BasePublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningScenarioBranch_TenantId_ModelId_S_25f2ff99f515",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningScenarioBranch_CloneJob",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "CloneJobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningScenarioBranch_ScenarioVersion",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "ScenarioVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationLocationResult_TenantId_9a68ae7911bd",
                table: "Space_PlanningSimulationLocationResult",
                columns: new[] { "TenantId", "RunId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationLocationResult_TenantId_ffd3e22e22f5",
                table: "Space_PlanningSimulationLocationResult",
                columns: new[] { "TenantId", "ScenarioVersionId", "LocationLogicalId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningSimulationLocation_Run_Location",
                table: "Space_PlanningSimulationLocationResult",
                columns: new[] { "TenantId", "RunId", "LocationLogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_Branch_Created",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_Dataset_Created",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "DatasetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_TenantId_DatasetId__13722f859fbf",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "DatasetId", "BranchId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_TenantId_ModelId_Sc_3e0d73f49ca0",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalDecision_Tenant_Batch",
                table: "Space_ProposalDecision",
                columns: new[] { "TenantId", "DecisionBatchId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalDecision_Tenant_Run_Proposal_Created",
                table: "Space_ProposalDecision",
                columns: new[] { "TenantId", "RunId", "ProposalId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAttempt_Tenant_Site_Started",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "SiteId", "StartedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAttempt_TenantId_JobId",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAttempt_TenantId_PublishPlanId",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "PublishPlanId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAttempt_Tenant_Idempotency",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "BusinessIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAttempt_Tenant_Site_Active",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "SiteId" },
                unique: true,
                filter: "\"OwnsPublishSlot\" = TRUE AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAuditEvent_Tenant_Job_Occurred",
                table: "Space_PublishAuditEvent",
                columns: new[] { "TenantId", "JobId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAuditEvent_Tenant_Attempt_Dedupe",
                table: "Space_PublishAuditEvent",
                columns: new[] { "TenantId", "AttemptId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAuditEvent_Tenant_Attempt_EventNo",
                table: "Space_PublishAuditEvent",
                columns: new[] { "TenantId", "AttemptId", "EventNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishBatch_Tenant_Attempt_BatchNo",
                table: "Space_PublishBatch",
                columns: new[] { "TenantId", "AttemptId", "BatchNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishBatch_Tenant_OperationKey",
                table: "Space_PublishBatch",
                columns: new[] { "TenantId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishPlan_Tenant_Site_Target_Created",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "SiteId", "TargetVersionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishPlan_TenantId_TargetVersionId",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "TargetVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishPlan_TenantId_ValidationRunId",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "ValidationRunId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishPlan_Tenant_PlanHash",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "PlanHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PutawayRecommendation_Tenant_Site_Generated",
                table: "Space_PutawayRecommendation",
                columns: new[] { "TenantId", "SiteId", "GeneratedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PutawayRecommendation_TenantId_PublishedVersionId",
                table: "Space_PutawayRecommendation",
                columns: new[] { "TenantId", "PublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackGenerationProfile_Scope_Owner_Code_Active",
                table: "Space_RackGenerationProfile",
                columns: new[] { "Scope", "OwnerTenantId", "ProfileCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackGenerationProfileVersion_Scope_Owner__cf3b7e5bb7a9",
                table: "Space_RackGenerationProfileVersion",
                columns: new[] { "Scope", "OwnerTenantId", "ProfileId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackLevelRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_RackLevelRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackLevelRevision_Rack_Level_Active",
                table: "Space_RackLevelRevision",
                columns: new[] { "TenantId", "ModelVersionId", "RackLogicalId", "LevelNo" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackRevision_TenantId_ModelVersionId_AisleLogicalId",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "AisleLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackRevision_TenantId_ModelVersionId_FloorLogicalId",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackRevision_Zone_Code_Active",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "ZoneLogicalId", "RackCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ReconciliationIssue_Tenant_Attempt_Status",
                table: "Space_ReconciliationIssue",
                columns: new[] { "TenantId", "AttemptId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_RuntimeElement_Tenant_Site_Version_Active",
                table: "Space_RuntimeElement",
                columns: new[] { "TenantId", "SiteId", "ModelVersionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RuntimeElement_Tenant_Site_LogicalId",
                table: "Space_RuntimeElement",
                columns: new[] { "TenantId", "SiteId", "LogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiWorkSlot_Tenant_Expiry",
                table: "Space_TenantAiWorkSlot",
                columns: new[] { "TenantId", "LeaseExpiresAtUtc", "SlotNo" });

            migrationBuilder.CreateIndex(
                name: "UX_TenantAiWorkSlot_Tenant_Run",
                table: "Space_TenantAiWorkSlot",
                columns: new[] { "TenantId", "RunId" },
                unique: true,
                filter: "\"RunId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_UnderlayCalibration_Version_Floor_Created",
                table: "Space_UnderlayCalibration",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_UnderlayCalibration_Version_Source",
                table: "Space_UnderlayCalibration",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ValidationRun_Tenant_Version_Requested",
                table: "Space_ValidationRun",
                columns: new[] { "TenantId", "ModelVersionId", "RequestedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ValidationRun_Tenant_Input_ActiveOrReusable",
                table: "Space_ValidationRun",
                columns: new[] { "TenantId", "ModelVersionId", "ContentHash", "RuleSetVersion", "AdapterId", "CapabilityHash" },
                unique: true,
                filter: "\"Status\" <> 4 AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ValidationRun_Tenant_Job",
                table: "Space_ValidationRun",
                columns: new[] { "TenantId", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_WarehouseTemplate_Tenant_Code_Active",
                table: "Space_WarehouseTemplate",
                columns: new[] { "TenantId", "NormalizedTemplateCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_WarehouseTemplateVersion_ContentHash",
                table: "Space_WarehouseTemplateVersion",
                columns: new[] { "TenantId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_WarehouseTemplateVersion_Template_Version",
                table: "Space_WarehouseTemplateVersion",
                columns: new[] { "TenantId", "TemplateId", "VersionNo" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Space_WmsAdoption_Tenant_Site_Adapter_Status_Code",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "Status", "WmsLocationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_WmsAdoption_TenantId_ModelVersionId",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "ModelVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsAdoption_Tenant_Site_Adapter_External",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "ExternalLocationId" },
                unique: true,
                filter: "\"ExternalLocationId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsAdoption_Tenant_Site_Adapter_Location",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "LocationLogicalId" },
                unique: true,
                filter: "\"LocationLogicalId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsAdoption_Tenant_Site_Adapter_WmsLogical",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "WmsLogicalId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsReceipt_Tenant_Batch_LogicalId",
                table: "Space_WmsReceipt",
                columns: new[] { "TenantId", "BatchId", "LogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_ZoneRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_ZoneRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ZoneRevision_Floor_Code_Active",
                table: "Space_ZoneRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "ZoneCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.AddForeignKey(
                name: "FK_Space_AiBudgetReservation_Run_Tenant",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "RunId" },
                principalTable: "Space_GenerationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_AisleRevision_Space_ModelSource_TenantId__6f44f4f86e9e",
                table: "Space_AisleRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_AisleRevision_Space_ModelVersion_TenantId_341b261a2f33",
                table: "Space_AisleRevision",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_AisleRevision_Zone_Tenant_Version_Logical",
                table: "Space_AisleRevision",
                columns: new[] { "TenantId", "ModelVersionId", "ZoneLogicalId" },
                principalTable: "Space_ZoneRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_AiUsageRecord_Run_Tenant",
                table: "Space_AiUsageRecord",
                columns: new[] { "TenantId", "RunId" },
                principalTable: "Space_GenerationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_Artifact_Source_Tenant_Version",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_Artifact_Version_Tenant",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_CadParsePreparation_Source_Tenant",
                table: "Space_CadParsePreparation",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_CadParsePreparation_Version_Tenant",
                table: "Space_CadParsePreparation",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_DesignAttribute_Source_Tenant_Version",
                table: "Space_DesignAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_DesignAttribute_Version_Tenant",
                table: "Space_DesignAttribute",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_DeviceAlarmState_Mapping_Tenant",
                table: "Space_DeviceAlarmState",
                columns: new[] { "TenantId", "DeviceMappingId" },
                principalTable: "Space_DeviceMapping",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_DeviceEvent_Mapping_Tenant",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "DeviceMappingId" },
                principalTable: "Space_DeviceMapping",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_DeviceMapping_Element_Tenant_Version_Logical",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "ValidatedModelVersionId", "ElementLogicalId" },
                principalTable: "Space_ElementRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_DeviceMapping_ModelVersion_Tenant",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "ValidatedModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_DispatchRecommendation_Version_Tenant",
                table: "Space_DispatchRecommendation",
                columns: new[] { "TenantId", "PublishedVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_EditLease_Floor_Tenant_Version_Logical",
                table: "Space_EditLease",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" },
                principalTable: "Space_FloorRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_EditLease_Version_Tenant",
                table: "Space_EditLease",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_EditLeaseTakeoverAudit_Version_Tenant",
                table: "Space_EditLeaseTakeoverAudit",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ElementAttribute_Element_Tenant_Version",
                table: "Space_ElementAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "ElementRevisionId" },
                principalTable: "Space_ElementRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ElementCommandBatch_Floor_Tenant_Version_Logical",
                table: "Space_ElementCommandBatch",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" },
                principalTable: "Space_FloorRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ElementCommandBatch_Version_Tenant",
                table: "Space_ElementCommandBatch",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ElementRevision_Floor_Tenant_Version_Logical",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" },
                principalTable: "Space_FloorRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ElementRevision_Space_ModelSource_TenantI_25005af9d910",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ElementRevision_Space_ModelVersion_Tenant_c7b46b89684d",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_FloorRevision_Space_ModelSource_TenantId__8195d54198d5",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_FloorRevision_UnderlaySource_Tenant_Version",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "UnderlaySourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_FloorRevision_Space_ModelVersion_TenantId_4c7cfddf8c3d",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_FloorRevision_UnderlayCalibration_Tenant__f39ea0d77937",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "LogicalId", "UnderlaySourceId", "UnderlayCalibrationId" },
                principalTable: "Space_UnderlayCalibration",
                principalColumns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "SourceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationLockedFact_BasedOnRun_Tenant",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "BasedOnRunId" },
                principalTable: "Space_GenerationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationLockedFact_Run_Tenant",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "RunId" },
                principalTable: "Space_GenerationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationLockedFact_Decision_Tenant",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "SourceDecisionId" },
                principalTable: "Space_ProposalDecision",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationLockedFact_Proposal_Tenant_Run",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "BasedOnRunId", "SourceProposalId" },
                principalTable: "Space_GenerationProposal",
                principalColumns: new[] { "TenantId", "RunId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationProposal_Run_Tenant",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "RunId" },
                principalTable: "Space_GenerationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationProposal_Version_Tenant",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationRun_Source_Tenant_Version",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationRun_Version_Tenant",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_HistoricalRepublish_ExpectedVersion_Tenant",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "ExpectedPublishedVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "ModelId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_HistoricalRepublish_HistoricalVersion_Tenant",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "HistoricalVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "ModelId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_HistoricalRepublish_TargetVersion_Tenant",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "TargetVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "ModelId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_HistoricalRepublish_Model_Tenant",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId" },
                principalTable: "Space_Model",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_HistoricalRepublish_PublishAttempt_Tenant",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "PublishAttemptId" },
                principalTable: "Space_PublishAttempt",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_HistoricalRepublish_Validation_Tenant",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ValidationRunId" },
                principalTable: "Space_ValidationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_LocationExternalBinding_Location_Tenant_V_d70ef4f96a64",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "LocationLogicalId" },
                principalTable: "Space_LocationRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_LocationExternalBinding_Source_Tenant_Version",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_LocationExternalBinding_Version_Tenant",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_LocationRevision_Rack_Tenant_Version_Logical",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "RackLogicalId" },
                principalTable: "Space_RackRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_LocationRevision_Space_ModelSource_Tenant_1e2c42d2e8d7",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                principalTable: "Space_ModelSource",
                principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_LocationRevision_Space_ModelVersion_Tenan_49163d86c0c5",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_Model_ActiveDraft_Tenant_Model_Version",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "ActiveDraftVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "ModelId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_Model_CurrentPublished_Tenant_Model_Version",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "CurrentPublishedVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "ModelId", "Id" },
                onDelete: ReferentialAction.Restrict);
            PostgreSqlTokenV1.Install(migrationBuilder, "cp6_storage_space", PostgreSqlSpaceTokenTablesV1.All);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("PostgreSQL v1 baselines are forward-only; restore a verified backup instead of rolling schema back.");
        }
    }
}
