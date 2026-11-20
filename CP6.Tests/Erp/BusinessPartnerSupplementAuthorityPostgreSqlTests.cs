using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_enforces_all_creation_entry_authorities()
    {
        await using var h = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, bridge);
        var account = Guid.NewGuid();
        bridge.Account(account, "00000367");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, bridge.Environment());
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var registered = WorkspaceInput(options, "00000038", "普通正式创建", registration: "REGISTERED", complete: true);
        var ordinary = await WorkspacePreview(host, cookies, registered);
        Assert.True(ordinary.Body.GetProperty("canApply").GetBoolean());
        var proof = await SignedAccountObservation(host, cookies, account);
        var context = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("00000368", "关联正式创建"));
        object PartnerDraft(string code, string name, string registration)
        {
            var data = JsonSerializer.SerializeToElement(WorkspaceInput(options, code, name, registration: registration, complete: true), MappingJson);
            return new { registration, identity = data.GetProperty("identity"), duplicateDecision = data.GetProperty("duplicateDecision") };
        }
        var mappingDraft = PartnerDraft("00000368", "关联正式创建", "REGISTERED");
        var mappingIntent = MappingInput(context, "CREATE_AND_LINK", null, "模拟授权边界", mappingDraft);
        var mapping = await GoodMapPreview(host, cookies, mappingIntent);
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == 212 && x.ActionCode == "register").ExecuteDeleteAsync();
        var limited = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        Assert.Equal("PRE_REGISTERED", Assert.Single(limited.Body.GetProperty("bases")[0].GetProperty("createModes").EnumerateArray()).GetString());
        await WorkspacePreview(host, cookies, registered, 403);
        await ApplyWorkspace(host, cookies, ordinary, Guid.NewGuid(), 403);
        await ApplyMap(host, cookies, mapping, Guid.NewGuid(), 403);
        context = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("00000368", "关联正式创建"));
        await MapPreview(host, cookies, MappingInput(context, "CREATE_AND_LINK", null, "模拟授权边界", mappingDraft), 403);
        await GrantPartnerActions(h, "register");

        var pre = WorkspaceInput(options, "00000039", "模拟只读据点");
        var basePreview = await WorkspacePreview(host, cookies, pre);
        await SetSampleBaseGrant(h, false);
        var onlyRead = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        Assert.False(onlyRead.Body.GetProperty("bases")[0].GetProperty("canCreate").GetBoolean());
        var baseRefused = await WorkspacePreview(host, cookies, pre, 403);
        Assert.Equal("ORG_SCOPE_DENIED", baseRefused.Body.GetProperty("code").GetString());
        Assert.Equal("ORG_SCOPE_DENIED", (await ApplyWorkspace(host, cookies, basePreview, Guid.NewGuid(), 403)).Body.GetProperty("code").GetString());
        await SetSampleBaseGrant(h, true);

        foreach (var field in new[] { "BpAbbrev", "PaidEachTimeFlg", "Relations" })
        {
            var saved = await WorkspacePreview(host, cookies, pre);
            h.Db.Sys_RoleFieldPerms.Add(new() { ResourceKey = PartnerResource, RoleId = 72, FieldName = field, Access = 2 });
            await h.Db.SaveChangesAsync();
            var restricted = await WorkspacePreview(host, cookies, pre, 403);
            Assert.Equal("FIELD_DENIED", restricted.Body.GetProperty("code").GetString());
            Assert.Equal("FIELD_DENIED", (await ApplyWorkspace(host, cookies, saved, Guid.NewGuid(), 403)).Body.GetProperty("code").GetString());
            await h.Db.Sys_RoleFieldPerms.Where(item => item.FieldName == field).ExecuteDeleteAsync();
            h.Db.ChangeTracker.Clear();
        }
        var finance = JsonSerializer.SerializeToNode(pre, MappingJson)!;
        finance["identity"]!["profile"]!["remittanceAccount"] = "TEST BANK";
        var bankDenied = await WorkspacePreview(host, cookies, finance, 400);
        Assert.DoesNotContain("TEST BANK", bankDenied.Body.GetRawText());
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await FormalCounts(h));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await MappingCounts(h));
        await DeviceEvidence("wp04-38-creation-grants.json", new
        {
            ordinaryCreate = new[] { 403, 403 }, createAndLink = new[] { 403, 403 }, readOnlyBase = "ORG_SCOPE_DENIED",
            missingWrites = new[] { "BpAbbrev:null", "PaidEachTimeFlg:false", "Relations:nullableSlots" },
            financeRaw = bankDenied.Status, businessWrites = 0
        });
    }

    private async Task SetSampleBaseGrant(Harness h, bool canManage)
    {
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await BpUseScopeGate.LockAsync(h.Db, "JP-A", true, default);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.base_grants SET can_manage={canManage}
            WHERE tenant_id={h.Tenant} AND legal_entity_key='JP-A' AND base_cd='BPA' AND role_id=72
            """);
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_command_auditor_reads_scoped_result_without_replaying_another_actor()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await GrantPartnerActions(h, "command-result.audit.read");
        h.Db.Sys_Roles.Add(new() { RoleId = 73, RoleName = "模拟原结果审计" });
        h.Db.Sys_Users.Add(new Sys_User { UserName = "receipt-audit", RoleId = 73, Password = new BCryptPasswordHasher(4).Hash("Price1!Password") });
        foreach (var action in new[] { "query", "profile-query", "command-result.audit.read" })
            h.Db.Sys_RoleActions.Add(new() { RoleId = 73, MenuId = 212, ActionCode = action });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 73, ResourceKey = PartnerResource, ScopeType = 5 });
        await h.Db.SaveChangesAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.base_grants VALUES({h.Tenant},'JP-A','BPA',73,true,false)
            """);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var operatorCookies = await Login(host, h);
        var auditedCookies = await Login(host, "receipt-audit", "Price1!Password", h.Tenant.ToString("N"));
        var options = await WorkspaceCall(host, operatorCookies, "/options?legalEntityKey=JP-A");
        var preview = await WorkspacePreview(host, operatorCookies, WorkspaceInput(options, "AUDIT-38", "模拟原结果审计"));
        var command = Guid.NewGuid();
        var applied = await ApplyWorkspace(host, operatorCookies, preview, command);
        var before = await FormalCounts(h);
        var audit = await WorkspaceCall(host, auditedCookies, $"/commands/{command:D}");
        Assert.Equal("FOUND", audit.Body.GetProperty("knowledge").GetString());
        Assert.Equal(applied.Body.GetProperty("receipt").GetRawText(), audit.Body.GetProperty("receipt").GetRawText());
        await ApplyWorkspace(host, auditedCookies, preview, command, 404);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.base_grants SET can_read=false
            WHERE tenant_id={h.Tenant} AND role_id=73 AND legal_entity_key='JP-A'
            """);
        var hidden = await WorkspaceCall(host, auditedCookies, $"/commands/{command:D}");
        var never = await WorkspaceCall(host, auditedCookies, $"/commands/{Guid.NewGuid():D}");
        Assert.Equal(never.Body.GetRawText(), hidden.Body.GetRawText());
        Assert.Equal("NOT_OBSERVED", hidden.Body.GetProperty("knowledge").GetString());
        await ApplyWorkspace(host, auditedCookies, preview, command, 404);
        Assert.Equal(before, await FormalCounts(h));
        await DeviceEvidence("wp04-38-command-audit.json", new { auditIndependent = true, replay = 404, hiddenMatchesAbsent = true, writes = 0 });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_replays_both_no_change_and_preexisting_rejection_after_conditions_change()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var preview = await WorkspacePreview(host, cookies, WorkspaceInput(options, "TERMINAL", "模拟已有终态",
            overrides: new() { ["businessStaffCd"] = "STB" }));
        var bp = PartnerId(await ApplyWorkspace(host, cookies, preview, Guid.NewGuid()));
        var state = await WorkspaceStatus(host, cookies, bp);
        var hold = new { bpId = bp, expectedRevision = "1", reason = "模拟原次保持解除", hold = "CLEAR" };
        var sameKey = Guid.NewGuid();
        var unchanged = MappingReceipt(await WorkspaceCall(host, cookies, "/hold", HttpMethod.Post, hold, state.ETag, sameKey));
        Assert.Equal("NO_CHANGE", unchanged.GetProperty("outcome").GetString());
        var refuseKey = Guid.NewGuid();
        var register = new { bpId = bp, expectedRevision = "1", reason = "模拟初次资料不齐" };
        var refused = await WorkspaceCall(host, cookies, "/register", HttpMethod.Post, register, state.ETag, refuseKey, 422);
        Assert.Equal("PROFILE_INCOMPLETE", refused.Body.GetProperty("code").GetString());
        var coreReceipt = new
        {
            commandId = refuseKey, operation = "REGISTER", outcome = "REJECTED",
            bp = state.Body.GetProperty("bp"), mapping = (object?)null, reconcile = (object?)null,
            errorCode = "PROFILE_INCOMPLETE", completedAt = MappingInstant()
        };
        var hash = MappingHash(coreReceipt);
        var receipt = JsonSerializer.SerializeToNode(coreReceipt, MappingJson)!.AsObject();
        receipt["receiptHash"] = hash;
        var fingerprint = System.Security.Cryptography.SHA256.HashData(BusinessPartnerWorkspaceV1.Canonical(new
        {
            actor = h.User, operation = "REGISTER", ifMatch = state.ETag, body = register, path = FormalPartnerApi + "/register"
        }));
        // 模拟已有 Owner 的持久拒绝终态。读取和重放均由真实 Main 执行。
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.commands(tenant_id,command_id,actor_id,legal_entity_key,bp_id,operation,fingerprint,receipt)
                VALUES({h.Tenant},{refuseKey},{h.User},'JP-A',{bp},'REGISTER',{fingerprint},{receipt.ToJsonString()})
            """);
        await StateCommand(host, cookies, bp, state, "freeze", Guid.NewGuid());
        var fill = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ProfilePatch(bp, "2", "salesStaffCd", "STA"));
        await ApplyWorkspace(host, cookies, fill, Guid.NewGuid(), path: "/profile-apply");
        var newState = await WorkspaceStatus(host, cookies, bp);
        Assert.Equal("3", CurrentRevision(newState));
        Assert.Equal("PRE_REGISTERED", newState.Body.GetProperty("state").GetProperty("registration").GetString());
        Assert.Equal("FROZEN", newState.Body.GetProperty("state").GetProperty("hold").GetString());
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == 212 &&
            (x.ActionCode == "register" || x.ActionCode == "status")).ExecuteDeleteAsync();
        var before = await FormalCounts(h);
        var replaySame = await WorkspaceCall(host, cookies, "/hold", HttpMethod.Post, hold, state.ETag, sameKey);
        Assert.Equal(unchanged.GetRawText(), MappingReceipt(replaySame).GetRawText());
        var replayRefuse = await WorkspaceCall(host, cookies, "/register", HttpMethod.Post, register, state.ETag, refuseKey);
        Assert.Equal("REJECTED", MappingReceipt(replayRefuse).GetProperty("outcome").GetString());
        Assert.Equal(hash, MappingReceipt(replayRefuse).GetProperty("receiptHash").GetString());
        Assert.Equal("PROFILE_INCOMPLETE", MappingReceipt(replayRefuse).GetProperty("errorCode").GetString());
        foreach (var (key, original) in new[] { (sameKey, replaySame), (refuseKey, replayRefuse) })
        {
            Assert.True(original.Body.GetProperty("replayed").GetBoolean());
            Assert.Equal(MappingReceipt(original).GetRawText(),
                MappingReceipt(await WorkspaceCall(host, cookies, $"/commands/{key:D}")).GetRawText());
        }
        Assert.Equal(before, await FormalCounts(h));
        await GrantPartnerActions(h, "register");
        await StateCommand(host, cookies, bp, newState, "register", Guid.NewGuid());
        Assert.Equal("REGISTERED", (await WorkspaceStatus(host, cookies, bp)).Body.GetProperty("state").GetProperty("registration").GetString());
        await DeviceEvidence("wp04-38-old-terminals.json", new
        {
            actualNoChange = true, seededRejected = true, originalRejection = hash,
            currentConditionsChanged = true, readAfterWriteRevoked = true, replayEffects = 0, newKeyRegisters = true
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_rejects_the_same_foreign_uuid_at_service_and_tenant_foreign_keys()
    {
        await using var local = await Seed();
        await using var foreign = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(local.Tenant);
        await EnableMapping(local, bridge);
        await ProvisionWorkspace(foreign);
        await GrantPartnerActions(local, "export");
        local.Db.Sys_Users.Add(new Sys_User { UserName = "other-creator", RoleId = 72, Password = new BCryptPasswordHasher(4).Hash("Price1!Password") });
        await local.Db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(local.Db.Database.GetConnectionString()!, bridge.Environment());
        var cookies = await Login(host, local);
        var foreignCookies = await Login(host, foreign);
        var localBp = await AdoptMappingPartner(host, cookies, "0000138", "模拟本租户伙伴");
        var other = await AdoptMappingPartner(host, foreignCookies, "0000238", "模拟异租户伙伴");
        await WorkspaceCall(host, cookies, $"/{other:D}", status: 404);
        await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ProfilePatch(other, "1", "bpName", "模拟异租户"),
            status: 404);
        var relation = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ParentPatch(localBp, "1", other));
        Assert.True(HasIssue(relation, "PARENT_CUSTOMER", "REFERENCE_INVALID"));
        var before = await FormalCounts(local);
        await ApplyWorkspace(host, cookies, relation, Guid.NewGuid(), 422, "/profile-apply");
        var account = Guid.NewGuid();
        bridge.Account(account, "0000139");
        var observation = await SignedAccountObservation(host, cookies, account);
        var mapping = await ReadMappingContext(host, cookies, account, observation, MappingCriteria("0000138", "模拟本租户伙伴"));
        var rejectedMap = await MapPreview(host, cookies, MappingInput(mapping, "LINK", other, "模拟异租户关系"));
        Assert.False(rejectedMap.Body.GetProperty("canApply").GetBoolean());
        Assert.True(HasIssue(rejectedMap, "action", "MAPPING_TARGET_NOT_SELECTABLE"));
        Assert.DoesNotContain("0000238", rejectedMap.Body.GetRawText());
        Assert.DoesNotContain(other.ToString("D"), rejectedMap.Body.GetRawText());
        var mappingApply = await ApplyMap(host, cookies, rejectedMap, Guid.NewGuid(), 412);
        Assert.Equal(before, await FormalCounts(local));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await MappingCounts(local));

        var foreignKeys = new List<string>();
        foreach (var (table, command) in new (string Table, FormattableString Command)[]
        {
            ("profile_revisions", $"""
                INSERT INTO main_bp.profile_revisions VALUES({local.Tenant},'JP-A',{other},1,decode('0000000000000001','hex'),
                  {new string('a', 64)},decode('00','hex'),current_timestamp)
                """),
            ("relations", $"""
                UPDATE main_bp.relations SET target_id={other}
                WHERE tenant_id={local.Tenant} AND legal_entity_key='JP-A' AND bp_id={localBp} AND slot='PARENT_CUSTOMER'
                """),
            ("mapping_revisions", $"""
                INSERT INTO main_bp.mapping_slots(tenant_id,legal_entity_key,account_id,scope_identity)
                    VALUES({local.Tenant},'JP-A',{account},gen_random_uuid());
                INSERT INTO main_bp.mapping_revisions(tenant_id,legal_entity_key,account_id,revision,bp_id,business_partner_key,
                    semantic_hash,identity_observation_id,command_id,recorded_at,action)
                    VALUES({local.Tenant},'JP-A',{account},1,{other},'0000238',{new string('a', 64)},
                         {observation},gen_random_uuid(),current_timestamp,'LINK')
                """)
        })
        {
            await using var tx = await local.Db.Database.BeginTransactionAsync();
            await BpUseScopeGate.LockAsync(local.Db, "JP-A", true, default);
            var violation = await Assert.ThrowsAsync<PostgresException>(() => local.Db.Database.ExecuteSqlInterpolatedAsync(command));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, violation.SqlState);
            Assert.Equal(table, violation.TableName);
            foreignKeys.Add(violation.ConstraintName!);
            await tx.RollbackAsync();
        }
        Assert.Equal(before, await FormalCounts(local));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await MappingCounts(local));

        var otherActor = await Login(host, "other-creator", "Price1!Password", local.Tenant.ToString("N"));
        await AdoptMappingPartner(host, otherActor, "0000338", "模拟同租户异对象范围");
        await local.Db.Sys_RoleDataScopes.Where(scope => scope.ResourceKey == PartnerResource)
            .ExecuteUpdateAsync(set => set.SetProperty(scope => scope.ScopeType, 1));
        using var export = await PartnerExport(host, cookies, ExportQuery());
        Assert.Equal(System.Net.HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("0000138", Assert.Single(CsvRecords(await export.Content.ReadAsByteArrayAsync()).Skip(1))[0]);
        await DeviceEvidence("wp04-38-foreign-uuid.json", new
        {
            serviceProfile = 404, serviceRelation = "REFERENCE_INVALID", serviceMapping = "MAPPING_TARGET_NOT_SELECTABLE", mappingApply.Status,
            databaseSqlState = PostgresErrorCodes.ForeignKeyViolation, foreignKeys, foreignAndUnownedRowsExcludedInExport = true, writes = 0
        });
    }
}
