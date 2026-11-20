using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static object ExportQuery(string legal = "JP-A", string? name = null) => new
    {
        filter = new
        {
            legalEntityKey = legal, rolesAny = Array.Empty<string>(), registrations = Array.Empty<string>(),
            lifecycles = Array.Empty<string>(), holds = Array.Empty<string>(), bpCd = (string?)null, name,
            baseCds = Array.Empty<string>(), salesStaffCd = (string?)null, businessStaffCd = (string?)null,
            classFilters = Array.Empty<object>(), createdFrom = (string?)null, createdTo = (string?)null
        },
        sort = "BP_CD", direction = "ASC", page = 2, pageSize = 1
    };

    // 大量行用同一份正式建档衍生的模拟根和有效前沿填充。加密历史不作为这些派生行的验收输入。
    private static async Task<Guid[]> CopyWorkspaceRows(Harness h, Guid template, string prefix, string[] names, int skip = 0)
    {
        var originalTimeout = h.Db.Database.GetCommandTimeout();
        h.Db.Database.SetCommandTimeout(TimeSpan.FromMinutes(4));
        try { return await InsertWorkspaceSamples(h, template, prefix, names, skip); }
        finally { h.Db.Database.SetCommandTimeout(originalTimeout); }
    }

    private static async Task<Guid[]> InsertWorkspaceSamples(Harness h, Guid template, string prefix, string[] names, int skip)
    {
        var normalized = names.Select(name => BusinessPartnerUnicode15.Normalize(name.TrimEnd())).ToArray();
        await using var transaction = await h.Db.Database.BeginTransactionAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            CREATE TEMP TABLE wp0438_sample ON COMMIT DROP AS
            SELECT gen_random_uuid() AS id, j, ({prefix} || lpad(({skip} + j)::text,5,'0')) AS code,
                   ({names})[j] AS name, ({normalized})[j] AS normalized
            FROM generate_subscripts({names},1) AS j;
            INSERT INTO public."T_WebBusinessPartner"
            SELECT (jsonb_populate_record(NULL::public."T_WebBusinessPartner",
                    to_jsonb(source) || jsonb_build_object('Id',sample.id,'BpCd',sample.code,'BpName',sample.name))).*
            FROM public."T_WebBusinessPartner" source CROSS JOIN wp0438_sample sample
            WHERE source."TenantId"={h.Tenant} AND source."Id"={template};
            INSERT INTO main_bp.heads
                SELECT source.tenant_id,sample.id,source.legal_entity_key,source.registration,source.revision
                FROM main_bp.heads source CROSS JOIN wp0438_sample sample
                WHERE source.tenant_id={h.Tenant} AND source.bp_id={template};
            INSERT INTO main_bp.profile_revisions
                SELECT head.tenant_id,head.legal_entity_key,head.bp_id,head.revision,root."RowVersion",
                       encode(sha256(convert_to(sample.code,'UTF8')),'hex'),'\x00'::bytea,current_timestamp
                FROM wp0438_sample sample JOIN main_bp.heads head ON head.tenant_id={h.Tenant} AND head.bp_id=sample.id
                JOIN public."T_WebBusinessPartner" root ON root."TenantId"=head.tenant_id AND root."Id"=head.bp_id;
            INSERT INTO main_bp.normalized_identity(tenant_id,legal_entity_key,bp_id,norm_version,revision,name_norm,name_hash)
                SELECT head.tenant_id,head.legal_entity_key,head.bp_id,'BP-NORM-U15.0-1',head.revision,sample.normalized,
                       sha256(convert_to(sample.normalized,'UTF8'))
                FROM wp0438_sample sample JOIN main_bp.heads head ON head.tenant_id={h.Tenant} AND head.bp_id=sample.id;
            """);
        var ids = await h.Db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM wp0438_sample ORDER BY j").ToArrayAsync();
        await transaction.CommitAsync();
        return ids;
    }

    private static async Task<HttpResponseMessage> PartnerExport(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        object input)
    {
        using var request = OperationsRequest(cookies, HttpMethod.Post, FormalPartnerApi + "/export", input);
        return await host.Send(request);
    }

    private static string[][] CsvRecords(byte[] bytes)
    {
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        using var parser = new TextFieldParser(new MemoryStream(bytes)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var records = new List<string[]>();
        while (!parser.EndOfData) records.Add(parser.ReadFields()!);
        return records.ToArray();
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_export_uses_formal_query_and_current_fields()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, alternate: true);
        await GrantPartnerActions(h, "export");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var preview = await WorkspacePreview(host, cookies, WorkspaceInput(options, "CSV00001", "=镜像,\"甲\"",
            overrides: new() { ["tel"] = null }));
        var id = PartnerId(await ApplyWorkspace(host, cookies, preview, Guid.NewGuid()));
        const string hidden = "+81-12-3456";
        var sensitiveEdit = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ProfilePatch(id, "1", "tel", hidden));
        await ApplyWorkspace(host, cookies, sensitiveEdit, Guid.NewGuid(), path: "/profile-apply");
        var candidateDraft = WorkspaceInput(options, "CSV-CAND", "=镜像,\"甲\"");
        var openCandidate = await WorkspacePreview(host, cookies, candidateDraft);
        Assert.Equal(id, Assert.Single(openCandidate.Body.GetProperty("candidates").GetProperty("items").EnumerateArray())
            .GetProperty("reference").GetProperty("bpId").GetGuid());
        var before = await FormalCounts(h);

        using var response = await PartnerExport(host, cookies, ExportQuery());
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var version = response.Headers.GetValues("X-BP-Policy-Version").Single();
        Assert.True(DateTimeOffset.TryParse(response.Headers.GetValues("X-BP-As-Of").Single(), out var asOf));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("CSV00001,\"'=镜像,\"\"甲\"\"\"", csv);
        Assert.Contains("'+81-12-3456", csv);
        Assert.EndsWith("\r\n", csv);

        var historyInput = new { legalEntityKey = "JP-A", bpId = id, kind = "PROFILE", beforeSequence = (string?)null, limit = 50 };
        var visibleHistory = await WorkspaceCall(host, cookies, "/history", HttpMethod.Post, historyInput);
        Assert.Contains(visibleHistory.Body.GetProperty("items")[0].GetProperty("changes").EnumerateArray(),
            item => item.GetProperty("field").GetString() == "tel" && item.GetProperty("value").GetString() == hidden);
        foreach (var field in new[] { "Tel", "Ein", "PaidEachTimeFlg" })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = PartnerResource, FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync();
        using var masked = await PartnerExport(host, cookies, ExportQuery());
        Assert.Equal(HttpStatusCode.OK, masked.StatusCode);
        var maskedCsv = await masked.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Tel", maskedCsv.Split("\r\n")[0]);
        Assert.DoesNotContain(hidden, maskedCsv);
        Assert.NotEqual(version, masked.Headers.GetValues("X-BP-Policy-Version").Single());
        var history = await WorkspaceCall(host, cookies, "/history", HttpMethod.Post, historyInput);
        Assert.DoesNotContain(hidden, history.Body.GetRawText());
        var record = history.Body.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, record.GetProperty("reference").GetProperty("digest").ValueKind);
        Assert.Contains(record.GetProperty("redactedFields").EnumerateArray(), field => field.GetString() == "tel");
        var detail = await WorkspaceCall(host, cookies, $"/{id:D}");
        Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("profile").GetProperty("paidEachTimeFlg").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("bp").GetProperty("contentHash").ValueKind);
        Assert.Contains(detail.Body.GetProperty("redactedFields").EnumerateArray(), field => field.GetString() == "paidEachTimeFlg");
        var maskedEdit = ProfilePatch(id, CurrentRevision(detail), "paidEachTimeFlg", null);
        var denied = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, maskedEdit, status: 403);
        Assert.DoesNotContain(hidden, denied.Body.GetRawText());
        await h.Db.Sys_RoleFieldPerms.Where(p => p.FieldName == "PaidEachTimeFlg").ExecuteDeleteAsync();
        var invalid = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, maskedEdit, status: 400);
        Assert.Equal("FIELD_INVALID", invalid.Body.GetProperty("code").GetString());
        var candidate = await WorkspacePreview(host, cookies, candidateDraft, 403);
        Assert.False(candidate.Body.TryGetProperty("candidates", out _));
        Assert.DoesNotContain(hidden, candidate.Body.GetRawText());
        var audit = await h.Db.Sys_OperLogs.AsNoTracking().Where(x => x.RequestUrl == "api/main/v1/bp/export")
            .Select(x => x.AuditEvidenceJson).ToArrayAsync();
        Assert.Equal(2, audit.Length);
        Assert.All(audit, item => Assert.DoesNotContain(hidden, item));
        var evidence = audit.Select(item => JsonDocument.Parse(item!).RootElement.Clone()).ToArray();
        var first = Assert.Single(evidence, item => item.GetProperty("permissionVersion").GetString() == version);
        Assert.Equal(asOf, first.GetProperty("snapshotAsOfUtc").GetDateTimeOffset());
        Assert.Equal(before, await FormalCounts(h));
        await DeviceEvidence("wp04-38-b03-fields.json", new
        {
            status = (int)response.StatusCode, columns = CsvRecords(bytes)[0], asOf, version,
            historyMasked = true, candidateWithoutRaw = true, errorWithoutRaw = true, logWithSnapshot = true,
            hiddenBoolean = "null", sameNullAfterGrant = (int)invalid.Status, partnerCounts = before
        });
    }

    private static object ProfilePatch(Guid bp, string revision, string field, object? value) => new
    {
        bpId = bp, expectedRevision = revision, changes = new[] { new { field, value } },
        relationChanges = Array.Empty<object>(), financeLinkChanges = Array.Empty<object>(), reason = "模拟资料维护"
    };

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_export_checks_complete_authorized_5000_and_refuses_5001()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, alternate: true);
        await GrantPartnerActions(h, "export");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var preview = await WorkspacePreview(host, cookies, WorkspaceInput(options, "EX00000", "导出组 起点"));
        var id = PartnerId(await ApplyWorkspace(host, cookies, preview, Guid.NewGuid()));
        var otherOptions = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-B");
        var other = await WorkspacePreview(host, cookies, WorkspaceInput(otherOptions, "EXPORT-B", "导出组 另一法人",
            legal: "JP-B", site: "BPB"));
        await ApplyWorkspace(host, cookies, other, Guid.NewGuid());
        var names = new[] { "=导出组", "+导出组", "-导出组", "@导出组", "\t导出组", "导出组,\"双引号\"\r\n第二行" }
            .Concat(Enumerable.Repeat("导出组 模拟行", 4993)).ToArray();
        await CopyWorkspaceRows(h, id, "EX", names);
        await CopyWorkspaceRows(h, id, "OUT", ["其他筛选"]);
        var before = await FormalCounts(h);
        var query = ExportQuery(name: "导出组");
        using var response = await PartnerExport(host, cookies, query);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var csv = CsvRecords(bytes);
        Assert.Equal(5001, csv.Length);
        Assert.Equal(new[] { "BpCd", "BpName", "BaseCd", "LegalEntity" }.Concat(WorkspaceRoles)
            .Concat(new[] { "Registration", "Lifecycle", "Hold", "Revision", "CreatedAt" }), csv[0].Take(22));
        Assert.Equal(Enumerable.Range(0, 5000).Select(i => "EX" + i.ToString("D5")), csv.Skip(1).Select(row => row[0]));
        Assert.All(csv.Skip(1), row => { Assert.Equal(csv[0].Length, row.Length); Assert.Equal("JP-A", row[3]); });
        Assert.Equal(names.Take(5).Select(name => "'" + name), csv.Skip(2).Take(5).Select(row => row[1]));
        Assert.Equal(names[5], csv[7][1]);
        Assert.EndsWith("\r\n", Encoding.UTF8.GetString(bytes));
        using var across = await PartnerExport(host, cookies, ExportQuery("JP-B", "导出组"));
        Assert.Equal("EXPORT-B", CsvRecords(await across.Content.ReadAsByteArrayAsync())[1][0]);
        using var legacy = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/export-csv"));
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.DoesNotContain("EX00000", await legacy.Content.ReadAsStringAsync());
        Assert.DoesNotContain("EXPORT-B", await legacy.Content.ReadAsStringAsync());
        Assert.Equal(before, await FormalCounts(h));
        await CopyWorkspaceRows(h, id, "EX", ["导出组 超限"], 4999);
        var beforeLimit = await FormalCounts(h);
        using var limit = await PartnerExport(host, cookies, query);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, limit.StatusCode);
        Assert.Equal("EXPORT_LIMIT", (await FormalPartnerJson(limit)).GetProperty("code").GetString());
        Assert.Null(limit.Content.Headers.ContentDisposition);
        Assert.False(limit.Headers.Contains("X-BP-As-Of"));
        Assert.Equal(beforeLimit, await FormalCounts(h));
        await DeviceEvidence("wp04-38-b03-boundary.json", new
        {
            records = csv.Length - 1, first = csv[1][0], last = csv[^1][0], escaped = csv.Skip(2).Take(6).Select(row => row[1]),
            bytes = bytes.Length, limit = (int)limit.StatusCode, noAttachment = true, otherLegal = "JP-B", legacyIsolated = true,
            partnerCounts = before, limitPartnerCounts = beforeLimit
        });
    }

    [PartnerWorkspaceBrowserFact]
    public async Task Business_partner_wp04_38_browser_downloads_filtered_pages_and_displays_limit()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, alternate: true);
        await GrantPartnerActions(h, "export");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var first = await WorkspacePreview(host, cookies, WorkspaceInput(options, "WEB00000", "浏览器导出"));
        var id = PartnerId(await ApplyWorkspace(host, cookies, first, Guid.NewGuid()));
        await CopyWorkspaceRows(h, id, "WEB", Enumerable.Repeat("浏览器导出模拟", 25).ToArray());
        await CopyWorkspaceRows(h, id, "OTHER", ["排除的测试行"]);
        options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-B");
        var alternate = await WorkspacePreview(host, cookies, WorkspaceInput(options, "WEB-B", "浏览器另一法人",
            legal: "JP-B", site: "BPB"));
        var second = PartnerId(await ApplyWorkspace(host, cookies, alternate, Guid.NewGuid()));
        await CopyWorkspaceRows(h, second, "WEBB", Enumerable.Repeat("浏览器超限模拟", 5000).ToArray());
        var before = await FormalCounts(h);
        var folder = Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("CP6_BP_BROWSER_NODE")!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("CP6_BP_BROWSER_SCRIPT")!);
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new
        {
            api = host.Origin.ToString(), dist = Environment.GetEnvironmentVariable("CP6_BP_BROWSER_DIST"), evidence = folder,
            user = "price-owner", password = "Price1!Password", tenant = h.Tenant.ToString("N")
        }));
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        Assert.True(process.ExitCode == 0, (await stderr) + "\n" + (await stdout));
        using var proof = JsonDocument.Parse(await stdout);
        Assert.True(proof.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(before, await FormalCounts(h));
        await DeviceEvidence("wp04-38-b03-vue.json", proof.RootElement.Clone());
    }
}
