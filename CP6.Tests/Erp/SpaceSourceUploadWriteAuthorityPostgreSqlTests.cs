using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClosedXML.Excel;
using CP6.Core.Services.Platform;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("cad", "business-first")]
    [InlineData("cad", "pause-first")]
    [InlineData("cad", "permission-first")]
    [InlineData("excel", "business-first")]
    [InlineData("excel", "pause-first")]
    [InlineData("excel", "permission-first")]
    public async Task PGT017_space_source_upload_and_revocation_share_current_authority(string kind, string order)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        var menuId = await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, _, _) = await SeedSpaceDesignDraft(space, execution, site);
        var input = SourceUploadSample.For(kind, version.Id);
        var fileRoot = NewUnderlayFileRoot();
        await using var host = await StartSourceUploadHost(connection, writer.Tenant, site, fileRoot);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var before = await UnderlayWriteSet(space, fileRoot);

        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        var businessFirst = order == "business-first";
        var permissionFirst = order == "permission-first";
        var table = businessFirst ? "Space_ModelSource" : "Space_File";
        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage>? pause = null;
        FenceWait uploadWait;
        FenceWait? pauseWait = null;
        await using (var blocked = await HoldUnderlayTable(business, table))
        {
            if (permissionFirst)
            {
                await using var revoking = await control.BeginTransactionAsync();
                await using var remove = new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire();
                    DELETE FROM public."Sys_RoleAction"
                    WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"=@action;
                    """, control, revoking);
                remove.Parameters.AddWithValue("tenant", writer.Tenant);
                remove.Parameters.AddWithValue("menu", menuId);
                remove.Parameters.AddWithValue("action", SpaceSourceUploadAction);
                Assert.Equal(1, await remove.ExecuteNonQueryAsync());
                pending = SendSourceUpload(host, actor, input);
                uploadWait = await SerialNativeWait(observer, control.ProcessID, business.ProcessID,
                    $"space-{kind}-upload-permission", pending);
                await revoking.CommitAsync();
            }
            else
            {
                await using var tenant = await HoldUnderlayTenant(control, writer.Tenant);
                if (businessFirst)
                {
                    pending = SendSourceUpload(host, actor, input);
                    uploadWait = await WaitForFence(observer, business.ProcessID, $"%{table}%", null, pending);
                    pause = Pause();
                    pauseWait = await SerialNativeWait(observer, uploadWait.Backend, control.ProcessID,
                        $"space-{kind}-upload-business-pause", pause);
                }
                else
                {
                    pause = Pause();
                    pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                    pending = SendSourceUpload(host, actor, input);
                    uploadWait = await SerialNativeWait(observer, pauseWait.Backend, business.ProcessID,
                        $"space-{kind}-upload-pause", pending);
                }
                await tenant.RollbackAsync();
                if (pauseWait.Blocker == control.ProcessID)
                    await pause;
            }
            await blocked.RollbackAsync();
        }

        using var result = await pending;
        using var paused = pause is null ? null : await pause;
        var after = await UnderlayWriteSet(space, fileRoot);
        var expected = businessFirst ? HttpStatusCode.Accepted : permissionFirst ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        await DeviceEvidence($"space-{kind}-upload-{order}.json", new
        {
            UploadWait = uploadWait, PauseWait = pauseWait, Expected = (int)expected, Actual = (int)result.StatusCode,
            Before = before, After = after,
        });

        Assert.Equal(expected, result.StatusCode);
        SourceUploadIds? first = null;
        if (businessFirst)
        {
            Assert.Equal(uploadWait.Backend, pauseWait!.Blocker);
            first = await CheckSourceUpload(space, fileRoot, input, version.ContentRevision, result, reused: false);
        }
        else
        {
            Assert.Equal(permissionFirst ? control.ProcessID : pauseWait!.Backend, uploadWait.Blocker);
            Assert.Equal(before, after);
        }

        if (permissionFirst)
        {
            Assert.True(await writer.Db.Sys_RoleActions.AsNoTracking().AnyAsync(x =>
                x.TenantId == writer.Tenant && x.RoleId == 72 && x.MenuId == menuId && x.ActionCode == "model:edit"));
            await GrantSpaceDesignActions(writer, SpaceSourceUploadAction);
        }
        else
        {
            using var obsolete = await SendSourceUpload(host, actor, input);
            Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
            await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused!);
            actor = await Login(host, writer);
        }

        using var recovered = await SendSourceUpload(host, actor, input);
        var committed = await CheckSourceUpload(space, fileRoot, input, version.ContentRevision, recovered, reused: businessFirst);
        if (first is not null)
            Assert.Equal(first, committed);
        var committedSet = await UnderlayWriteSet(space, fileRoot);
        using var replay = await SendSourceUpload(host, actor, input, alternateName: true);
        Assert.Equal(committed, await CheckSourceUpload(space, fileRoot, input, version.ContentRevision, replay, reused: true));
        Assert.Equal(committedSet, await UnderlayWriteSet(space, fileRoot));

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    [TenantManagementPostgresTheory]
    [InlineData("cad")]
    [InlineData("excel")]
    public async Task PGT017_space_source_upload_recovers_catalog_when_registration_loses_authority(string kind)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, _, _) = await SeedSpaceDesignDraft(space, execution, site);
        var input = SourceUploadSample.For(kind, version.Id);
        var fileRoot = NewUnderlayFileRoot();
        await using var host = await StartSourceUploadHost(connection, writer.Tenant, site, fileRoot);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        await using var fileOwner = new NpgsqlConnection(connection);
        await using var sourceOwner = new NpgsqlConnection(connection);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await fileOwner.OpenAsync();
        await sourceOwner.OpenAsync();
        await control.OpenAsync();
        await observer.OpenAsync();

        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage> pause;
        FenceWait fileWait, pauseWait, sourceWait;
        int publishedBeforeCatalog;
        long[] stagedLengths;
        string catalogWriteSet;
        Guid fileId, scanId;
        await using (var blockedSource = await HoldUnderlayTable(sourceOwner, "Space_ModelSource"))
        await using (var tenant = await HoldUnderlayTenant(control, writer.Tenant))
        await using (var blockedFile = await HoldUnderlayTable(fileOwner, "Space_File"))
        {
            pending = SendSourceUpload(host, actor, input);
            fileWait = await WaitForFence(observer, fileOwner.ProcessID, "%Space_File%", null, pending);
            publishedBeforeCatalog = UnderlayFiles(fileRoot, "*.content").Length;
            stagedLengths = UnderlayFiles(fileRoot, "*.upload").Select(file => new FileInfo(file).Length).ToArray();
            pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
                new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
            pauseWait = await SerialNativeWait(observer, fileWait.Backend, control.ProcessID, $"space-{kind}-upload-file-pause", pause);
            await blockedFile.RollbackAsync();

            var heldPause = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            sourceWait = await SerialNativeWait(observer, heldPause.Backend, sourceOwner.ProcessID,
                $"space-{kind}-upload-source", pending);
            catalogWriteSet = await UnderlayWriteSet(space, fileRoot);
            (fileId, scanId) = await CheckSourceCatalog(space, fileRoot, input);
            Assert.Empty(await space.Sources.AsNoTracking().ToArrayAsync());
            var draft = await space.Versions.AsNoTracking().SingleAsync(item => item.Id == version.Id);
            Assert.Equal(version.ContentRevision, draft.ContentRevision);
            await tenant.RollbackAsync();
            await pause;
            await blockedSource.RollbackAsync();
        }

        using var denied = await pending;
        using var paused = await pause;
        var deniedWriteSet = await UnderlayWriteSet(space, fileRoot);
        await DeviceEvidence($"space-{kind}-upload-interphase.json", new
        {
            FileWait = fileWait, PauseWait = pauseWait, SourceWait = sourceWait, PublishedBeforeCatalog = publishedBeforeCatalog,
            StagedLengths = stagedLengths, Expected = 401, Actual = (int)denied.StatusCode, Catalog = catalogWriteSet, Denied = deniedWriteSet,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(0, publishedBeforeCatalog);
        Assert.Equal(input.Bytes.LongLength, Assert.Single(stagedLengths));
        Assert.Equal(fileWait.Backend, pauseWait.Blocker);
        Assert.Equal(pauseWait.Backend, sourceWait.Blocker);
        Assert.Equal(catalogWriteSet, deniedWriteSet);

        await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused);
        actor = await Login(host, writer);
        using var recovered = await SendSourceUpload(host, actor, input, alternateName: true);
        var committed = await CheckSourceUpload(space, fileRoot, input, version.ContentRevision, recovered, reused: true);
        Assert.Equal(fileId, committed.File);
        Assert.Equal(scanId, committed.ScanJob);
        var committedSet = await UnderlayWriteSet(space, fileRoot);
        using var replay = await SendSourceUpload(host, actor, input);
        Assert.Equal(committed, await CheckSourceUpload(space, fileRoot, input, version.ContentRevision, replay, reused: true));
        Assert.Equal(committedSet, await UnderlayWriteSet(space, fileRoot));
    }

    private static Task<AuthenticationApiProcess> StartSourceUploadHost(string connection, Guid tenant, Guid site, string fileRoot)
    {
        var settings = SpaceDesignSettings(tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_SOURCE_UPLOAD_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(connection, settings);
    }

    private static async Task ResumeSourceUploadTenant(AuthenticationApiProcess host, Dictionary<string, string> manager,
        Guid tenant, HttpResponseMessage paused)
    {
        var suspended = await NumberHttpData<TenantManagementReceiptV1>(paused);
        Assert.False(suspended.Snapshot.Enable);
        using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), suspended.Snapshot.Revision, "reactivate", null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(resumed)).Snapshot.Enable);
    }

    private static async Task<HttpResponseMessage> SendSourceUpload(AuthenticationApiProcess host, Dictionary<string, string> actor,
        SourceUploadSample input, bool alternateName = false)
    {
        using var message = OperationsRequest(actor, HttpMethod.Post, input.Path);
        var form = new MultipartFormDataContent();
        if (input.SourceType == SpaceSourceType.Dxf)
            form.Add(new StringContent("Dxf"), "SourceFormat");
        var content = new ByteArrayContent(input.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(input.ContentType);
        form.Add(content, "File", alternateName ? "alternate" + Path.GetExtension(input.Name) : input.Name);
        message.Content = form;
        return await host.Send(message);
    }

    private static async Task<(Guid File, Guid ScanJob)> CheckSourceCatalog(SpaceContext space, string fileRoot, SourceUploadSample input)
    {
        var file = await space.Files.AsNoTracking().SingleAsync();
        var scan = await space.Jobs.AsNoTracking().SingleAsync();
        Assert.Equal(SpaceFileState.Quarantined, file.State);
        Assert.Equal(input.Name, file.OriginalName);
        Assert.Equal(input.Sha256, file.Sha256);
        Assert.Equal(input.Bytes.LongLength, file.SizeBytes);
        Assert.Equal(SpaceJobType.FileScan, scan.JobType);
        Assert.Equal(SpaceJobStatus.Queued, scan.Status);
        Assert.Equal(file.Id, scan.SubjectId);
        Assert.Equal(file.Sha256, scan.InputHash);
        Assert.Equal(input.Bytes, await File.ReadAllBytesAsync(Assert.Single(UnderlayFiles(fileRoot, "*.content"))));
        Assert.Empty(UnderlayFiles(fileRoot, "*.upload"));
        return (file.Id, scan.Id);
    }

    private static async Task<SourceUploadIds> CheckSourceUpload(SpaceContext space, string fileRoot, SourceUploadSample input,
        long originalRevision, HttpResponseMessage result, bool reused)
    {
        Assert.True(result.StatusCode == HttpStatusCode.Accepted,
            result.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(result));
        var (fileId, scanId) = await CheckSourceCatalog(space, fileRoot, input);
        var source = await space.Sources.AsNoTracking().SingleAsync();
        var version = await space.Versions.AsNoTracking().SingleAsync(item => item.Id == input.VersionId);
        Assert.Equal(SpaceVersionStatus.Draft, version.Status);
        Assert.Equal(originalRevision + 1, version.ContentRevision);
        Assert.Equal(SpaceSourceState.Scanning, source.State);
        Assert.Equal(input.SourceType, source.SourceType);
        Assert.Equal(input.Sha256, source.Sha256);
        Assert.Equal(input.Name, source.DisplayName);
        Assert.Equal(version.Id, source.ModelVersionId);
        Assert.Equal(fileId, source.FileId);

        var (fileDto, sourceDto, scanJobId, status, responseReused) = input.SourceType == SpaceSourceType.Dxf
            ? Assert.IsType<UploadSpaceCadSourceResponse>(await result.Content.ReadFromJsonAsync<UploadSpaceCadSourceResponse>())
            : AsCadShape(Assert.IsType<UploadSpaceExcelSourceResponse>(await result.Content.ReadFromJsonAsync<UploadSpaceExcelSourceResponse>()));
        Assert.Equal(fileId, fileDto.Id);
        Assert.Equal(input.Name, fileDto.OriginalName);
        Assert.Equal(input.Sha256, fileDto.Sha256);
        Assert.Equal(source.Id, sourceDto.Id);
        Assert.Equal(input.Name, sourceDto.DisplayName);
        Assert.Equal(scanId, scanJobId);
        Assert.Equal($"/{SpaceDesignPath}/jobs/{scanId:D}", status);
        Assert.Equal(status, result.Headers.Location?.OriginalString);
        Assert.Equal(reused, responseReused);
        return new SourceUploadIds(fileId, source.Id, scanId);

        static UploadSpaceCadSourceResponse AsCadShape(UploadSpaceExcelSourceResponse excel) =>
            new(excel.File, excel.Source, excel.ScanJobId, excel.JobStatusUrl, excel.Reused);
    }

    private sealed record SourceUploadIds(Guid File, Guid Source, Guid ScanJob);

    private sealed record SourceUploadSample(Guid VersionId, string Path, string Name, string ContentType, SpaceSourceType SourceType, byte[] Bytes)
    {
        internal string Sha256 => Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();

        internal static SourceUploadSample For(string kind, Guid versionId)
        {
            var path = $"{SpaceDesignPath}/versions/{versionId:D}/{kind}-sources";
            if (kind == "cad")
            {
                var dxf = "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n"u8.ToArray();
                return new(versionId, path, "survey.dxf", "application/dxf", SpaceSourceType.Dxf, dxf);
            }
            using var workbook = new XLWorkbook();
            workbook.AddWorksheet("Locations").Cell("A1").Value = "LocationCode";
            using var file = new MemoryStream();
            workbook.SaveAs(file);
            return new(versionId, path, "survey.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", SpaceSourceType.Excel, file.ToArray());
        }
    }
}
