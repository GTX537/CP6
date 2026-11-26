using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Wp0502_http_distinguishes_scoped_dual_owner_confirmation_from_legacy_draft()
    {
        await using var h = await Seed();
        await GrantMappingRead(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        var engineering = await MakeEngineering(h);
        var legacy = await MakeMapping(h, await MappingCandidate(h, engineering, "P0502A"));
        var product = legacy.Candidate.BusinessRef!;
        const string legal = "SIMULATED-LEGAL-A";
        await SeedMappingLegalScope(h, legal);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        const string productPath = "api/products/identity-owner-v1";
        const string mappingPath = "api/engineering-mappings/owner-v1";
        async Task<JsonElement> Read(HttpMethod method, string path, object? body = null)
        {
            using var response = await host.Send(OperationsRequest(cookies, method, path, body));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Clone();
        }

        var original = await Read(HttpMethod.Get, $"{productPath}/{product.Id}");
        AssertUnprovenProductFacts(original);
        var before = await Read(HttpMethod.Post, $"{productPath}/qualification",
            new CommercialProductQualificationQueryV1(product, MappingRef: legacy.Reference));
        AssertUnprovenProductFacts(before.GetProperty("product"));
        Assert.Equal(JsonValueKind.Null, before.GetProperty("mapping").GetProperty("confirmedIdentitySite").ValueKind);

        var created = await Read(HttpMethod.Post, $"{mappingPath}/commands", new
        {
            operationId = Guid.NewGuid(), mappingId = legacy.Reference.Id, action = "DRAFT",
            expectedVersion = legacy.Reference.Version,
            candidate = new
            {
                legacy.Candidate.EngineeringRef, legacy.Candidate.BusinessRef, legacy.Candidate.SiteRef,
                legacy.Candidate.Purpose, legacy.Candidate.Evidence, legacy.Candidate.Conflicts, legacy.Candidate.ConflictsResolved,
                legalEntity = new { key = legal, version = "1", evidence = "合成经营产品法人归属原件 A" },
                legacyMappingRef = legacy.Reference
            }
        });
        var draftVersion = created.GetProperty("version").GetString();
        var current = await Read(HttpMethod.Get, $"{mappingPath}/{legacy.Reference.Id}");
        Assert.Equal("DRAFT", current.GetProperty("state").GetString());
        AssertUnprovenProductFacts(await Read(HttpMethod.Get, $"{productPath}/{product.Id}"));

        var premature = new EngineeringMappingCommandV1
        {
            OperationId = Guid.NewGuid(), MappingId = legacy.Reference.Id, ExpectedVersion = draftVersion!, Action = "CONFIRM"
        };
        using (var rejected = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"{mappingPath}/commands", premature)))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        var reviewed = await Read(HttpMethod.Post, $"{mappingPath}/commands", premature with { Action = "REVIEW", OperationId = Guid.NewGuid() });
        AssertUnprovenProductFacts(await Read(HttpMethod.Get, $"{productPath}/{product.Id}"));
        var confirm = premature with { ExpectedVersion = reviewed.GetProperty("version").GetString()! };
        var result = await Read(HttpMethod.Post, $"{mappingPath}/commands", confirm);
        Assert.Equal(result.GetRawText(), (await Read(HttpMethod.Post, $"{mappingPath}/commands", confirm)).GetRawText());
        var confirmed = await Read(HttpMethod.Get, $"{mappingPath}/{legacy.Reference.Id}");
        var confirmedRef = confirmed.GetProperty("reference").Deserialize<EngineeringMappingRefV1>(ProductWire)!;
        Assert.Equal(legacy.Reference.Id, confirmedRef.Id);
        Assert.Equal("CONFIRMED", confirmed.GetProperty("state").GetString());
        Assert.Equal(legal, confirmed.GetProperty("confirmedLegalScope").GetProperty("legalEntity").GetProperty("key").GetString());
        Assert.Equal("confirm", confirmed.GetProperty("engineeringConfirmation").GetProperty("action").GetString());
        Assert.Equal("confirm-engineering", confirmed.GetProperty("businessConfirmation").GetProperty("action").GetString());

        var after = await Read(HttpMethod.Get, $"{productPath}/{product.Id}");
        var combined = await Read(HttpMethod.Post, $"{productPath}/qualification",
            new CommercialProductQualificationQueryV1(product, MappingRef: confirmedRef));
        foreach (var row in new[] { after, combined.GetProperty("product") })
        {
            Assert.Equal("CONFIRMED", row.GetProperty("legalEntityApplicability").GetString());
            Assert.Equal("KNOWN", row.GetProperty("legacyMappingKnowledge").GetString());
            Assert.Equal("REQUIRED_FENCED", row.GetProperty("manufacturingApplicability").GetString());
            Assert.Equal("REQUIRED_FENCED", row.GetProperty("productCurrencyBinding").GetString());
            Assert.Equal(original.GetProperty("originalJson").GetString(), row.GetProperty("originalJson").GetString());
        }
        Assert.DoesNotContain("LEGAL_ENTITY_APPLICABILITY", combined.GetProperty("missing").Deserialize<string[]>()!);
        var historical = await Read(HttpMethod.Get, $"{mappingPath}/{legacy.Reference.Id}?version={legacy.Reference.Version}");
        Assert.Equal("DRAFT", historical.GetProperty("state").GetString());
        Assert.Equal(legacy.OriginalJson, historical.GetProperty("originalJson").GetString());

        var knownOriginal = await Read(HttpMethod.Post, $"{productPath}/qualification",
            new CommercialProductQualificationQueryV1(product, MappingRef: legacy.Reference));
        Assert.Equal("KNOWN", knownOriginal.GetProperty("product").GetProperty("legacyMappingKnowledge").GetString());
        Assert.Equal(JsonValueKind.Null, knownOriginal.GetProperty("mapping").GetProperty("confirmedIdentitySite").ValueKind);
        await Read(HttpMethod.Post, $"{mappingPath}/commands", confirm with
        { Action = "DISABLE", OperationId = Guid.NewGuid(), ExpectedVersion = confirmedRef.Version });
        var disabled = await Read(HttpMethod.Get, $"{productPath}/{product.Id}");
        Assert.Equal("REQUIRED_FENCED", disabled.GetProperty("legalEntityApplicability").GetString());
        Assert.Equal("KNOWN", disabled.GetProperty("legacyMappingKnowledge").GetString());
        var engineeringMenu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "plm-engineering-product").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == engineeringMenu && row.ActionCode == "query").ExecuteDeleteAsync();
        AssertUnprovenProductFacts(await Read(HttpMethod.Get, $"{productPath}/{product.Id}"));
        using var recovered = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{mappingPath}/operations/{confirm.OperationId}"));
        Assert.Equal(HttpStatusCode.Forbidden, recovered.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_r1_qualification_uses_only_the_selected_site_legal_scope()
    {
        await using var h = await Seed();
        await GrantMappingRead(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        const string legalKey = "SIM-R1-LEGAL";
        await SeedMappingLegalScope(h, legalKey);
        var service = EngineeringMappings(h);
        var a = await MappingCandidate(h, await MakeEngineering(h), "R1SITE");
        var confirmedA = await ConfirmReviewedMapping(service, await MakeMapping(h, a with { LegalEntity = ProductMappingLegal(legalKey) }));
        var siteB = await OtherScopeSite(h);
        var draftB = await MakeMapping(h, a with { SiteRef = siteB });
        var identityOnly = await service.CommandAsync(MappingAction(draftB, "CONFIRM"));
        var confirmedB = await service.ReadAsync(identityOnly.ObjectId, identityOnly.Version);
        Assert.Equal(a.BusinessRef, confirmedB.Candidate.BusinessRef);
        Assert.Equal(siteB, confirmedB.ConfirmedIdentitySite!.SiteRef);
        Assert.Null(confirmedB.ConfirmedLegalScope);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        async Task<JsonElement> Read(HttpMethod method, string path, object? body = null)
        {
            using var response = await host.Send(OperationsRequest(cookies, method, path, body));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Clone();
        }

        const string path = "api/products/identity-owner-v1";
        var product = a.BusinessRef!;
        Task<JsonElement> Qualify(EngineeringMappingRefV1? mapping) => Read(HttpMethod.Post, $"{path}/qualification",
            new CommercialProductQualificationQueryV1(product, MappingRef: mapping));

        var bResult = await Qualify(confirmedB.Reference);
        Assert.Equal(confirmedB.Reference, bResult.GetProperty("mapping").GetProperty("reference").Deserialize<EngineeringMappingRefV1>(ProductWire));
        foreach (var result in new[] { bResult, await Qualify(draftB.Reference), await Qualify(null) })
        {
            Assert.Contains("LEGAL_ENTITY_APPLICABILITY", result.GetProperty("missing").Deserialize<string[]>()!);
            var identity = result.GetProperty("product");
            Assert.Equal("REQUIRED_FENCED", identity.GetProperty("legalEntityApplicability").GetString());
            Assert.Empty(identity.GetProperty("legalEntityScopes").EnumerateArray());
        }

        var aResult = await Qualify(confirmedA.Reference);
        Assert.DoesNotContain("LEGAL_ENTITY_APPLICABILITY", aResult.GetProperty("missing").Deserialize<string[]>()!);
        var aIdentity = aResult.GetProperty("product");
        Assert.Equal("CONFIRMED", aIdentity.GetProperty("legalEntityApplicability").GetString());
        var scope = Assert.Single(aIdentity.GetProperty("legalEntityScopes").Deserialize<EngineeringConfirmedLegalScopeV1[]>(ProductWire)!);
        Assert.Equal(confirmedA.ConfirmedLegalScope, scope);
        Assert.Equal(a.SiteRef, scope.IdentitySite.SiteRef);
        Assert.Equal(legalKey, scope.LegalEntity.Key);
        var standalone = await Read(HttpMethod.Get, $"{path}/{product.Id}");
        Assert.Equal(scope, Assert.Single(standalone.GetProperty("legalEntityScopes").Deserialize<EngineeringConfirmedLegalScopeV1[]>(ProductWire)!));
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_two_sites_keep_their_own_legal_scope_and_conflicting_owner_facts_are_rejected()
    {
        await using var h = await Seed();
        await GrantMappingRead(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        await SeedMappingLegalScope(h, "SIM-A");
        await SeedMappingLegalScope(h, "SIM-B");
        var engineering = await MakeEngineering(h);
        var different = await MakeEngineering(h, engineering.Product with { Definition = "同名部材，使用不同厚度及原始规格" });
        Assert.NotEqual(engineering.Reference.Id, different.Reference.Id);
        Assert.Equal(engineering.Product.Category, different.Product.Category);
        Assert.Equal(engineering.Product.Name, different.Product.Name);
        Assert.NotEqual(engineering.Product.Definition, different.Product.Definition);
        var siteA = (await MappingCandidate(h, engineering, "P0502A")) with { LegalEntity = ProductMappingLegal("SIM-A") };
        var siteB = (await MappingCandidate(h, engineering, "P0502B")) with { LegalEntity = ProductMappingLegal("SIM-B") };
        var service = EngineeringMappings(h);
        var a = await ConfirmReviewedMapping(service, await MakeMapping(h, siteA));
        var b = await ConfirmReviewedMapping(service, await MakeMapping(h, siteB));
        Assert.NotEqual(a.Candidate.BusinessRef, b.Candidate.BusinessRef);
        Assert.NotEqual(a.Candidate.SiteRef, b.Candidate.SiteRef);
        foreach (var map in new[] { a, b })
        {
            Assert.Equal(engineering.Reference, map.Candidate.EngineeringRef);
            Assert.Equal("CONFIRMED", map.LegalEntityApplicability);
            Assert.Equal(map.Candidate.LegalEntity, map.ConfirmedLegalScope!.LegalEntity);
            Assert.Equal(map.Candidate.SiteRef, map.ConfirmedLegalScope.IdentitySite.SiteRef);
            Assert.Equal(h.User, map.EngineeringConfirmation!.ActorId);
            Assert.Equal(h.User, map.BusinessConfirmation!.ActorId);
            Assert.NotEqual(map.EngineeringConfirmation.Owner, map.BusinessConfirmation.Owner);
            Assert.Null(map.LegacyConfirmation);
        }

        var conflicting = await MakeMapping(h, siteA with { SiteRef = siteB.SiteRef, LegalEntity = siteB.LegalEntity });
        var reviewed = await ReviewMapping(service, conflicting);
        Assert.Equal("ENGINEERING_PRODUCT_LEGAL_SCOPE_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => service.CommandAsync(MappingAction(reviewed, "CONFIRM")))).Message);
        var wrongLegacy = MappingAction(a, "DRAFT") with { Candidate = siteB with { LegacyMappingRef = a.Reference } };
        Assert.Equal("ENGINEERING_LEGACY_MAPPING_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => service.CommandAsync(wrongLegacy))).Message);
        Assert.Equal(a.Reference, (await ConsumeMapping(h, a.Reference)).Reference);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.scopes SET enabled=false,revision=revision+1 WHERE tenant_id={h.Tenant} AND legal_entity_key='SIM-A'
            """);
        var disabledScope = await service.ReadAsync(a.Reference.Id, a.Reference.Version);
        Assert.Equal("REQUIRED_FENCED", disabledScope.LegalEntityApplicability);
        Assert.Equal(a.OriginalJson, disabledScope.OriginalJson);
        Assert.Equal("CONFIRMED", (await service.ReadAsync(b.Reference.Id, b.Reference.Version)).LegalEntityApplicability);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_same_item_code_at_another_site_does_not_supply_technical_equivalence()
    {
        await using var h = await Seed();
        await GrantMappingRead(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        await SeedMappingLegalScope(h, "SIM-SPEC");
        var firstEngineering = await MakeEngineering(h);
        var otherEngineering = await MakeEngineering(h, firstEngineering.Product with { Definition = "同名同码，厚度为 9 mm" });
        Assert.Equal(firstEngineering.Product.Code, otherEngineering.Product.Code);
        Assert.Equal(firstEngineering.Product.Name, otherEngineering.Product.Name);
        Assert.NotEqual(firstEngineering.Product.Definition, otherEngineering.Product.Definition);
        var first = await MappingCandidate(h, firstEngineering, "SPCA");
        var other = await MappingCandidate(h, otherEngineering, "SPCB");
        Assert.NotEqual(first.SiteRef, other.SiteRef);

        const string item = "SIM-SAME-ITEM";
        var identityReader = new CommercialProductIdentityReader(h.Db, EngineeringAuthority(h));
        async Task<CommercialProductIdentityV1> SetMaterial(EngineeringMappingCandidateV1 candidate, string branch)
        {
            var id = candidate.BusinessRef!.Id;
            await h.Db.ProductMasters.Where(row => row.Id == id).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.ItemCd, item).SetProperty(row => row.Branch1, branch));
            return await identityReader.ReadAsync(id);
        }
        var a = await SetMaterial(first, "A");
        var b = await SetMaterial(other, "B");
        Assert.Equal(a.MaterialKey.ItemCode, b.MaterialKey.ItemCode);
        Assert.NotEqual(a.MaterialKey.Branch1, b.MaterialKey.Branch1);
        Assert.NotEqual(a.Reference, b.Reference);
        var service = EngineeringMappings(h);
        var confirmed = await ConfirmReviewedMapping(service, await MakeMapping(h,
            first with { BusinessRef = a.Reference, LegalEntity = ProductMappingLegal("SIM-SPEC") }));
        var pending = await MakeMapping(h, other with { EngineeringRef = firstEngineering.Reference, BusinessRef = b.Reference,
            LegalEntity = ProductMappingLegal("SIM-SPEC"), Evidence = "", Conflicts = "同物料代码，另一 Site 的枝号和工程规格不同", ConflictsResolved = false });
        Assert.Equal("ENGINEERING_MAPPING_REVIEW_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => service.CommandAsync(MappingAction(pending, "REVIEW")))).Message);
        var draft = await service.ReadAsync(pending.Reference.Id, pending.Reference.Version);
        Assert.Equal("DRAFT", draft.State);
        Assert.Null(draft.ConfirmedIdentitySite);
        Assert.Equal(b.Reference, draft.Candidate.BusinessRef);
        Assert.Equal(confirmed.OriginalJson, (await service.ReadAsync(confirmed.Reference.Id, confirmed.Reference.Version)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_scoped_review_confirmation_and_recovery_keep_authority_and_tenant_boundaries()
    {
        var interruption = new InterruptCurrencyCommit();
        await using var h = await Seed(interruption);
        await GrantMappingRead(h);
        await SeedMappingLegalScope(h, "SIM-OWN");
        var engineering = await MakeEngineering(h);
        var service = EngineeringMappings(h);
        var candidate = await MappingCandidate(h, engineering, "SAME");
        var original = await MakeMapping(h, candidate);
        var draftResult = await service.CommandAsync(MappingAction(original, "DRAFT") with
        { Candidate = candidate with { LegalEntity = ProductMappingLegal("SIM-OWN"), LegacyMappingRef = original.Reference } });
        var verified = await ReviewMapping(service, await service.ReadAsync(original.Reference.Id, draftResult.Version));
        var command = MappingAction(verified, "CONFIRM");
        var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(command));
        Assert.Equal(403, denied.Status);
        Assert.Equal("UNKNOWN", (await service.OperationAsync(command.OperationId, false)).State);

        await EngineeringAction(h, "erp-product", "confirm-engineering");
        interruption.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(command))).Message);
        Assert.Equal("UNKNOWN", (await service.OperationAsync(command.OperationId, false)).State);
        Assert.Equal(verified.OriginalJson, (await service.ReadAsync(verified.Reference.Id, null)).OriginalJson);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var outcomes = await Task.WhenAll(service.CommandAsync(command), EngineeringMappings(h, peer).CommandAsync(command));
        Assert.Equal(outcomes[0], outcomes[1]);
        Assert.Equal(outcomes[0], (await service.OperationAsync(command.OperationId, false)).Result);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => service.CommandAsync(command with { ExpectedVersion = original.Reference.Version }))).Message);
        var confirmed = await service.ReadAsync(original.Reference.Id, null);
        Assert.Equal(original.Reference, confirmed.LegacyConfirmation!.OriginalMappingRef);
        Assert.Equal(confirmed.Reference, confirmed.LegacyConfirmation.ConfirmedMappingRef);

        await using var foreign = await Seed();
        await GrantMappingRead(foreign);
        var foreignEngineering = await MakeEngineering(foreign);
        Assert.Equal(engineering.Product.Code, foreignEngineering.Product.Code);
        var foreignService = EngineeringMappings(foreign);
        Assert.Equal("UNKNOWN", (await foreignService.OperationAsync(command.OperationId, false)).State);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => foreignService.ReadAsync(confirmed.Reference.Id, null))).Status);
        var foreignCandidate = await MappingCandidate(foreign, foreignEngineering, "SAME");
        var foreignDraft = await MakeMapping(foreign, foreignCandidate with { LegalEntity = ProductMappingLegal("SIM-OWN") });
        Assert.Equal("ENGINEERING_LEGAL_ENTITY_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => foreignService.CommandAsync(MappingAction(foreignDraft, "REVIEW")))).Message);
        Assert.Equal("DRAFT", (await foreignService.ReadAsync(foreignDraft.Reference.Id, null)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_wp1047_engineering_sequence_pins_original_mapping_and_current_revision_and_authority()
    {
        await using var h = await Seed();
        var initial = await BomParent(h);
        await GrantProductIdentity(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        await EngineeringAction(h, "space-site", "query");
        await SeedMappingLegalScope(h, "SIM-ENG");
        var revisions = BomRevisions(h);
        await revisions.CommandAsync(PatchRevision(initial, new EngineeringDraftPatchV1
        { Op = "SET_CONFIGURATION", Configuration = new([], "APPLIES", "隔离工程草稿配置") }));
        var parent = await revisions.ReadAsync(initial.Reference.Id, null);
        var engineering = await Engineering(h).ReadAsync(parent.Draft.ProductRef!.Id, null);
        var candidate = await MappingCandidate(h, engineering, "ENG5");
        var service = new EngineeringProductMappingService(h.Db, BomAuthority(h));
        var old = await MakeMapping(h, candidate);
        var scoped = candidate with { LegalEntity = ProductMappingLegal("SIM-ENG"), LegacyMappingRef = old.Reference,
            TechnicalScope = new(parent.Reference, parent.Draft.Configuration!.Reference) };
        var draftResult = await service.CommandAsync(MappingAction(old, "DRAFT") with { Candidate = scoped });
        var mapping = await ConfirmReviewedMapping(service, await service.ReadAsync(old.Reference.Id, draftResult.Version));
        Assert.Equal("CONFIRMED", mapping.TechnicalApplicability);
        Assert.Equal(scoped.TechnicalScope, mapping.ConfirmedTechnicalScope);

        var code = await h.Db.ProductMasters.Where(row => row.Id == scoped.BusinessRef!.Id).Select(row => row.ProductCd).SingleAsync();
        h.Db.ProductMaterials.Add(new ProductMaterial { ProductCd = code, ProcessCd = "CUT", MaterialCd = "SIM-MAT",
            UnitUsage = 1.23456789m, UsageUnit = "个", UsageType = 2 });
        h.Db.ProductProcesses.Add(new ProductProcess { ProductCd = code, TaskCd = "CUT-1", ProcessCd = "CUT", SortOrder = 10, SetupHour = 2.12345678m });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var boms = Boms(h);
        var preview = await boms.LegacyPreviewAsync(mapping.Reference);
        var bom = BomCreate(parent);
        var input = bom with { Body = bom.Body with { SiteRef = scoped.SiteRef, Occurrences = preview.SuggestedOccurrences },
            LegacyMappingRef = mapping.Reference, LegacyDigest = preview.Digest };
        Assert.Equal("ENGINEERING_MAPPING_USAGE_SCOPE_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => boms.CommandAsync(input with { Body = input.Body with { SiteRef = null } }))).Message);
        var alternate = await MakeRevision(revisions, engineering.Reference);
        Assert.Equal("ENGINEERING_MAPPING_USAGE_SCOPE_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => boms.CommandAsync(input with { Body = input.Body with { RevisionOrigin = alternate.Reference } }))).Message);
        await boms.CommandAsync(input);
        var original = await boms.ReadAsync(input.BomId, "1");
        Assert.Equal(mapping.Reference, original.Legacy!.MappingRef);
        Assert.Equal(scoped.BusinessRef, original.Legacy.ProductRef);
        Assert.Equal("1.23456789", Assert.Single(original.Body.Occurrences).Quantity);
        var routes = Routings(h);
        var routePreview = await routes.LegacyPreviewAsync(mapping.Reference);
        var routing = RoutingCreate(parent);
        routing = routing with { Body = routing.Body with { SiteRef = scoped.SiteRef, Operations = routePreview.SuggestedOperations },
            LegacyMappingRef = mapping.Reference, LegacyDigest = routePreview.Digest };
        await routes.CommandAsync(routing);
        var originalRouting = await routes.ReadAsync(routing.RoutingId, "1");
        Assert.Equal(mapping.Reference, originalRouting.Legacy!.MappingRef);
        Assert.Equal("2.12345678", Assert.Single(originalRouting.Body.Operations).Parameters.Single(parameter => parameter.Name == "SetupHour").Value);
        Assert.Equal("DRAFT", parent.State);
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());

        await revisions.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1
        { Op = "SET_CONFIGURATION", Configuration = new([], "APPLIES", "修订后的合成配置") }));
        Assert.Equal("ENGINEERING_MAPPING_TECHNICAL_SCOPE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => boms.LegacyPreviewAsync(mapping.Reference))).Message);
        Assert.Equal(original.OriginalJson, (await boms.ReadAsync(input.BomId, "1")).OriginalJson);
        Assert.Equal(originalRouting.OriginalJson, (await routes.ReadAsync(routing.RoutingId, "1")).OriginalJson);
        var historicalMapping = await service.ReadAsync(mapping.Reference.Id, mapping.Reference.Version);
        Assert.Equal(mapping.OriginalJson, historicalMapping.OriginalJson);
        Assert.False(historicalMapping.CanAdoptIdentitySite);
        Assert.Equal(scoped.TechnicalScope, historicalMapping.ConfirmedTechnicalScope);
        var menu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "erp-product").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == menu && row.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => boms.ReadAsync(input.BomId, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => routes.ReadAsync(routing.RoutingId, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadAsync(mapping.Reference.Id, mapping.Reference.Version))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_r1_bom_revise_rechecks_inherited_mapping_scope_and_keeps_original_snapshot()
    {
        await using var h = await Seed();
        var (parent, mapping, code) = await ScopedLegacyMapping(h);
        h.Db.ProductMaterials.Add(new ProductMaterial { ProductCd = code, ProcessCd = "CUT", MaterialCd = "SIM-MAT",
            UnitUsage = 1.23456789m, UsageUnit = "个", UsageType = 2 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var owner = Boms(h);
        var preview = await owner.LegacyPreviewAsync(mapping.Reference);
        var create = BomCreate(parent);
        await owner.CommandAsync(create with { Body = create.Body with { SiteRef = mapping.Candidate.SiteRef, Occurrences = preview.SuggestedOccurrences },
            LegacyMappingRef = mapping.Reference, LegacyDigest = preview.Digest });
        var original = await owner.ReadAsync(create.BomId, "1");
        var revise = BomRevise(original);
        var wrongSite = revise with { Body = revise.Body with { SiteRef = await OtherScopeSite(h) } };
        Assert.Equal("ENGINEERING_MAPPING_USAGE_SCOPE_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => owner.CommandAsync(wrongSite))).Message);
        Assert.Equal(original.Reference, (await owner.ReadAsync(create.BomId)).Reference);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(wrongSite.OperationId, false)).State);

        await h.Db.ProductMaterials.Where(row => row.ProductCd == code).ExecuteUpdateAsync(change => change.SetProperty(row => row.UnitUsage, 9m));
        var matching = revise with { OperationId = Guid.NewGuid(), Body = revise.Body with { Name = "同一适用域的结构修订" } };
        var receipt = await owner.CommandAsync(matching);
        var revised = await owner.ReadAsync(create.BomId);
        Assert.Equal("2", revised.Reference.Version);
        Assert.NotNull(revised.Legacy);
        Assert.Equal(JsonSerializer.Serialize(original.Legacy), JsonSerializer.Serialize(revised.Legacy));

        var newParent = await ChangeScopeRevision(h, parent, "另一版本的合成配置");
        var wrongRevision = BomRevise(revised) with { Body = revised.Body with { RevisionOrigin = newParent.Reference } };
        Assert.Equal("ENGINEERING_MAPPING_TECHNICAL_SCOPE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => owner.CommandAsync(wrongRevision))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(wrongRevision.OperationId, false)).State);
        Assert.Equal(receipt, await owner.CommandAsync(matching));
        Assert.Equal(original.OriginalJson, (await owner.ReadAsync(create.BomId, "1")).OriginalJson);
        Assert.Equal(revised.OriginalJson, (await owner.ReadAsync(create.BomId)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_r1_routing_revise_rechecks_inherited_mapping_scope_and_keeps_original_snapshot()
    {
        await using var h = await Seed();
        var (parent, mapping, code) = await ScopedLegacyMapping(h);
        h.Db.ProductProcesses.Add(new ProductProcess { ProductCd = code, TaskCd = "CUT-1", ProcessCd = "CUT", SortOrder = 10, SetupHour = 2.12345678m });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var owner = Routings(h);
        var preview = await owner.LegacyPreviewAsync(mapping.Reference);
        var create = RoutingCreate(parent);
        await owner.CommandAsync(create with { Body = create.Body with { SiteRef = mapping.Candidate.SiteRef, Operations = preview.SuggestedOperations },
            LegacyMappingRef = mapping.Reference, LegacyDigest = preview.Digest });
        var original = await owner.ReadAsync(create.RoutingId, "1");
        var revise = RoutingRevise(original);
        var wrongSite = revise with { Body = revise.Body with { SiteRef = await OtherScopeSite(h) } };
        Assert.Equal("ENGINEERING_MAPPING_USAGE_SCOPE_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => owner.CommandAsync(wrongSite))).Message);
        Assert.Equal(original.Reference, (await owner.ReadAsync(create.RoutingId)).Reference);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(wrongSite.OperationId, false)).State);

        await h.Db.ProductProcesses.Where(row => row.ProductCd == code).ExecuteUpdateAsync(change => change.SetProperty(row => row.SetupHour, 9m));
        var matching = revise with { OperationId = Guid.NewGuid(), Body = revise.Body with { Name = "同一适用域的路线修订" } };
        var receipt = await owner.CommandAsync(matching);
        var revised = await owner.ReadAsync(create.RoutingId);
        Assert.Equal("2", revised.Reference.Version);
        Assert.NotNull(revised.Legacy);
        Assert.Equal(JsonSerializer.Serialize(original.Legacy), JsonSerializer.Serialize(revised.Legacy));

        var newParent = await ChangeScopeRevision(h, parent, "另一版本的合成配置");
        var wrongRevision = RoutingRevise(revised) with { Body = revised.Body with { RevisionOrigin = newParent.Reference } };
        Assert.Equal("ENGINEERING_MAPPING_TECHNICAL_SCOPE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => owner.CommandAsync(wrongRevision))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(wrongRevision.OperationId, false)).State);
        Assert.Equal(receipt, await owner.CommandAsync(matching));
        Assert.Equal(original.OriginalJson, (await owner.ReadAsync(create.RoutingId, "1")).OriginalJson);
        Assert.Equal(revised.OriginalJson, (await owner.ReadAsync(create.RoutingId)).OriginalJson);
    }

    private static async Task<(EngineeringRevisionSnapshotV1 Parent, EngineeringMappingVersionV1 Mapping, string ProductCode)> ScopedLegacyMapping(Harness h)
    {
        var initial = await BomParent(h);
        await GrantProductIdentity(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        await EngineeringAction(h, "space-site", "query");
        const string legalKey = "SIM-R1-LEGACY";
        await SeedMappingLegalScope(h, legalKey);
        var parent = await ChangeScopeRevision(h, initial, "映射确认时的合成配置");
        var engineering = await Engineering(h).ReadAsync(parent.Draft.ProductRef!.Id, null);
        var candidate = await MappingCandidate(h, engineering, "R1MAP");
        var service = new EngineeringProductMappingService(h.Db, BomAuthority(h));
        var draft = await service.CommandAsync(MappingDraft(candidate with
        {
            LegalEntity = ProductMappingLegal(legalKey),
            TechnicalScope = new(parent.Reference, parent.Draft.Configuration!.Reference)
        }));
        var mapping = await ConfirmReviewedMapping(service, await service.ReadAsync(draft.ObjectId, draft.Version));
        var code = await h.Db.ProductMasters.Where(row => row.Id == candidate.BusinessRef!.Id).Select(row => row.ProductCd).SingleAsync();
        return (parent, mapping, code);
    }

    private static async Task<EngineeringRevisionSnapshotV1> ChangeScopeRevision(Harness h, EngineeringRevisionSnapshotV1 parent, string evidence)
    {
        var owner = BomRevisions(h);
        await owner.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new([], "APPLIES", evidence) }));
        return await owner.ReadAsync(parent.Reference.Id, null);
    }

    private static async Task<EngineeringSourceRefV1> OtherScopeSite(Harness h)
    {
        const string code = "R1B";
        var site = await PhysicalSite(h, code);
        return (await EngineeringMappings(h).SiteChoicesAsync(code)).Single(row => row.Reference.Id == site).Reference;
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_erp_entry_resolves_native_product_uuid_and_engineering_authority_cannot_edit_price_or_stock()
    {
        await using var h = await Seed();
        await GrantMappingRead(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        var id = await SeedIdentityProduct(h, "PLM");
        var code = await h.Db.ProductMasters.Where(row => row.Id == id).Select(row => row.ProductCd).SingleAsync();
        h.Db.ProductLotPrices.Add(new() { ProductCd = code, DetailNo = 1, CurrentUnitPrice = 8.12500001m });
        h.Db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "L1", ProductCd = code,
            PhysicalQty = 2.12500001m, AvailableQty = 2.12500001m });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var products = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/products?productCdFrom={code}&productCdTo={code}"));
        Assert.True(products.StatusCode == HttpStatusCode.OK, await OperationsFailure(products));
        using var json = JsonDocument.Parse(await products.Content.ReadAsStringAsync());
        Assert.Equal(id, Assert.Single(json.RootElement.GetProperty("data").GetProperty("rows").EnumerateArray()).GetProperty("productId").GetGuid());

        using var price = await host.Send(OperationsRequest(cookies, HttpMethod.Put, $"api/products/{code}", new
        { productCd = code, lotPrices = new[] { new { detailNo = 1, currentUnitPrice = 9.12500001m } } }));
        Assert.Equal(HttpStatusCode.Forbidden, price.StatusCode);
        using var stock = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/stock/commands/v1/apply",
            new StockApplyCommandV1 { OperationId = Guid.NewGuid(), WarehouseCd = "W1", LocationCd = "L1", ProductCd = code, Qty = 1m, TxnType = "ADJ" }));
        Assert.Equal(HttpStatusCode.Forbidden, stock.StatusCode);
        Assert.Equal(8.12500001m, await h.Db.ProductLotPrices.Where(row => row.ProductCd == code).Select(row => row.CurrentUnitPrice).SingleAsync());
        Assert.Equal(2.12500001m, await h.Db.Stocks.Where(row => row.ProductCd == code).Select(row => row.PhysicalQty).SingleAsync());
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0502_mapping_forward_index_preserves_old_original_receipt_and_current_permissions()
    {
        var database = await server.CreateIsolatedDatabaseAsync("20261126031000_ResourceAssignmentAndCapabilityApprovalV1");
        await using var h = await SeedForDatabase(database);
        await GrantMappingRead(h);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        var service = EngineeringMappings(h);
        var engineering = await MakeEngineering(h);
        var draft = await MakeMapping(h, await MappingCandidate(h, engineering, "OLD5"));
        var command = MappingAction(draft, "CONFIRM");
        var receipt = await service.CommandAsync(command);
        var original = await service.ReadAsync(receipt.ObjectId, receipt.Version);
        var identityReader = new CommercialProductIdentityReader(h.Db, EngineeringAuthority(h));
        var oldProduct = await identityReader.ReadAsync(draft.Candidate.BusinessRef!.Id);
        Assert.Equal("UNKNOWN", oldProduct.LegacyMappingKnowledge);
        var menu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "erp-product").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == menu && row.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.OperationAsync(command.OperationId, false))).Status);

        const string migration = "20261126120000_EngineeringMappingBusinessReferenceV1";
        Assert.Contains(migration, await h.Db.Database.GetPendingMigrationsAsync());
        await h.Db.GetService<IMigrator>().MigrateAsync(migration);
        Assert.Contains(migration, await h.Db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.OperationAsync(command.OperationId, false))).Status);
        await EngineeringAction(h, "erp-product", "query");
        var read = await service.ReadAsync(receipt.ObjectId, receipt.Version);
        Assert.Equal(original.Reference, read.Reference);
        Assert.Equal(original.OriginalJson, read.OriginalJson);
        Assert.Equal(original.BusinessConfirmation, read.BusinessConfirmation);
        var sameProduct = await identityReader.ReadAsync(oldProduct.ProductId);
        Assert.Equal(oldProduct.Reference, sameProduct.Reference);
        Assert.Equal(oldProduct.OriginalJson, sameProduct.OriginalJson);
        Assert.Equal("UNKNOWN", sameProduct.LegacyMappingKnowledge);
        Assert.Equal("REQUIRED_FENCED", sameProduct.LegalEntityApplicability);
        Assert.Equal(receipt, await service.CommandAsync(command));
        Assert.Equal(receipt, (await service.OperationAsync(command.OperationId, false)).Result);
        Assert.Equal("REQUIRED_FENCED", read.LegalEntityApplicability);
        Assert.Null(read.LegacyConfirmation);
        Assert.Null(read.Review);
    }

    private static EngineeringMappingLegalEntityV1 ProductMappingLegal(string key) => new(key, "1", $"合成 ERP 法人归属原件 {key}");

    private static async Task<EngineeringMappingVersionV1> ReviewMapping(EngineeringProductMappingService service, EngineeringMappingVersionV1 draft)
    {
        var reviewed = await service.CommandAsync(MappingAction(draft, "REVIEW"));
        return await service.ReadAsync(reviewed.ObjectId, reviewed.Version);
    }

    private static async Task<EngineeringMappingVersionV1> ConfirmReviewedMapping(EngineeringProductMappingService service, EngineeringMappingVersionV1 draft)
    {
        var reviewed = await ReviewMapping(service, draft);
        var confirmation = await service.CommandAsync(MappingAction(reviewed, "CONFIRM"));
        return await service.ReadAsync(confirmation.ObjectId, confirmation.Version);
    }

    private static Task<int> SeedMappingLegalScope(Harness h, string legal) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO main_bp.scopes(tenant_id,legal_entity_key,label,enabled,revision,norm_version,coverage_complete)
        VALUES({h.Tenant},{legal},{legal},true,1,'SIMULATED',true)
        """);
}
