using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string AssignmentPath = "/api/mes/resource-assignments/owner-v1";
    private const string CapabilityPath = "/api/mes/resource-capabilities/owner-v1/MACHINE";

    private static async Task<JsonElement> CatalogueCall(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = OperationsRequest(cookies, method, path);
        if (body != null) request.Content = JsonContent.Create(body, body.GetType());
        using var response = await host.Send(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{method} {path}: expected {expected}, got {response.StatusCode}. {content}");
        if (expected != HttpStatusCode.OK) return default;
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var envelope = JsonDocument.Parse(content);
        return envelope.RootElement.GetProperty("data").Clone();
    }

    private static async Task<(ResourceSourceRefV1 Machine, ResourceSourceRefV1 First, ResourceSourceRefV1 Second)> AssignmentResources(Harness h)
    {
        await GrantMachine(h);
        await SeedMachine(h);
        await GrantCost(h);
        await SeedCost(h, "WC-A");
        await SeedCost(h, "WC-B");
        var resources = new ResourceSourceOwnerV1(h.Db, RoutingAuthority(h));
        var machine = Assert.Single(await resources.ListAsync("MACHINE"));
        var centers = await h.Db.WorkCenters.OrderBy(row => row.WgCd).Select(row => row.Id).ToArrayAsync();
        return (machine.Reference, (await resources.ReadAsync("WORK_CENTER", centers[0])).Reference,
            (await resources.ReadAsync("WORK_CENTER", centers[1])).Reference);
    }

    [CurrentPermissionPostgresFact]
    public async Task ResourceCatalogue_HTTP_assignment_preserves_exact_history_and_routing_original()
    {
        await using var h = await Seed();
        var (machine, first, second) = await AssignmentResources(h);
        var firstPlan = new ResourceAssignmentPlanV1(machine, [new(first, "2020-01-01T00:00:00Z", null)]);
        var create = new ResourceAssignmentCommandV1(Guid.NewGuid(), "absent", firstPlan, "模拟机器初始归属");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var commandPath = AssignmentPath + "/commands";
        var readPath = AssignmentPath + "/" + machine.Id;
        Assert.Equal(JsonValueKind.Null, (await CatalogueCall(host, cookies, HttpMethod.Get, readPath)).ValueKind);
        await CatalogueCall(host, cookies, HttpMethod.Post, commandPath, create, HttpStatusCode.Forbidden);
        await EngineeringAction(h, "mes-machine", "assignment-edit");

        var receipt = (await CatalogueCall(host, cookies, HttpMethod.Post, commandPath, create)).Deserialize<ResourceSourceResultV1>(JsonSerializerOptions.Web);
        Assert.Equal("1", receipt!.Version);
        var initial = (await CatalogueCall(host, cookies, HttpMethod.Get, readPath)).Deserialize<ResourceAssignmentVersionV1>(JsonSerializerOptions.Web)!;
        Assert.Equal(machine.Id, initial.Reference.Id);
        Assert.Equal(first, initial.CurrentWorkCenter);
        Assert.True(initial.IsCurrent);

        var route = RoutingCreate(await RoutingParent(h));
        route = route with { Body = route.Body with { Operations = [route.Body.Operations[0] with { Resources = [machine] }] } };
        await Routings(h).CommandAsync(route);
        var routePath = "/api/engineering-routing/owner-v1/" + route.RoutingId;
        var originalRoute = (await CatalogueCall(host, cookies, HttpMethod.Get, routePath)).Deserialize<EngineeringRoutingSnapshotV1>(JsonSerializerOptions.Web)!;
        Assert.Equal(machine, Assert.Single(Assert.Single(originalRoute.Body.Operations).Resources));

        var boundary = "2021-01-01T00:00:00Z";
        var later = new ResourceAssignmentPlanV1(machine, [new(first, "2020-01-01T00:00:00Z", boundary), new(second, boundary, null)]);
        var revise = new ResourceAssignmentCommandV1(Guid.NewGuid(), "1", later, "模拟调整工作中心");
        var badIntervals = new ResourceAssignmentPlanV1(machine,
            [new(first, "2020-01-01T00:00:00Z", null), new(second, boundary, null)]);
        await CatalogueCall(host, cookies, HttpMethod.Post, commandPath, revise with { Plan = badIntervals }, HttpStatusCode.BadRequest);
        var revised = (await CatalogueCall(host, cookies, HttpMethod.Post, commandPath, revise)).Deserialize<ResourceSourceResultV1>(JsonSerializerOptions.Web);
        Assert.Equal("2", revised!.Version);
        await CatalogueCall(host, cookies, HttpMethod.Post, commandPath, revise with { OperationId = Guid.NewGuid() }, HttpStatusCode.PreconditionFailed);

        var current = (await CatalogueCall(host, cookies, HttpMethod.Get, readPath)).Deserialize<ResourceAssignmentVersionV1>(JsonSerializerOptions.Web)!;
        var history = (await CatalogueCall(host, cookies, HttpMethod.Get, readPath + "?version=1")).Deserialize<ResourceAssignmentVersionV1>(JsonSerializerOptions.Web)!;
        Assert.Equal(initial.Reference.Id, current.Reference.Id);
        Assert.Equal(second, current.CurrentWorkCenter);
        Assert.Equal(initial.OriginalJson, history.OriginalJson);
        Assert.False(history.IsCurrent);
        Assert.Null(history.CurrentWorkCenter);

        var sameRoute = (await CatalogueCall(host, cookies, HttpMethod.Get, routePath + "?version=1")).Deserialize<EngineeringRoutingSnapshotV1>(JsonSerializerOptions.Web)!;
        Assert.Equal(originalRoute.OriginalJson, sameRoute.OriginalJson);
        Assert.Equal(machine, Assert.Single(Assert.Single(sameRoute.Body.Operations).Resources));
        Assert.Equal("DRAFT_ONLY", sameRoute.Readiness);

        var recoveryPath = AssignmentPath + "/operations/" + create.OperationId;
        var original = (await CatalogueCall(host, cookies, HttpMethod.Get, recoveryPath)).Deserialize<ResourceSourceOperationV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("COMMITTED", original.State);
        Assert.Equal(receipt, original.Result);
        Assert.Equal(receipt, (await CatalogueCall(host, cookies, HttpMethod.Post, commandPath, create)).Deserialize<ResourceSourceResultV1>(JsonSerializerOptions.Web));

        await using var other = await Seed();
        await GrantMachine(other);
        var otherCookies = await Login(host, other);
        await CatalogueCall(host, otherCookies, HttpMethod.Get, readPath, expected: HttpStatusCode.Forbidden);
        var centerMenu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "mes-work-center").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == centerMenu && row.ActionCode == "query").ExecuteDeleteAsync();
        await CatalogueCall(host, cookies, HttpMethod.Get, recoveryPath, expected: HttpStatusCode.Forbidden);
    }

    [CurrentPermissionPostgresFact]
    public async Task ResourceCatalogue_HTTP_approved_capability_pins_sources_and_keeps_unknown()
    {
        await using var h = await Seed();
        var (_, draft, _) = await CapabilitySetup(h);
        var timeUnit = await MakeUnit(Units(h), BaseUnit("SEC", "TIME", 8));
        var time = new UomTimeBasisOwnerV1(h.Db, RoutingAuthority(h));
        await time.SaveAsync(new(Guid.NewGuid(), "absent", new(timeUnit.Reference, "SECOND", true, "模拟时间定义")));
        var timeBasis = await time.ReadAsync(timeUnit.Reference.Id, null);
        draft = draft with { Body = draft.Body with { TimeUnit = timeUnit.Reference, TimeBasis = timeBasis.Reference,
            Capabilities = [draft.Body.Capabilities[0] with { ValidFrom = "2020-01-01T00:00:00Z", ValidTo = null }] } };

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var path = CapabilityPath + "/" + draft.Body.Source.Id;
        var lookup = path + "/capability?key=WIDTH";
        var unknown = (await CatalogueCall(host, cookies, HttpMethod.Get, lookup)).Deserialize<ResourceCapabilityReadingV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("UNKNOWN", unknown.State);
        Assert.Null(unknown.Capability);
        await CatalogueCall(host, cookies, HttpMethod.Post, CapabilityPath + "/commands", draft);
        var declaration = (await CatalogueCall(host, cookies, HttpMethod.Get, path)).Deserialize<ResourceCapabilitySnapshotV1>(JsonSerializerOptions.Web)!;
        var declared = (await CatalogueCall(host, cookies, HttpMethod.Get, lookup)).Deserialize<ResourceCapabilityReadingV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("DECLARED_NOT_QUALIFIED", declared.State);
        Assert.Null(declared.Approval);
        Assert.Equal(timeBasis.Reference, declaration.Body.TimeBasis);
        Assert.Equal("SECOND", timeBasis.Definition.Basis);

        var approve = new ResourceCapabilityApprovalCommandV1(Guid.NewGuid(), declaration.Reference, "模拟能力检验原件 WP08-16-01");
        var approvalPath = CapabilityPath + "/approvals";
        await CatalogueCall(host, cookies, HttpMethod.Post, approvalPath, approve, HttpStatusCode.Forbidden);
        await EngineeringAction(h, "mes-machine", "capability-approve");
        var receipt = (await CatalogueCall(host, cookies, HttpMethod.Post, approvalPath, approve)).Deserialize<ResourceSourceResultV1>(JsonSerializerOptions.Web)!;
        var approved = (await CatalogueCall(host, cookies, HttpMethod.Get, path)).Deserialize<ResourceCapabilitySnapshotV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("2", receipt.Version);
        Assert.Equal(receipt.Digest, approved.Reference.Digest);
        Assert.Equal(declaration.Reference, approved.Approval!.Declaration);
        Assert.Equal(approve.OperationId, approved.Approval.OperationId);
        Assert.Equal(h.User, approved.ActorId);
        var observed = (await CatalogueCall(host, cookies, HttpMethod.Get, lookup)).Deserialize<ResourceCapabilityReadingV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("APPROVED", observed.State);
        Assert.Equal(approved.Reference, observed.Catalog);
        Assert.Equal(approve.Evidence, observed.Approval!.Evidence);
        Assert.Contains("RESOURCE_QUALIFICATION_REQUIRED", approved.Missing);
        Assert.Equal("DECLARED_NOT_QUALIFIED", approved.Qualification);
        var noEvidence = (await CatalogueCall(host, cookies, HttpMethod.Get, path + "/capability?key=UNDECLARED")).Deserialize<ResourceCapabilityReadingV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("UNKNOWN", noEvidence.State);
        Assert.Null(noEvidence.Approval);

        var recover = (await CatalogueCall(host, cookies, HttpMethod.Get, CapabilityPath + "/operations/" + approve.OperationId))
            .Deserialize<ResourceSourceOperationV1>(JsonSerializerOptions.Web)!;
        Assert.Equal(receipt, recover.Result);
        await CatalogueCall(host, cookies, HttpMethod.Post, approvalPath, approve with { OperationId = Guid.NewGuid() }, HttpStatusCode.PreconditionFailed);
        await CatalogueCall(host, cookies, HttpMethod.Post, CapabilityPath + "/commands", draft with
        {
            OperationId = Guid.NewGuid(), ExpectedVersion = "2",
            Body = draft.Body with { Basis = "模拟能力声明后继版本" }
        });
        var after = (await CatalogueCall(host, cookies, HttpMethod.Get, lookup)).Deserialize<ResourceCapabilityReadingV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("DECLARED_NOT_QUALIFIED", after.State);
        Assert.Null(after.Approval);
        var savedApproval = (await CatalogueCall(host, cookies, HttpMethod.Get, path + "?version=2"))
            .Deserialize<ResourceCapabilitySnapshotV1>(JsonSerializerOptions.Web)!;
        Assert.Equal(approved.OriginalJson, savedApproval.OriginalJson);
        Assert.False(savedApproval.IsCurrent);

        await time.SaveAsync(new(Guid.NewGuid(), timeBasis.Reference.Version, timeBasis.Definition with { Enabled = false }));
        var stale = (await CatalogueCall(host, cookies, HttpMethod.Get, lookup)).Deserialize<ResourceCapabilityReadingV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("UNKNOWN", stale.State);
        Assert.Null(stale.Approval);
    }

    [CurrentPermissionPostgresFact]
    public async Task ResourceCatalogue_assignment_commit_failure_keeps_original_and_head_atomic()
    {
        var failure = new InterruptCurrencyCommit();
        await using var h = await Seed(failure);
        var (machine, center, _) = await AssignmentResources(h);
        await EngineeringAction(h, "mes-machine", "assignment-edit");
        var owner = new ResourceAssignmentOwnerV1(h.Db, RoutingAuthority(h));
        var input = new ResourceAssignmentCommandV1(Guid.NewGuid(), "absent",
            new(machine, [new(center, "2020-01-01T00:00:00Z", null)]), "模拟事务失败");
        failure.Interrupt = true;
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(input));
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", error.Message);
        Assert.Null(await owner.ReadAsync(machine.Id, null));
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
        Assert.Equal("CLOSED", (await owner.OperationAsync(input.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",
            (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(input))).Message);
    }
}
