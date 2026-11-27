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
    [CurrentPermissionPostgresFact]
    public async Task Wp0508_routing_requires_exact_catalogue_dimension_and_parameter_evidence()
    {
        await using var h = await Seed();
        var parent = await RoutingParent(h);
        var routing = Routings(h);
        var machineAuthority = await GrantMachine(h);
        await SeedMachine(h);
        var machine = Assert.Single(await new ResourceSourceOwnerV1(h.Db, machineAuthority).ListAsync("MACHINE"));
        await GrantUomProfile(h);
        var unit = await MakeUnit(Units(h));
        var command = RoutingCreate(parent);
        var step = command.Body.Operations[0] with
        {
            Resources = [machine.Reference],
            Parameters = [new("SIMULATED-width", "2.50", null, null, unit.Reference, "SIMULATED operator text")]
        };
        await routing.CommandAsync(command with { Body = command.Body with { Operations = [step] } });
        var saved = await routing.ReadAsync(command.RoutingId);
        Assert.Contains($"RESOURCE_CAPABILITY_REQUIRED:{step.OccurrenceId}", saved.Missing!);
        Assert.Contains($"PARAMETER_DIMENSION_REQUIRED:{step.OccurrenceId}:SIMULATED-width", saved.Missing!);
        Assert.Contains($"PARAMETER_EVIDENCE_REQUIRED:{step.OccurrenceId}:SIMULATED-width", saved.Missing!);
        Assert.Equal("UNKNOWN", saved.InputValidation!.State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0508_http_pins_resource_approval_work_time_and_exact_process_parameter()
    {
        await using var h = await Seed();
        var fixture = await QualitySetup(h);
        var (_, capabilityCommand, unit) = await CapabilitySetup(h);
        var timeUnit = await MakeUnit(Units(h), BaseUnit("SECOND", "TIME", 8));
        var time = new UomTimeBasisOwnerV1(h.Db, RoutingAuthority(h));
        await time.SaveAsync(new(Guid.NewGuid(), "absent", new(timeUnit.Reference, "SECOND", true, "SIMULATED 工时来源")));
        var basis = await time.ReadAsync(timeUnit.Reference.Id, null);
        var capabilities = new ResourceCapabilityOwnerV1(h.Db, RoutingAuthority(h));
        var input = capabilityCommand with
        {
            Body = capabilityCommand.Body with
            {
                TimeUnit = timeUnit.Reference, TimeBasis = basis.Reference, OutputUnit = unit.Reference,
                Capabilities = [capabilityCommand.Body.Capabilities[0] with { ValidFrom = "2020-01-01T00:00:00Z", ValidTo = null }]
            }
        };
        await capabilities.SaveAsync("MACHINE", input);
        var declaration = await capabilities.ReadAsync("MACHINE", input.Body.Source.Id);
        await EngineeringAction(h, "mes-machine", "capability-approve");
        var approval = new ResourceCapabilityApprovalCommandV1(Guid.NewGuid(), declaration.Reference, "SIMULATED 资源审批原件");
        await capabilities.ApproveAsync("MACHINE", approval);
        var catalogue = await capabilities.ReadAsync("MACHINE", input.Body.Source.Id);

        var qualityBody = fixture.Create.Body!;
        var requirement = qualityBody.Requirements[0] with
        {
            Characteristic = "SIMULATED 工件宽度", Method = "SIMULATED 量具测量",
            Unit = unit.Reference, Target = "2.50000000", LowerLimit = "2.48000000", UpperLimit = "2.55000000"
        };
        var quality = await QualityApprove(h, fixture with
        {
            Create = fixture.Create with { Body = qualityBody with { Stage = "IPQC", Purposes = ["PROCESS_CONTROL"], Requirements = [requirement] } }
        });
        foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, "plm-engineering-routing", action);
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(row => row.RoleId == 72 && row.ResourceKey == "plm-engineering-routing"))
        {
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-engineering-routing", ScopeType = 5 });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
        var revisions = new EngineeringRevisionOwnerV1(h.Db, CheckAuthority(h), Documents(h, fixture.Files), Specifications(h, fixture.Files));
        var parent = await revisions.ReadAsync(fixture.Document.Body.RevisionOrigin.Id, null);
        var draft = RoutingCreate(parent);
        var parameter = new RoutingParameterV1(requirement.Characteristic, "2.50000000", "2.48", "2.55", unit.Reference,
            requirement.Method, "LENGTH", new(quality.Version.Reference, quality.CurrentApproval!.Reference, requirement.Id));
        var step = draft.Body.Operations[0] with
        {
            Resources = [input.Body.Source], ResourceCapabilities = [catalogue.Reference], Parameters = [parameter]
        };
        var command = draft with { Body = draft.Body with { SiteRef = quality.Version.Body.Site, Operations = [step] } };
        var settings = await DocumentHostSettings(fixture.Files);
        settings["QualitySpecification__EvidenceMode"] = "DEMO";
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings);
        var cookies = await Login(host, h);
        const string path = "/api/engineering-routing/owner-v1";
        async Task<HttpResponseMessage> Save(EngineeringRoutingCommandV1 body)
        {
            using var request = OperationsRequest(cookies, HttpMethod.Post, path + "/commands");
            request.Content = JsonContent.Create(body);
            return await host.Send(request);
        }
        async Task<EngineeringRoutingSnapshotV1> Read(string version)
        {
            using var reply = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/{command.RoutingId}?version={version}"));
            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Deserialize<EngineeringRoutingSnapshotV1>(JsonSerializerOptions.Web)!;
        }

        using var create = await Save(command);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var version = await Read("1");
        Assert.Equal("PASS", version.InputValidation!.State);
        Assert.Equal("DRAFT_ONLY", version.Readiness);
        Assert.Equal(parameter, Assert.Single(Assert.Single(version.Body.Operations).Parameters));
        var resource = Assert.Single(version.InputValidation.Catalogues);
        Assert.Equal("APPROVED", resource.State);
        Assert.Equal(catalogue.Reference, resource.Reference);
        Assert.Equal(timeUnit.Reference, resource.TimeUnit);
        Assert.Equal(basis.Reference, resource.TimeBasis);
        Assert.Equal(approval.OperationId, resource.ApprovalOperationId);
        var binding = new EngineeringComponentDraftV1(Guid.NewGuid(), "ROUTING", "manufacturing/primary", 0, "APPLIES", "", "", version.Reference);
        await revisions.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = binding }));
        var bound = await revisions.ReadAsync(parent.Reference.Id, null);
        Assert.Equal(version.Reference, Assert.Single(bound.Draft.Components).SourceRef);

        var mismatch = parameter with { DimensionKey = "MASS", Value = "2.49000000" };
        using var revise = await Save(RoutingRevise(version) with
        {
            Body = version.Body with { RevisionOrigin = bound.Reference, Operations = [step with { Parameters = [mismatch] }] }
        });
        Assert.Equal(HttpStatusCode.OK, revise.StatusCode);
        var invalid = (await Read("2")).InputValidation!;
        Assert.Equal("BLOCK", invalid.State);
        Assert.Contains($"PARAMETER_DIMENSION_MISMATCH:{step.OccurrenceId}:{parameter.Name}", invalid.Findings);
        Assert.Contains($"PARAMETER_EVIDENCE_MISMATCH:{step.OccurrenceId}:{parameter.Name}", invalid.Findings);
        Assert.Equal(version.OriginalJson, (await Read("1")).OriginalJson);

        await Specifications(h, fixture.Files).CommandAsync(QualityCommand(quality, "WITHDRAW"));
        var withdrawn = await Read("1");
        Assert.Contains($"PARAMETER_EVIDENCE_UNAVAILABLE:{step.OccurrenceId}:{parameter.Name}", withdrawn.InputValidation!.Findings);
        Assert.Equal(version.OriginalJson, withdrawn.OriginalJson);
        var menu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "quality-specification").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.RoleId == 72 && row.MenuId == menu && row.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/{command.RoutingId}?version=1"));
        using var recovery = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/operations/{command.OperationId}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, recovery.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0508_alternative_is_directional_and_bound_to_an_approved_configuration_option()
    {
        await using var h = await Seed();
        var parent = await RoutingParent(h);
        await GrantConfiguration(h);
        var rule = await ApprovedConfiguration(h, parent.Draft.ProductRef!);
        var routing = new EngineeringRoutingOwnerV1(h.Db, CheckAuthority(h));
        var input = RoutingCreate(parent);
        var primary = input.Body.Operations[0] with { AlternativeGroup = "SIMULATED-cut" };
        var option = rule.Body.Definitions[0].AllowedValues[0];
        var substitute = primary with
        {
            OccurrenceId = Guid.NewGuid(), SemanticOrder = 20,
            Alternative = new(primary.OccurrenceId, rule.Reference, option)
        };
        await routing.CommandAsync(input with { Body = input.Body with { Operations = [primary, substitute] } });
        var original = await routing.ReadAsync(input.RoutingId);
        Assert.Equal(substitute.Alternative, original.Body.Operations[1].Alternative);
        Assert.DoesNotContain(original.Missing!, code => code.StartsWith("ALTERNATIVE_") || code.StartsWith("APPLICABILITY_OWNER_REQUIRED"));

        var collision = substitute with { OccurrenceId = Guid.NewGuid(), SemanticOrder = 30 };
        await routing.CommandAsync(RoutingRevise(original) with { Body = original.Body with { Operations = [primary, substitute, collision] } });
        var current = await routing.ReadAsync(input.RoutingId);
        Assert.Equal("BLOCK", current.InputValidation!.State);
        Assert.Contains("ALTERNATIVE_RELATION_CONFLICT:" + collision.OccurrenceId, current.Missing!);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        const string path = "/api/engineering-routing/owner-v1";
        async Task<EngineeringRoutingSnapshotV1> ReviseWithDependency(EngineeringRoutingSnapshotV1 before,
            RoutingOperationV1 predecessor, RoutingOperationV1 dependent)
        {
            var command = RoutingRevise(before) with { Body = before.Body with { Operations = [predecessor, dependent] } };
            using var post = OperationsRequest(cookies, HttpMethod.Post, path + "/commands", command);
            using var saved = await host.Send(post);
            var receipt = await DocumentHttpData<EngineeringRoutingResultV1>(saved);
            using var get = OperationsRequest(cookies, HttpMethod.Get, $"{path}/{input.RoutingId}?version={receipt.Version}");
            using var read = await host.Send(get);
            var persisted = await DocumentHttpData<EngineeringRoutingSnapshotV1>(read);
            Assert.Equal(receipt.Digest, persisted.Reference.Digest);
            Assert.Equal(dependent.DependsOn, Assert.Single(persisted.Body.Operations, op => op.OccurrenceId == dependent.OccurrenceId).DependsOn);
            Assert.Equal("BLOCK", persisted.InputValidation!.State);
            Assert.Contains($"ALTERNATIVE_RELATION_CONFLICT:{dependent.OccurrenceId}", persisted.InputValidation.Findings);
            Assert.DoesNotContain($"DEPENDENCY_ORDER_CONFLICT:{dependent.OccurrenceId}", persisted.InputValidation.Findings);
            return persisted;
        }

        current = await ReviseWithDependency(current, primary, substitute with { DependsOn = [primary.OccurrenceId] });
        current = await ReviseWithDependency(current, substitute,
            primary with { SemanticOrder = 30, DependsOn = [substitute.OccurrenceId] });
        Assert.Equal(original.OriginalJson, (await routing.ReadAsync(input.RoutingId, "1")).OriginalJson);
        await Step(ConfigurationRules(h), rule, "WITHDRAW");
        Assert.Contains("ALTERNATIVE_RULE_CHANGED:" + substitute.OccurrenceId, (await routing.ReadAsync(input.RoutingId, "1")).Missing!);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => routing.CommandAsync(RoutingRevise(current)))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0508_approved_purchased_exception_and_required_internal_route_are_distinct()
    {
        await using var h = await Seed();
        var fixture = await CheckSetup(h);
        var checks = Checks(h, fixture.Files);
        var purchased = await checks.CheckAsync(fixture.Command);
        var existing = await checks.ReadAsync(purchased.CheckRunId);
        var route = Assert.Single(existing.Original.Items, item => item.Path == "composition/ROUTING");
        Assert.Equal("NOT_APPLICABLE", route.State);
        Assert.Equal(fixture.Policy.Reference, route.Source);
        Assert.DoesNotContain(existing.Original.Sources, source => source.Reference.Owner == EngineeringRoutingValuesV1.Owner);

        var rules = ConfigurationRules(h);
        var nextPolicy = fixture.Policy.Body with
        {
            EngineeringChecks = new("engineering-check-policy/1", fixture.Policy.Body.EngineeringChecks!.ProductCategory,
                "INTERNAL", "APPLIES", "SIMULATED 自制策略",
                EngineeringSourceCheckValuesV1.Kinds.Select(kind => new EngineeringCompositionPolicyV1(kind, "APPLIES", 1, "SIMULATED 必需依据")).ToArray())
        };
        var policyDraft = await Step(rules, fixture.Policy, "REVISE", nextPolicy);
        var internalPolicy = await Step(rules, await Step(rules, policyDraft, "SUBMIT"), "APPROVE");
        var revisions = new EngineeringRevisionOwnerV1(h.Db, CheckAuthority(h), Documents(h, fixture.Files), Specifications(h, fixture.Files));
        var current = await revisions.ReadAsync(fixture.Command.Revision.Id, null);
        var claimed = new EngineeringComponentDraftV1(Guid.NewGuid(), "ROUTING", "/missing", 20, "NOT_APPLICABLE",
            "SIMULATED 无依据的不适用声明", "", null);
        await revisions.CommandAsync(PatchRevision(current,
            new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new(ValidOptions(), "APPLIES", "SIMULATED 自制策略", internalPolicy.Reference) },
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = claimed }));
        current = await revisions.ReadAsync(current.Reference.Id, null);
        var inspected = await checks.CheckAsync(new(Guid.NewGuid(), current.Reference, current.CandidateRef, current.Etag, internalPolicy.Reference, "SIMULATED 缺少必需路线"));
        var blocked = await checks.ReadAsync(inspected.CheckRunId);
        Assert.Equal("BLOCK", blocked.Original.State);
        Assert.Contains(blocked.Original.Items, item => item.Path == "composition/ROUTING" && item.Code == "REQUIRED_OCCURRENCES_MISSING");
        Assert.Contains(blocked.Original.Items, item => item.Path == "components/" + claimed.OccurrenceId && item.Code == "COMPONENT_APPLICABILITY_BYPASS");
        Assert.DoesNotContain(blocked.Original.Sources, source => source.Reference.Owner == EngineeringRoutingValuesV1.Owner);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0508_exact_tool_specification_does_not_confer_same_number_instance_qualification()
    {
        await using var h = await Seed();
        var parent = await RoutingParent(h);
        await GrantSpecification(h);
        var native = NativePlate();
        native.WdPtnNo = "SIMULATED-" + h.Tenant.ToString("N")[..8];
        h.Db.PlateMolds.Add(native);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var tool = native.Id;
        var specifications = Specifications(h);
        var draft = await ToolDraft(h, tool);
        await specifications.CommandAsync(draft with { Requirements = draft.Requirements with { ProductRef = parent.Draft.ProductRef! } });
        var view = await specifications.ReadAsync(tool);
        var specification = view.Specification!.Reference;
        var number = view.Source.Source.GetProperty("identity").GetProperty("number").GetString()!;
        await GrantToolInstance(h);
        var physical = new CP6.Entity.DomainModels.Wms.PlateMoldStock { PlateNo = number, PlateType = "PLATE", Creator = "price-owner" };
        h.Db.PlateMoldStocks.Add(physical);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var instance = (await ToolInstances(h).ReadAsync(physical.Id)).Reference;

        var routing = Routings(h);
        var command = RoutingCreate(parent);
        var step = command.Body.Operations[0];
        var formed = command with { Body = command.Body with { Operations = [step with { Tools = [specification] }] } };
        await routing.CommandAsync(formed);
        var saved = await routing.ReadAsync(command.RoutingId);
        Assert.Equal(specification, Assert.Single(saved.Body.Operations[0].Tools));
        Assert.Contains("TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED:" + step.OccurrenceId, saved.Missing!);
        foreach (var reference in new[] { new EngineeringSourceRefV1(instance.Owner, instance.Id, instance.Version, instance.Digest),
            specification with { Owner = "PlateNo" } })
        {
            var invalid = RoutingRevise(saved) with { Body = saved.Body with { Operations = [step with { Tools = [reference] }] } };
            var denial = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => routing.CommandAsync(invalid));
            Assert.Equal("ROUTING_TOOL_REFERENCE_INVALID", denial.Message);
            Assert.Equal(400, denial.Status);
            Assert.Equal("UNKNOWN", (await routing.OperationAsync(invalid.OperationId, false)).State);
        }
        Assert.Equal(saved.OriginalJson, (await routing.ReadAsync(command.RoutingId)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0508_lost_commit_recovers_original_key_and_route_revision_preserves_work_order_snapshot()
    {
        var commit = new CommitFault();
        await using var h = await Seed(commit);
        var parent = await RoutingParent(h);
        var routing = Routings(h);
        var mes = new WorkOrderService(h.Db, new MesSequenceService(h.Db), new NoOpWmsBridgeHook(), await GrantOperations(h));
        const string wo = "SIM-WP05-08";
        h.Db.WorkOrders.Add(new() { WorkOrderNo = wo, ProductCd = "SIMULATED", ProductionQty = 12, Creator = "price-owner" });
        h.Db.WorkOrderProcesses.Add(new()
        {
            WorkOrderNo = wo, ProcessCd = "CUT", TaskCd = "STEP1", SortOrder = 10,
            ProcessName = "SIMULATED 原工序快照", StdLossRate = 0.00000001m, Creator = "price-owner"
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var before = await mes.GetByNoAsync(wo);
        Assert.NotNull(before);

        var command = RoutingCreate(parent);
        await routing.CommandAsync(command);
        var original = await routing.ReadAsync(command.RoutingId);
        var change = RoutingRevise(original) with { Body = original.Body with { Operations = [original.Body.Operations[0] with { SemanticOrder = 50 }] } };
        commit.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => routing.CommandAsync(change))).Message);
        var recovered = await routing.OperationAsync(change.OperationId, false);
        Assert.Equal("COMMITTED", recovered.State);
        Assert.Equal(recovered.Result, await routing.CommandAsync(change));
        Assert.Equal("2", recovered.Result!.Version);
        Assert.Equal(original.OriginalJson, (await routing.ReadAsync(command.RoutingId, "1")).OriginalJson);
        Assert.Equal(JsonSerializer.Serialize(before.Processes), JsonSerializer.Serialize((await mes.GetByNoAsync(wo))!.Processes));
    }
}
