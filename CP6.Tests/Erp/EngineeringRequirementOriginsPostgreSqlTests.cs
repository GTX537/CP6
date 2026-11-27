using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string RequirementPath = "/api/engineering-requirements/owner-v1";

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_exact_doc_original_http_and_current_disclosure()
    {
        await using var h = await Seed();
        await GrantRequirement(h);
        var files = new ContentStore();
        var document = await DocumentInput(h, files);
        var originalText = "SIMULATED 客户规格，强度至少 10.00";
        var bytes = Encoding.UTF8.GetBytes(originalText + "\r\nSIMULATED 防潮条款");
        var original = document.Body.Contents[0];
        var fileOwner = ContentOwner(h, files, new ContentInspector());
        var upload = new FileUploadV1(Guid.NewGuid(), Guid.NewGuid(), document.Body.RevisionOrigin,
            "SIMULATED 规格.txt", bytes.Length, FileContentBytesV1.Digest(bytes), "SIMULATED 原始客户规格");
        await fileOwner.UploadAsync(upload, new MemoryStream(bytes));
        original = original with { File = (await fileOwner.ReadAsync(upload.FileId)).ContentRef };
        document = document with { Body = document.Body with { Contents = [original] } };
        var docOwner = Documents(h, files);
        await docOwner.CommandAsync(document);
        var doc = await docOwner.ReadAsync(document.DocumentId);

        var root = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "wp05-13-actual-files");
        var localFiles = new LocalFileStore(root);
        foreach (var entry in files.Bytes)
            await localFiles.SaveAsync(new MemoryStream(entry.Value), entry.Key);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["Storage__Provider"] = "Local", ["Storage__LocalRoot"] = root });
        var session = await Login(host, h);
        var template = await NewRequirement(Requirements(h), SourceBody());
        using (var context = await host.Send(OperationsRequest(session, HttpMethod.Get, RequirementPath + "/creation-context")))
            template = template with { ExpectedAuthorityVersion = (await DocumentHttpData<EngineeringRevisionCreationContextV1>(context)).AuthorityVersion };
        var body = RequirementNode(template);
        body["body"]!["name"] = "SIMULATED 受限来源";
        body["body"]!["source"] = RequirementNode(new
        {
            kind = "OWNER_ORIGINAL",
            originDescription = "SIMULATED DOC 原件",
            sourceLabel = "SIMULATED 规格",
            acquisitionBasis = "SIMULATED 目录取得",
            clauses = new[] { new { id = "strength", originalText, locator = "LINE:1" } },
            origin = new { reference = doc.Reference, occurrenceId = original.OccurrenceId }
        });

        var wrongDigest = body.DeepClone();
        wrongDigest["body"]!["source"]!["origin"]!["reference"]!["digest"] = new string('0', 64);
        using (var denied = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", wrongDigest)))
            Assert.Equal(HttpStatusCode.PreconditionFailed, denied.StatusCode);

        var wrongVersion = body.DeepClone();
        wrongVersion["body"]!["source"]!["origin"]!["reference"]!["version"] = "7";
        using (var denied = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", wrongVersion)))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        var forged = body.DeepClone();
        forged["body"]!["source"]!["clauses"]![0]!["originalText"] = "SIMULATED 伪造原文";
        using (var denied = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", forged)))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);

        using var created = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", body));
        var receipt = await DocumentHttpData<EngineeringRequirementResultV1>(created);
        using (var retry = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", body)))
            Assert.Equal(receipt, await DocumentHttpData<EngineeringRequirementResultV1>(retry));

        string sourcePath = RequirementPath + "/" + receipt.ObjectId;
        using (var download = await host.Send(OperationsRequest(session, HttpMethod.Get, sourcePath + "/original?version=1")))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.True(download.Headers.CacheControl?.NoStore);
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        }

        await docOwner.CommandAsync(DocumentRevise(doc) with { Body = doc.Body with { Name = "SIMULATED 后继版" } });
        using (var exact = await host.Send(OperationsRequest(session, HttpMethod.Get, sourcePath + "?version=1")))
            Assert.Equal(originalText, (await DocumentHttpData<EngineeringRequirementSnapshotV1>(exact)).Body.Source!.Clauses[0].OriginalText);

        const string exportPath = RequirementPath + "/export?kind=SOURCE";
        using (var denied = await host.Send(OperationsRequest(session, HttpMethod.Get, exportPath)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await EngineeringAction(h, "plm-engineering-requirement", "export");
        using (var exported = await host.Send(OperationsRequest(session, HttpMethod.Get, exportPath)))
        {
            Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
            Assert.True(exported.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await exported.Content.ReadAsStringAsync());
            Assert.Equal(originalText, json.RootElement[0].GetProperty("body").GetProperty("source").GetProperty("clauses")[0].GetProperty("originalText").GetString());
        }

        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "file-content", FieldName = "Content", Access = 3 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        foreach (var path in new[] { sourcePath + "?version=1", sourcePath + "/original?version=1", RequirementPath + "/operations/" + template.OperationId })
        {
            using var denied = await host.Send(OperationsRequest(session, HttpMethod.Get, path));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.DoesNotContain(originalText, await denied.Content.ReadAsStringAsync());
        }
        using (var list = await host.Send(OperationsRequest(session, HttpMethod.Get, RequirementPath + "?kind=SOURCE")))
            Assert.Empty(await DocumentHttpData<EngineeringRequirementSnapshotV1[]>(list));
        using (var exported = await host.Send(OperationsRequest(session, HttpMethod.Get, exportPath)))
        {
            Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
            Assert.Equal("[]", await exported.Content.ReadAsStringAsync());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_reply_is_fixed_to_an_earlier_question_and_old_source()
    {
        await using var h = await Seed();
        await GrantRequirement(h);
        var bytes = "SIMULATED 原渠道书面答复"u8.ToArray();
        var upload = await ContentInput(h, bytes);
        var store = new ContentStore();
        var fileOwner = ContentOwner(h, store, new ContentInspector());
        await fileOwner.UploadAsync(upload, new MemoryStream(bytes));
        var owner = RequirementOriginals(h, store);
        var source = await CreateRequirement(owner, SourceBody());
        var request = new RequirementClarificationV1(Guid.NewGuid(), source.Reference, "water", "SIMULATED 防潮等级如何确认？");
        var body = DefinitionBody(source.Reference);
        var requirement = await CreateRequirement(owner, body with { Requirement = body.Requirement! with { Clarifications = [request] } });
        Assert.Contains("SOURCE_CLARIFICATION_PENDING", requirement.Missing);

        await owner.CommandAsync(ReviseRequirement(source, source.Body with { Name = "SIMULATED 来源后继" }));
        var fileRef = new EngineeringSourceRefV1(FileContentBytesV1.Owner, upload.FileId, "1", upload.Digest);
        var response = request with { Reply = new(fileRef, null) };
        var command = ReviseRequirement(requirement, requirement.Body with
        {
            Requirement = requirement.Body.Requirement! with { Clarifications = [response] }
        });
        var result = await owner.CommandAsync(command);
        Assert.Equal(result, (await owner.OperationAsync(command.OperationId, false)).Result);
        var updated = await owner.ReadAsync(command.ObjectId);
        Assert.Equal(response, Assert.Single(updated.Body.Requirement!.Clarifications!));
        Assert.Contains("SOURCE_REPLY_AUTHORITY_UNPROVEN:" + request.Id, updated.Missing);
        Assert.Equal("DRAFT_ONLY", updated.State);
        Assert.Equal(bytes, (await owner.DownloadOriginalAsync(updated.Reference.Id, "2", request.Id)).Bytes);
        Assert.Null(Assert.Single((await owner.ReadAsync(command.ObjectId, "1")).Body.Requirement!.Clarifications!).Reply);
        Assert.Contains((await owner.DiffAsync(command.ObjectId, "1", "2")).Changes, change => change.Path == "clarifications");
        var changedQuestion = ReviseRequirement(updated, updated.Body with
        {
            Requirement = updated.Body.Requirement with { Clarifications = [response with { Question = "替换已保存原问题" }] }
        });
        Assert.Equal("REQUIREMENT_CLARIFICATION_IMMUTABLE",
            (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(changedQuestion))).Message);
        await fileOwner.CommandAsync(ContentCommand(await fileOwner.ReadAsync(upload.FileId), "HOLD"));
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => owner.DownloadOriginalAsync(updated.Reference.Id, "2", request.Id))).Status);
        Assert.Empty(await owner.ListAsync("REQUIREMENT"));
        Assert.Equal(requirement.OriginalJson, (await owner.ReadAsync(command.ObjectId, "1")).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_per_clause_coverage_keeps_disjoint_children_and_holes()
    {
        await using var h = await Seed();
        await GrantRequirement(h);
        await EngineeringAction(h, "plm-engineering-requirement", "source-confirm");
        var owner = Requirements(h);
        var body = JsonSerializer.Deserialize<EngineeringRequirementBodyV1>(QualifiedSourceJson)!;
        var strength = body.Source!.Clauses.Single();
        var moisture = strength with { Id = "water", OriginalText = "SIMULATED 防潮要求" };
        var origin = await CreateRequirement(owner, body with { Name = "SIMULATED 同名", Source = body.Source with { Clauses = [strength, moisture] } });
        var otherStrength = strength with { Terms = strength.Terms! with { Customers = new("VALUES", "sim.customer", ["B"]) } };
        var other = await CreateRequirement(owner, body with { Name = origin.Body.Name, Source = body.Source with { Clauses = [otherStrength] } });
        Assert.NotEqual(origin.Reference, other.Reference);
        var parent = await CreateRequirement(owner, DefinitionBody(origin.Reference));
        EngineeringRequirementBodyV1 Child(EngineeringSourceRefV1 source, string clause)
            => new("REQUIREMENT", "SIMULATED 同名子需求", Requirement: new(source, [clause], "工程解释", "客户约束", "需核原件",
                [], [], [new("parent", "PARENT", parent.Reference, "指定条款", "", "沿来源必要性", [new(clause, clause, "核对准确覆盖理由")])]));
        var a = await CreateRequirement(owner, Child(origin.Reference, "strength"));
        var b = await CreateRequirement(owner, Child(other.Reference, "strength"));
        Assert.NotEqual(a.Reference.Id, b.Reference.Id);
        Assert.NotEqual(a.Body.Requirement!.Source, b.Body.Requirement!.Source);
        EngineeringRequirementBodyV1 Reviewed(EngineeringRequirementSnapshotV1 p, params EngineeringSourceRefV1[] children)
            => p.Body with { Requirement = p.Body.Requirement! with { Decomposition = new(parent.Reference, children, "逐条核对合成范围") } };
        var duplicate = ReviseRequirement(parent, Reviewed(parent, a.Reference, a.Reference));
        Assert.Equal("REQUIREMENT_COVERAGE_DUPLICATE_CHILD",
            (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(duplicate))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(duplicate.OperationId, false)).State);
        await owner.CommandAsync(ReviseRequirement(parent, Reviewed(parent, a.Reference, b.Reference)));
        var partial = await owner.ReadAsync(parent.Reference.Id);
        Assert.False(partial.Coverage!.Complete);
        Assert.Contains(partial.Coverage.Clauses, c => c.ClauseId == "water" && c.State == "MISSING");
        var mapped = Assert.Single(partial.Coverage.Clauses, c => c.ClauseId == "strength");
        Assert.Contains(mapped.Children, c => c.Child.Id == b.Reference.Id && c.Scope == "DISJOINT");

        var c = await CreateRequirement(owner, Child(origin.Reference, "water"));
        await owner.CommandAsync(ReviseRequirement(partial, Reviewed(partial, a.Reference, c.Reference)));
        var reviewed = await owner.ReadAsync(parent.Reference.Id);
        Assert.True(reviewed.Coverage!.Complete);
        Assert.Equal("DRAFT_ONLY", reviewed.State);
        Assert.Equal(parent.OriginalJson, (await owner.ReadAsync(parent.Reference.Id, "1")).OriginalJson);
        Assert.Equal(partial.OriginalJson, (await owner.ReadAsync(parent.Reference.Id, "2")).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_sales_exact_original_and_crm_sources_remain_fenced()
    {
        await using var h = await Seed();
        await GrantRequirement(h);
        await GrantOrderWrites(h);
        var number = await h.Service.CreateAsync(NewOrder(), null);
        var id = await h.Db.Orders.Where(order => order.WebOrderNo == number).Select(order => order.Id).SingleAsync();
        var source = await SalesReader(h).ReadLegacyAsync(id, 1);
        var owner = RequirementOriginals(h, new ContentStore());
        var reference = new EngineeringSourceRefV1(OrderSourceReader.RequirementSourceOwner, id, "1", source.OriginalDigest!);
        var exact = new EngineeringRequirementBodyV1("SOURCE", "SIMULATED 销售原件",
            new("OWNER_ORIGINAL", "SIMULATED 原订单", "1", "SIMULATED 原授权",
                [new("sales-code", number, "JSON:/Number")], new(reference, null)));
        var saved = await CreateRequirement(owner, exact);
        var download = await owner.DownloadOriginalAsync(saved.Reference.Id, "1", null);
        Assert.Equal(source.OriginalDigest, SalesDigest(download.Bytes));
        using (var original = JsonDocument.Parse(download.Bytes))
            Assert.Equal(id, original.RootElement.GetProperty("Id").GetGuid());
        var definition = new RequirementDefinitionV1(saved.Reference, ["sales-code"], "SIMULATED 解释", "", "",
            [], [new("method", "QUALITATIVE", "原订单", "在原交易场景下", "准确字段对照", QualitativeBasis: "与原订单字段一致")], []);
        var missing = await CreateRequirement(owner, new("REQUIREMENT", "SIMULATED 评价草稿", Requirement: definition));
        Assert.Contains("CRITERION_SOURCE_BASIS_REQUIRED:method", missing.Missing);
        var criterion = definition.Criteria[0] with { SourceClauseIds = ["sales-code"] };
        await owner.CommandAsync(ReviseRequirement(missing, missing.Body with { Requirement = definition with { Criteria = [criterion] } }));
        var qualified = await owner.ReadAsync(missing.Reference.Id);
        Assert.DoesNotContain("CRITERION_SOURCE_BASIS_REQUIRED:method", qualified.Missing);
        Assert.Equal("准确字段对照", qualified.Body.Requirement!.Criteria[0].MethodRequirement);
        Assert.Null(qualified.Body.Requirement.Criteria[0].ProposedUnit);
        Assert.Equal("DRAFT_ONLY", qualified.State);

        await using var other = await Seed();
        await GrantRequirement(other);
        await GrantOrderReads(other);
        var foreign = RequirementOriginals(other, new ContentStore());
        var forbidden = await NewRequirement(foreign, exact);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => foreign.CommandAsync(forbidden))).Status);
        Assert.Equal("UNKNOWN", (await foreign.OperationAsync(forbidden.OperationId, false)).State);
        foreach (var (provider, code) in new[]
        {
            (EngineeringRequirementService.CrmOpportunityOrigin, "COMMERCIAL_HISTORY_UNPROVEN"),
            (EngineeringRequirementService.CrmActivityOrigin, "REQUIREMENT_CRM_ACT_AUTHORITY_UNPROVEN"),
            (EngineeringRequirementService.CrmAttachmentOrigin, "UNPROVEN_G03")
        })
        {
            var coordinates = new RequirementOriginV1(reference with { Id = Guid.NewGuid(), Owner = provider }, null);
            var external = exact with { Source = exact.Source! with { Origin = coordinates, Clauses = [] } };
            var blocked = await NewRequirement(owner, external);
            Assert.Equal(code, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(blocked))).Message);
            Assert.Equal("UNKNOWN", (await owner.OperationAsync(blocked.OperationId, false)).State);
            var draft = await CreateRequirement(owner, external with { Source = external.Source! with { Kind = "UNRESOLVED" } });
            Assert.Equal("DRAFT_ONLY", draft.State);
            Assert.Contains("SOURCE_UNRESOLVED", draft.Missing);
            Assert.Empty(draft.Body.Source!.Clauses);
            Assert.Equal(code, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
                () => owner.DownloadOriginalAsync(draft.Reference.Id, "1", null))).Message);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_act_registered_attachment_cannot_use_ordinary_file_source()
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "wp05-13-act-storage");
        await using var fixture = await CreateActFilesAsync(localRoot: folder);
        var context = fixture.Context;
        await GrantRequirement(context);
        var owner = RequirementOriginals(context, new LocalFileStore(folder));
        var upload = fixture.Input;
        Assert.Equal(fixture.Bytes, (await fixture.Native.DownloadAsync(upload.FileId, "1", upload.Digest)).Bytes);
        var plainFile = new RequirementOriginV1(new(FileContentBytesV1.Owner, upload.FileId, "1", upload.Digest), null);
        var draft = new EngineeringRequirementBodyV1("SOURCE", "SIMULATED 活动附件",
            new("OWNER_ORIGINAL", "SIMULATED 待核 ACT 原件", "1", "", [], plainFile));
        var command = await NewRequirement(owner, draft);
        Assert.Equal("UNPROVEN_G03", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => owner.CommandAsync(command))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        Assert.Empty(await owner.ListAsync("SOURCE"));
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_r1_act_attachment_drafts_preserve_distinct_host_coordinates()
    {
        await using var h = await Seed();
        await GrantRequirement(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var session = await Login(host, h);
        using var context = await host.Send(OperationsRequest(session, HttpMethod.Get, RequirementPath + "/creation-context"));
        var authority = (await DocumentHttpData<EngineeringRevisionCreationContextV1>(context)).AuthorityVersion;
        var fileId = Guid.NewGuid().ToString("D");
        var digest = new string('a', 64);
        var first = RequirementNode(new
        {
            activityId = Guid.NewGuid(), activityRevision = "3", referenceSetVersion = "7",
            referenceId = Guid.NewGuid(), attachmentId = fileId, version = "1", contentSha256 = digest
        });
        var second = first.DeepClone();
        second["activityId"] = Guid.NewGuid();
        second["activityRevision"] = "5";
        second["referenceSetVersion"] = "9";
        second["referenceId"] = Guid.NewGuid();

        var firstId = await SaveDraft(first);
        var secondId = await SaveDraft(second);
        Assert.NotEqual(firstId, secondId);
        using var response = await host.Send(OperationsRequest(session, HttpMethod.Get, RequirementPath + "?kind=SOURCE"));
        var rows = await DocumentHttpData<EngineeringRequirementSnapshotV1[]>(response);
        Assert.Equal(2, rows.Length);
        Assert.True(JsonNode.DeepEquals(first, ActCoordinates(Assert.Single(rows, row => row.Reference.Id == firstId))));
        Assert.True(JsonNode.DeepEquals(second, ActCoordinates(Assert.Single(rows, row => row.Reference.Id == secondId))));

        async Task<Guid> SaveDraft(JsonNode coordinates)
        {
            var command = ActRequirementDraft(authority, new JsonObject { ["actFile"] = coordinates.DeepClone() });
            using var write = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", command));
            var result = await DocumentHttpData<EngineeringRequirementResultV1>(write);
            var path = RequirementPath + "/" + result.ObjectId;
            using var read = await host.Send(OperationsRequest(session, HttpMethod.Get, path + "?version=1"));
            var saved = await DocumentHttpData<EngineeringRequirementSnapshotV1>(read);
            Assert.Equal("DRAFT_ONLY", saved.State);
            Assert.Empty(saved.Body.Source!.Clauses);
            Assert.True(JsonNode.DeepEquals(coordinates, ActCoordinates(saved)));
            using var replay = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", command));
            Assert.Equal(result, await DocumentHttpData<EngineeringRequirementResultV1>(replay));
            using var blocked = await host.Send(OperationsRequest(session, HttpMethod.Get, path + "/original?version=1"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
            Assert.Contains("UNPROVEN_G03", await blocked.Content.ReadAsStringAsync());
            return result.ObjectId;
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_r1_partial_act_attachment_can_only_be_saved_as_draft()
    {
        await using var h = await Seed();
        await GrantRequirement(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var session = await Login(host, h);
        using var context = await host.Send(OperationsRequest(session, HttpMethod.Get, RequirementPath + "/creation-context"));
        var authority = (await DocumentHttpData<EngineeringRevisionCreationContextV1>(context)).AuthorityVersion;
        var partial = RequirementNode(new { activityId = Guid.NewGuid(), attachmentId = "SIMULATED/File:原键", version = "版本 01" });
        var command = ActRequirementDraft(authority, new JsonObject { ["actFile"] = partial.DeepClone() });
        var confirmed = command.DeepClone();
        confirmed["body"]!["source"]!["kind"] = "OWNER_ORIGINAL";
        using (var blocked = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", confirmed)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
            Assert.Contains("UNPROVEN_G03", await blocked.Content.ReadAsStringAsync());
        }

        using var write = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", command));
        var result = await DocumentHttpData<EngineeringRequirementResultV1>(write);
        using var read = await host.Send(OperationsRequest(session, HttpMethod.Get, RequirementPath + "/" + result.ObjectId));
        var saved = await DocumentHttpData<EngineeringRequirementSnapshotV1>(read);
        Assert.Equal("DRAFT_ONLY", saved.State);
        Assert.Contains("SOURCE_UNRESOLVED", saved.Missing);
        Assert.True(JsonNode.DeepEquals(partial, ActCoordinates(saved)));

        var legacy = ActRequirementDraft(authority, RequirementNode(new
        {
            reference = new EngineeringSourceRefV1(EngineeringRequirementService.CrmAttachmentOrigin, Guid.NewGuid(), "1", new string('b', 64)),
            occurrenceId = (Guid?)null
        }));
        using var oldWrite = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", legacy));
        var oldResult = await DocumentHttpData<EngineeringRequirementResultV1>(oldWrite);
        using var oldRead = await host.Send(OperationsRequest(session, HttpMethod.Get, RequirementPath + "/" + oldResult.ObjectId + "?version=1"));
        var original = await DocumentHttpData<EngineeringRequirementSnapshotV1>(oldRead);
        Assert.DoesNotContain("\"actFile\"", original.OriginalJson);
        using var oldReplay = await host.Send(OperationsRequest(session, HttpMethod.Post, RequirementPath + "/commands", legacy));
        Assert.Equal(oldResult, await DocumentHttpData<EngineeringRequirementResultV1>(oldReplay));
    }

    private static JsonNode ActRequirementDraft(string authority, JsonNode origin)
    {
        var command = RequirementNode(new EngineeringRequirementCommandV1
        {
            OperationId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), Action = "CREATE",
            ExpectedAuthorityVersion = authority, Reason = "SIMULATED 保留活动附件坐标",
            Body = new("SOURCE", "SIMULATED 同名活动附件",
                new("UNRESOLVED", "SIMULATED 宿主与原件待核", "", "", []))
        });
        command["body"]!["source"]!["origin"] = origin;
        return command;
    }

    private static JsonNode ActCoordinates(EngineeringRequirementSnapshotV1 saved) =>
        JsonNode.Parse(saved.OriginalJson)!["body"]!["source"]!["origin"]!["actFile"]!;

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_wp0513_export_migration_keeps_original_receipt_and_requires_new_permission()
    {
        var database = await server.CreateIsolatedDatabaseAsync("20261126160000_EngineeringRevisionLineageV1");
        await using var h = await SeedForDatabase(database);
        await GrantRequirement(h);
        var owner = Requirements(h);
        var input = await NewRequirement(owner, SourceBody());
        var receipt = await owner.CommandAsync(input);
        var original = (await owner.ReadAsync(input.ObjectId)).OriginalJson;
        await h.Db.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(original, (await owner.ReadAsync(input.ObjectId)).OriginalJson);
        Assert.Equal(receipt, (await owner.OperationAsync(input.OperationId, false)).Result);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.ExportAsync("SOURCE", null, 1))).Status);
        Assert.True(await (from action in h.Db.Sys_MenuActions where action.ActionCode == "export"
            join menu in h.Db.Sys_Menus on action.MenuId equals menu.MenuId
            where menu.MenuKey == "plm-engineering-requirement" select action).AnyAsync());
        await EngineeringAction(h, "plm-engineering-requirement", "export");
        Assert.Equal(original, Assert.Single(await owner.ExportAsync("SOURCE", null, 1)).OriginalJson);
    }

    private static EngineeringRequirementService RequirementOriginals(Harness h, IFileStore storage)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "plm-engineering-requirement", "file-content", "plm-engineering-revision", "plm-engineering-product", "plm-controlled-document" })
        {
            catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 5);
            catalog.RegisterFields(resource, resource switch
            {
                "plm-engineering-requirement" => [new("Definition", "需求定义"), new("Source", "需求来源")],
                "file-content" => [new("Content", "内容"), new("FileName", "文件名")],
                "plm-controlled-document" => [new("Content", "内容"), new("Metadata", "元数据")],
                _ => []
            });
        }
        catalog.Seal();
        var root = new OperationsOwnerV1(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
        return new(h.Db, root, ContentOwner(h, storage, new ContentInspector()), Documents(h, storage), SalesReader(h));
    }

    private static JsonNode RequirementNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
