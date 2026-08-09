using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExcelPreflightServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Start_pins_mapping_and_replays_without_touching_model_content()
    {
        await using var fixture = await CreateFixtureAsync();
        var before = fixture.Version.ContentRevision;
        var request = new StartSpaceExcelPreflightRequest(
            SpaceExcelMappingService.SystemStandardProfileId,
            1);

        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            request,
            "preflight-1");
        var replay = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            request,
            "preflight-1");

        Assert.Equal(started.JobId, replay.JobId);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(before, fixture.Version.ContentRevision);
        Assert.Equal("Parsing", started.Source.State);
        Assert.Equal(request.MappingProfileId, started.Source.MappingProfileId);
        Assert.Equal(1, started.Source.MappingProfileVersion);
        Assert.Equal(
            SpaceExcelPreflightJobProcessor.Version,
            started.Source.ParserVersion);
        Assert.Single(await fixture.Context.Jobs.ToListAsync());
        Assert.Single(await fixture.Context.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task Worker_persists_located_issues_and_report_without_cell_values()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            new(
                SpaceExcelMappingService.SystemStandardProfileId,
                1),
            "preflight-worker");
        await RunWorkerAsync(fixture, started.JobId);

        var preview = await fixture.Service.GetAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId,
            200);
        var report = await fixture.Service.OpenErrorReportAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId);
        await using var reportContent = report.Content;
        using var reader = new StreamReader(report.Content);
        var csv = await reader.ReadToEndAsync();

        Assert.Equal("Succeeded", preview.Status);
        Assert.Equal("PreviewReady", preview.SourceState);
        Assert.False(preview.CanConfirm);
        Assert.Equal(5, preview.SheetCount);
        Assert.Equal(1, preview.DataRowCount);
        Assert.Equal(0, preview.ValidRowCount);
        Assert.True(preview.BlockingCount >= 2);
        Assert.Contains(preview.Issues, issue =>
            issue.Code == "SPACE_EXCEL_REQUIRED_VALUE_MISSING" &&
            issue.Sheet == "Racks" && issue.Row == 2 && issue.Column == "A" &&
            issue.TargetField == "FloorCode");
        Assert.Contains(preview.Issues, issue =>
            issue.Code == "SPACE_EXCEL_TYPE_INVALID" &&
            issue.Sheet == "Racks" && issue.Row == 2 && issue.Column == "G" &&
            issue.TargetField == "WidthMm");
        Assert.All(preview.Issues, issue =>
            Assert.DoesNotContain("not-a-number", issue.MessageArgsJson));
        Assert.Contains("Severity,Code,Sheet,Row,Column,TargetField,FixHint", csv);
        Assert.Contains("SPACE_EXCEL_TYPE_INVALID", csv);
        Assert.DoesNotContain("not-a-number", csv);
        Assert.Equal("text/csv; charset=utf-8", report.ContentType);
        Assert.Equal(0, fixture.Version.ContentRevision);
    }

    [Fact]
    public async Task Another_tenant_cannot_read_a_preflight_or_its_report()
    {
        var database = Guid.NewGuid().ToString("N");
        await using var owner = await CreateFixtureAsync(database: database);
        var started = await owner.Service.StartAsync(
            owner.Version.Id,
            owner.Source.Id,
            new(SpaceExcelMappingService.SystemStandardProfileId, 1),
            "tenant-owner");

        await using var other = await CreateFixtureAsync(
            database: database,
            tenantId: Guid.NewGuid());
        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            other.Service.GetAsync(
                owner.Version.Id,
                owner.Source.Id,
                started.JobId,
                100));
        Assert.Equal(SpaceErrorCodes.ExcelPreflightNotFound, error.Code);
    }

    [Fact]
    public async Task Only_the_current_completed_preview_can_be_confirmable()
    {
        await using var fixture = await CreateFixtureAsync();
        var first = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            new(SpaceExcelMappingService.SystemStandardProfileId, 1),
            "current-preview-1");
        await RunWorkerAsync(fixture, first.JobId, ValidSingleRackWorkbook());
        var completed = await fixture.Service.GetAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            first.JobId,
            100);
        Assert.True(completed.CanConfirm);

        await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            new(SpaceExcelMappingService.SystemStandardProfileId, 1),
            "current-preview-2");
        var stale = await fixture.Service.GetAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            first.JobId,
            100);

        Assert.False(stale.CanConfirm);
        Assert.Equal("Parsing", stale.SourceState);
    }

    private static async Task RunWorkerAsync(
        Fixture fixture,
        Guid jobId,
        SpaceExcelWorkbookData? workbook = null)
    {
        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == jobId);
        var attempt = job.Claim(
            "excel-worker",
            SpaceExcelPreflightJobProcessor.Version,
            Now,
            TimeSpan.FromMinutes(5));
        fixture.Context.JobAttempts.Add(attempt);
        await fixture.Context.SaveChangesAsync();
        var lease = new SpaceJobLease(
            fixture.Execution.TenantId,
            job.Id,
            attempt.Id,
            attempt.AttemptNo,
            attempt.WorkerId,
            job.JobType,
            job.SubjectType,
            job.SubjectId,
            job.InputHash,
            job.LockExpiresAtUtc!.Value,
            job.RowVersion);
        var executor = new SpaceExcelPreflightJobStepExecutor(
            fixture.Context,
            new SingleServiceProvider(new EmptyFileStore()),
            new FixedWorkbookReader(workbook ?? InvalidWorkbook()),
            fixture.MappingService,
            new SpaceExcelPreflightValidator());

        var validateStep = SpaceJobStep.Start(
            fixture.Execution.TenantId,
            attempt.Id,
            1,
            SpaceExcelPreflightJobProcessor.ValidateWorkbook,
            Now);
        fixture.Context.JobSteps.Add(validateStep);
        await fixture.Context.SaveChangesAsync();
        var validateOutput = await executor.ExecuteAsync(
            new(
                lease,
                1,
                SpaceExcelPreflightJobProcessor.ValidateWorkbook));
        validateStep.Complete(validateOutput.CheckpointJson, validateOutput.OutputHash, Now);
        await fixture.Context.SaveChangesAsync();

        var persistStep = SpaceJobStep.Start(
            fixture.Execution.TenantId,
            attempt.Id,
            2,
            SpaceExcelPreflightJobProcessor.PersistPreview,
            Now);
        fixture.Context.JobSteps.Add(persistStep);
        await fixture.Context.SaveChangesAsync();
        var persistOutput = await executor.ExecuteAsync(
            new(
                lease,
                2,
                SpaceExcelPreflightJobProcessor.PersistPreview));
        persistStep.Complete(persistOutput.CheckpointJson, persistOutput.OutputHash, Now);
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            attempt.WorkerId,
            Now,
            persistOutput.CheckpointJson);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task<Fixture> CreateFixtureAsync(
        string? database = null,
        Guid? tenantId = null)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        var execution = new TestExecutionContext(tenant, Guid.NewGuid());
        var clock = new FixedClock();
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    database ?? Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            clock);
        var model = SpaceModel.Create(tenant, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            tenant,
            model.Id,
            1,
            "Published");
        published.BeginValidation();
        published.MarkReady(
            new string('a', 64),
            "space-v1",
            new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        var version = SpaceModelVersion.CreateDraft(
            tenant,
            model.Id,
            2,
            "Draft",
            published.Id);
        model.ReserveDraft(version);
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            tenant,
            $"{tenant:N}/{Guid.NewGuid():N}/source.content",
            "warehouse.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SpaceFileRetentionClass.Source);
        file.CompleteQuarantine(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xlsx",
            1024,
            new string('c', 64));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        var source = SpaceModelSource.CreateFileSource(
            tenant,
            version.Id,
            SpaceSourceType.Excel,
            file,
            "warehouse.xlsx");
        context.AddRange(model, published, version, file, source);
        await context.SaveChangesAsync();

        var mappingService = new SpaceExcelMappingService(
            context,
            execution,
            clock);
        var service = new SpaceExcelPreflightService(
            context,
            execution,
            new AllowAccess(),
            null!,
            null!,
            mappingService,
            clock);
        return new Fixture(
            context,
            execution,
            version,
            source,
            mappingService,
            service);
    }

    private static SpaceExcelWorkbookData InvalidWorkbook() =>
        new(SpaceExcelTargetCatalog.Sheets.Select(sheet =>
        {
            var fields = SpaceExcelTargetCatalog.ForSheet(sheet);
            var header = new SpaceExcelWorkbookRow(
                1,
                fields.Select((field, index) => new SpaceExcelWorkbookCell(
                        index + 1,
                        ColumnName(index + 1),
                        field.Field,
                        false))
                    .ToDictionary(cell => cell.ColumnIndex));
            if (sheet != "Racks")
                return new SpaceExcelWorkbookSheet(sheet, [header]);
            var values = new Dictionary<string, string?>
            {
                ["FloorCode"] = null,
                ["ZoneCode"] = "Z1",
                ["RackCode"] = "R1",
                ["XMm"] = "0",
                ["YMm"] = "0",
                ["WidthMm"] = "not-a-number",
                ["DepthMm"] = "100",
                ["HeightMm"] = "200",
                ["LifecycleStatus"] = "Active",
            };
            var row = new SpaceExcelWorkbookRow(
                2,
                fields.Select((field, index) => new SpaceExcelWorkbookCell(
                        index + 1,
                        ColumnName(index + 1),
                        values.GetValueOrDefault(field.Field),
                        false))
                    .ToDictionary(cell => cell.ColumnIndex));
            return new SpaceExcelWorkbookSheet(sheet, [header, row]);
        }).ToArray());

    private static SpaceExcelWorkbookData ValidSingleRackWorkbook()
    {
        var workbook = InvalidWorkbook();
        var racks = workbook.Sheets.Single(sheet => sheet.Name == "Racks");
        var cells = racks.Rows[1].Cells.ToDictionary(
            item => item.Key,
            item => item.Value);
        cells[1] = cells[1] with { Value = "F1" };
        cells[7] = cells[7] with { Value = "100" };
        return workbook with
        {
            Sheets = workbook.Sheets.Select(sheet =>
                sheet.Name == "Racks"
                    ? sheet with
                    {
                        Rows = [sheet.Rows[0], sheet.Rows[1] with { Cells = cells }],
                    }
                    : sheet).ToArray(),
        };
    }

    private static string ColumnName(int index)
    {
        var value = index;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private sealed record Fixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        SpaceModelVersion Version,
        SpaceModelSource Source,
        SpaceExcelMappingService MappingService,
        SpaceExcelPreflightService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class FixedWorkbookReader(SpaceExcelWorkbookData workbook) :
        ISpaceExcelWorkbookReader
    {
        public Task<SpaceExcelWorkbookData> ReadAsync(
            Stream content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(workbook);
    }

    private sealed class EmptyFileStore : ISpaceFileStore
    {
        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SingleServiceProvider(ISpaceFileStore files) :
        IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISpaceFileStore) ? files : null;
    }
}
