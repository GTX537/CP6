using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] TenantPrivacyActions=["tenant-read","tenant-export","tenant-download","tenant-revoke","tenant-scope-identity","tenant-scope-security","tenant-scope-roles"];
    private static async Task TenantPrivacyAuthority(Harness h)
    {await PrivacyAuthority(h);foreach(var action in TenantPrivacyActions)await EngineeringAction(h,"privacy-export",action);h.Db.ChangeTracker.Clear();}
    private static TenantPrivacyExportCommandV1 TenantPrivacyCommand(Harness h)=>new(Guid.NewGuid(),Guid.NewGuid(),h.Tenant,"TENANT_ACCESS_REQUEST",["identity","security","roles"],h.User,PrivacyExportOwnerV1.TenantPolicy,60);
    private static Task<int> TenantPrivacyCount(Harness h,string table)=>h.Db.Database.SqlQueryRaw<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.{table} WHERE actor_tenant='"+h.Tenant+"'").SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_actual_cross_tenant_all_classified_rows_exclude_operator_secrets_and_unknown_content_with_honest_manifest()
    {
        await using var h=await Seed();await TenantPrivacyAuthority(h);await using var target=await Seed();
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,"operator-only@example.invalid"));
        await target.Db.Sys_Users.Where(x=>x.Id==target.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,"target-private@example.invalid").SetProperty(u=>u.TwoFactorSecret,"never-export-factor-secret"));
        target.Db.Sys_Users.Add(new(){UserName="second-target-user",Password="never-export-password",RoleId=72,Email="second-target@example.invalid"});
        target.Db.Sys_UserRoles.Add(new(){UserId=target.User,RoleId=72});await target.Db.SaveChangesAsync();target.Db.ChangeTracker.Clear();
        var included=await SecurityFieldLog(target,target.User);var anonymous=await SecurityFieldLog(target,null);var excluded=await SecurityFieldLog(h,h.User);
        var o=PrivacyOwner(h);var s=PrivacySession(h);var saved=await o.GenerateTenantAsync(TenantPrivacyCommand(h) with{TargetTenantId=target.Tenant},s);var receipt=saved.Receipt!;var data=await o.DownloadTenantAsync(receipt.ArtifactId,s);
        Assert.Equal(target.Tenant,receipt.TargetTenantId);Assert.Equal(h.Tenant,receipt.RequesterTenantId);Assert.Equal("PARTIAL",receipt.Manifest.State);Assert.False(receipt.Manifest.Complete);
        using var package=JsonDocument.Parse(data.Bytes);var root=package.RootElement;Assert.Equal(target.Tenant,root.GetProperty("tenant").GetProperty("Id").GetGuid());
        var users=root.GetProperty("users").EnumerateArray().ToArray();Assert.Equal(2,users.Length);Assert.All(users,u=>Assert.Equal(target.Tenant,u.GetProperty("TenantId").GetGuid()));Assert.DoesNotContain(users,u=>u.GetProperty("Id").GetGuid()==h.User);
        var logs=root.GetProperty("securityLogs").EnumerateArray().Select(x=>x.GetProperty("Id").GetGuid()).ToArray();Assert.Contains(included,logs);Assert.Contains(anonymous,logs);Assert.DoesNotContain(excluded,logs);
        Assert.Single(root.GetProperty("roleAssignments").EnumerateArray());Assert.False(root.GetProperty("manifest").TryGetProperty("subjectId",out _));Assert.False(root.GetProperty("scope").TryGetProperty("subjectId",out _));
        var text=System.Text.Encoding.UTF8.GetString(data.Bytes);Assert.Contains("target-private@example.invalid",text);Assert.DoesNotContain("operator-only@example.invalid",text);Assert.DoesNotContain("never-export-factor-secret",text);Assert.DoesNotContain("never-export-password",text);Assert.DoesNotContain("unclassified-context-remains",text);Assert.DoesNotContain("unclassified-device-remains",text);
        Assert.Equal(2,Assert.Single(receipt.Manifest.Sources,x=>x.Source=="Sys_User").RecordCount);Assert.Contains(receipt.Manifest.Sources,x=>x.Owner=="CRM"&&x.Status=="UNKNOWN_SCOPE"&&x.RecordCount==null);Assert.Contains(receipt.Manifest.Sources,x=>x.Source=="Sys_SecurityLog"&&x.ReadStartedAtUtc<=x.ReadCompletedAtUtc&&x.Selector=="TARGET_TENANT_ALL_ROWS");
        Assert.Equal(data.Bytes.LongLength,receipt.Length);Assert.Equal(Convert.ToHexString(SHA256.HashData(data.Bytes)).ToLowerInvariant(),receipt.Sha256);
        Assert.Equal(1,await TenantPrivacyCount(h,"commands"));Assert.Equal(1,await TenantPrivacyCount(h,"artifacts"));Assert.Equal(1,await TenantPrivacyCount(h,"payloads p JOIN cp6_privacy_tenant_export_v1.artifacts a ON a.id=p.artifact_id"));
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_disabled_empty_target_has_real_zero_rows_while_unrequested_and_unknown_sources_remain_distinct()
    {
        await using var h=await Seed();await TenantPrivacyAuthority(h);var empty=Guid.NewGuid();h.Db.Sys_Tenants.Add(new(){Id=empty,TenantCode="privacy_empty_"+empty.ToString("N"),TenantName="模拟停用空租户",Enable=false,ExpireDate=DateTime.Now.AddDays(-1)});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var o=PrivacyOwner(h);var s=PrivacySession(h);var saved=await o.GenerateTenantAsync(TenantPrivacyCommand(h) with{TargetTenantId=empty,Sources=["identity","security"]},s);var receipt=saved.Receipt!;
        Assert.Equal(0,Assert.Single(receipt.Manifest.Sources,x=>x.Source=="Sys_User").RecordCount);Assert.Equal(0,Assert.Single(receipt.Manifest.Sources,x=>x.Source=="Sys_SecurityLog").RecordCount);
        var roles=Assert.Single(receipt.Manifest.Sources,x=>x.Source=="Sys_UserRole");Assert.Equal("EXCLUDED_WITH_REASON",roles.Status);Assert.Equal("OUTSIDE_REQUESTED_SCOPE",roles.Reason);Assert.Null(roles.RecordCount);
        Assert.Contains(receipt.Manifest.Sources,x=>x.Owner=="FILE"&&x.Status=="UNKNOWN_SCOPE"&&x.RecordCount==null);
        Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateTenantAsync(TenantPrivacyCommand(h) with{TargetTenantId=Guid.NewGuid()},s))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateTenantAsync(TenantPrivacyCommand(h) with{Sources=["ALL"]},s))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateTenantAsync(TenantPrivacyCommand(h) with{Purpose="SUBJECT_ACCESS_REQUEST"},s))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_exact_original_survives_export_withdrawal_and_close_blocks_late_generate_without_new_data_capture()
    {
        await using var h=await Seed();await TenantPrivacyAuthority(h);var o=PrivacyOwner(h);var s=PrivacySession(h);var input=TenantPrivacyCommand(h);var saved=await o.GenerateTenantAsync(input,s);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.NickName,"changed after captured tenant export"));await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="tenant-export").ExecuteDeleteAsync();
        Assert.Equal(JsonSerializer.Serialize(saved),JsonSerializer.Serialize(await o.GenerateTenantAsync(input,s)));Assert.Equal(JsonSerializer.Serialize(saved),JsonSerializer.Serialize(await o.TenantOperationAsync(input.OperationId,false,s)));
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateTenantAsync(input with{RequestRef=Guid.NewGuid()},s))).Status);
        var late=TenantPrivacyCommand(h);Assert.Equal("NOT_FOUND",(await o.TenantOperationAsync(late.OperationId,false,s)).State);Assert.Equal("CLOSED",(await o.TenantOperationAsync(late.OperationId,true,s)).State);Assert.Equal("CLOSED",(await o.GenerateTenantAsync(late,s)).State);
        Assert.Equal(1,await TenantPrivacyCount(h,"artifacts"));Assert.Equal("NOT_FOUND",(await o.OperationAsync(input.OperationId,s)).State);Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.ReadArtifactAsync(saved.Receipt!.ArtifactId,s))).Status);
    }
}
