using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExcelMappingServiceTests
{
    [Fact]
    public async Task System_profile_freezes_the_standard_workbook_contract()
    {
        await using var fixture = CreateFixture();

        var profile = await fixture.Service.GetProfileAsync(
            SpaceExcelMappingService.SystemStandardProfileId);

        Assert.Equal("System", profile.Scope);
        Assert.True(profile.IsReadOnly);
        Assert.Equal(1, profile.Version);
        Assert.Equal(SpaceExcelTargetCatalog.Sheets, profile.Definition.Sheets
            .Select(sheet => sheet.TargetSheet));
        foreach (var sheet in profile.Definition.Sheets)
        {
            Assert.Equal(sheet.TargetSheet, sheet.SourceSheet);
            Assert.Equal("Exact", sheet.SheetMatchMode);
            Assert.Equal(1, sheet.HeaderRow);
            Assert.Equal(2, sheet.DataStartRow);
            Assert.Equal(
                SpaceExcelTargetCatalog.ForSheet(sheet.TargetSheet)
                    .Select(field => field.Field),
                sheet.Columns.Select(column => column.SourceHeader));
        }
    }

    [Fact]
    public async Task Custom_headers_can_be_previewed_and_saved_as_a_tenant_profile()
    {
        await using var fixture = CreateFixture();
        var system = await fixture.Service.GetProfileAsync(
            SpaceExcelMappingService.SystemStandardProfileId);
        var definition = CustomDefinition(system.Definition, "Warning");
        var workbook = WorkbookFor(definition, includeUnknown: true);

        var preview = fixture.Service.Preview(new(definition, workbook));
        var saved = await fixture.Service.SaveProfileAsync(
            new(
                null,
                "Vendor A warehouse columns",
                preview.NormalizedDefinition,
                CopyFromProfileId: system.Id,
                CopyFromVersion: system.Version),
            "vendor-a-v1");

        Assert.True(preview.CanSave);
        Assert.Equal(5, preview.Sheets.Count);
        Assert.All(
            preview.Sheets.SelectMany(sheet => sheet.Columns),
            column => Assert.Equal("Mapped", column.Status));
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_EXCEL_UNKNOWN_COLUMN" &&
                issue.Severity == "Warning");
        Assert.True(saved.Created);
        Assert.Equal("Tenant", saved.Profile.Scope);
        Assert.Equal(system.Id, saved.Profile.BasedOnProfileId);
        Assert.Equal(1, saved.Profile.BasedOnVersion);
        Assert.Single(await fixture.Context.ExcelMappingProfiles.ToListAsync());
        Assert.Single(await fixture.Context.ExcelMappingProfileVersions.ToListAsync());
    }

    [Fact]
    public async Task Preview_reports_missing_duplicate_and_rejected_unknown_headers()
    {
        await using var fixture = CreateFixture();
        var system = await fixture.Service.GetProfileAsync(
            SpaceExcelMappingService.SystemStandardProfileId);
        var definition = CustomDefinition(system.Definition, "Reject");
        var workbook = WorkbookFor(definition, includeUnknown: true).ToArray();
        var racks = workbook.Single(sample => sample.SheetName == "Upload Racks");
        var required = definition.Sheets.Single(sheet => sheet.TargetSheet == "Racks")
            .Columns.Single(column => column.TargetField == "RackCode")
            .SourceHeader!;
        workbook[Array.IndexOf(workbook, racks)] = racks with
        {
            Headers =
            [
                .. racks.Headers.Where(header => header != required),
                racks.Headers[0],
            ],
        };

        var preview = fixture.Service.Preview(new(definition, workbook));

        Assert.False(preview.CanSave);
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_EXCEL_SOURCE_HEADER_MISSING");
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_EXCEL_SOURCE_HEADER_DUPLICATE");
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_EXCEL_UNKNOWN_COLUMN" &&
                issue.Severity == "Error");
    }

    [Fact]
    public async Task Saved_versions_are_immutable_and_idempotent_replays_do_not_append()
    {
        await using var fixture = CreateFixture();
        var system = await fixture.Service.GetProfileAsync(
            SpaceExcelMappingService.SystemStandardProfileId);
        var createRequest = new SaveSpaceExcelMappingProfileRequest(
            null,
            "Operations import",
            system.Definition,
            CopyFromProfileId: system.Id,
            CopyFromVersion: 1);

        var created = await fixture.Service.SaveProfileAsync(
            createRequest,
            "operations-v1");
        var replay = await fixture.Service.SaveProfileAsync(
            createRequest,
            "operations-v1");
        var updatedDefinition = system.Definition with
        {
            UnknownColumnPolicy = "Ignore",
        };
        var updated = await fixture.Service.SaveProfileAsync(
            new(
                created.Profile.Id,
                "Operations import",
                updatedDefinition,
                created.Profile.RowVersion),
            "operations-v2");

        Assert.True(replay.IdempotentReplay);
        Assert.Equal(created.Profile.Id, replay.Profile.Id);
        Assert.False(updated.Created);
        Assert.Equal(2, updated.Profile.Version);
        Assert.Equal(2, await fixture.Context.ExcelMappingProfileVersions.CountAsync());
        var v1 = await fixture.Service.GetProfileAsync(created.Profile.Id, 1);
        var v2 = await fixture.Service.GetProfileAsync(created.Profile.Id, 2);
        Assert.Equal("Warning", v1.Definition.UnknownColumnPolicy);
        Assert.Equal("Ignore", v2.Definition.UnknownColumnPolicy);

        var storedV1 = await fixture.Context.ExcelMappingProfileVersions
            .SingleAsync(item => item.ProfileId == created.Profile.Id &&
                item.Version == 1);
        fixture.Context.Entry(storedV1).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task System_profile_is_read_only_and_updates_require_concurrency_token()
    {
        await using var fixture = CreateFixture();
        var system = await fixture.Service.GetProfileAsync(
            SpaceExcelMappingService.SystemStandardProfileId);
        var readOnly = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.SaveProfileAsync(
                new(system.Id, "system", system.Definition),
                "edit-system"));
        Assert.Equal(SpaceErrorCodes.ExcelMappingProfileReadOnly, readOnly.Code);

        var created = await fixture.Service.SaveProfileAsync(
            new(null, "private", system.Definition),
            "private-v1");
        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.SaveProfileAsync(
                new(created.Profile.Id, "private", system.Definition),
                "private-v2"));
        Assert.Equal(SpaceErrorCodes.ExcelMappingProfileConflict, conflict.Code);
    }

    [Fact]
    public async Task Tenant_profiles_and_versions_are_filtered_by_tenant()
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var fixtureA = CreateFixture(database, tenantA);
        var standard = await fixtureA.Service.GetProfileAsync(
            SpaceExcelMappingService.SystemStandardProfileId);
        var saved = await fixtureA.Service.SaveProfileAsync(
            new(null, "Tenant A", standard.Definition),
            "tenant-a-v1");

        await using var fixtureB = CreateFixture(database, tenantB);
        var profiles = await fixtureB.Service.GetProfilesAsync();
        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixtureB.Service.GetProfileAsync(saved.Profile.Id));

        Assert.Single(profiles);
        Assert.Equal("System", profiles[0].Scope);
        Assert.Equal(SpaceErrorCodes.ExcelMappingProfileNotFound, error.Code);
        Assert.Empty(await fixtureB.Context.ExcelMappingProfileVersions.ToListAsync());
        Assert.Equal(
            1,
            await fixtureB.Context.ExcelMappingProfileVersions
                .IgnoreQueryFilters()
                .CountAsync());
    }

    [Fact]
    public async Task Ef_model_freezes_profile_versioning_and_tenant_relationships()
    {
        await using var fixture = CreateFixture();
        var profile = fixture.Context.Model.FindEntityType(
            typeof(SpaceExcelMappingProfile))!;
        var version = fixture.Context.Model.FindEntityType(
            typeof(SpaceExcelMappingProfileVersion))!;

        Assert.Equal("Space_ExcelMappingProfile", profile.GetTableName());
        Assert.Equal("Space_ExcelMappingProfileVersion", version.GetTableName());
        Assert.True(profile.FindProperty(nameof(SpaceExcelMappingProfile.RowVersion))!
            .IsConcurrencyToken);
        Assert.NotNull(profile.GetQueryFilter());
        Assert.NotNull(version.GetQueryFilter());
        Assert.Contains(
            version.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["TenantId", "ProfileId"]));
        Assert.Contains(
            version.GetIndexes(),
            index => index.IsUnique && index.Properties
                .Select(property => property.Name)
                .SequenceEqual(["TenantId", "ProfileId", "Version"]));
    }

    private static SpaceExcelMappingDefinitionDto CustomDefinition(
        SpaceExcelMappingDefinitionDto standard,
        string unknownColumnPolicy) =>
        standard with
        {
            UnknownColumnPolicy = unknownColumnPolicy,
            Sheets = standard.Sheets.Select(sheet => sheet with
            {
                SourceSheet = $"Upload {sheet.TargetSheet}",
                Columns = sheet.Columns.Select(column => column with
                {
                    SourceHeader = $"src_{column.TargetField}",
                    SourceColumn = null,
                }).ToArray(),
            }).ToArray(),
        };

    private static IReadOnlyList<SpaceExcelHeaderSampleDto> WorkbookFor(
        SpaceExcelMappingDefinitionDto definition,
        bool includeUnknown) =>
        definition.Sheets.Select((sheet, index) =>
            new SpaceExcelHeaderSampleDto(
                sheet.SourceSheet,
                [
                    .. sheet.Columns.Select(column => column.SourceHeader!),
                    .. (includeUnknown && index == 0
                        ? new[] { "vendor_note" }
                        : Array.Empty<string>()),
                ])).ToArray();

    private static Fixture CreateFixture(
        string? database = null,
        Guid? tenantId = null)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        var execution = new TestExecutionContext(tenant, Guid.NewGuid());
        var clock = new FixedClock(
            new DateTime(2026, 8, 2, 14, 0, 0, DateTimeKind.Utc));
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    database ?? Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            clock);
        return new Fixture(
            context,
            new SpaceExcelMappingService(context, execution, clock));
    }

    private sealed record Fixture(
        SpaceContext Context,
        SpaceExcelMappingService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock(DateTime now) : ISpaceClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
