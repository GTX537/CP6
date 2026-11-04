using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class PrivacyReadFailure : DbCommandInterceptor
    {
        internal bool Fail;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken=default)
        {
            if(Fail && command.CommandText.Contains("FROM \"Sys_UserRole\""))throw new InvalidOperationException("Simulated required Owner read unavailable");
            return ValueTask.FromResult(result);
        }
    }
    private static GdprService Privacy(Harness h)=>new(h.Db,null!,null!,null!,new SecurityAuditService(h.Db),new TenantContext{CurrentTenantId=h.Tenant});

    [CurrentPermissionPostgresFact]
    public async Task PrivacySubject_manifest_uses_actual_stable_sources_excludes_nested_secrets_and_preserves_unknown_owners()
    {
        await using var h=await Seed();var other=Guid.NewGuid();
        h.Db.Sys_Users.Add(new(){Id=other,UserName="other-user",Password="other-password",RoleId=72});
        var user=await h.Db.Sys_Users.SingleAsync(x=>x.Id==h.User);user.TwoFactorSecret="secret-not-exported";user.QuickPinHash="pin-not-exported";
        h.Db.Sys_UserRoles.AddRange(new(){UserId=h.User,RoleId=72},new(){UserId=other,RoleId=72});
        var log=new Sys_SecurityLog{UserId=h.User,UserName="historic-name",EventType=1,CreatedAt=DateTime.UtcNow,
            ContextJson="{\"nested\":{\"token\":\"nested-secret-not-exported\"}}",Reason="unclassified-secret-not-exported"};
        h.Db.Sys_SecurityLogs.AddRange(log,new(){UserId=other,UserName=user.UserName,CreatedAt=DateTime.UtcNow});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var stream=await Privacy(h).ExportSubjectAsync(h.User);using var doc=await JsonDocument.ParseAsync(stream);var root=doc.RootElement;
        Assert.DoesNotContain("not-exported",root.GetRawText());
        Assert.Equal("historic-name",Assert.Single(root.GetProperty("securityLogs").EnumerateArray()).GetProperty("UserName").GetString());
        Assert.Equal(h.User,Assert.Single(root.GetProperty("roleAssignments").EnumerateArray()).GetProperty("UserId").GetGuid());
        var manifest=root.GetProperty("manifest");Assert.Equal("PARTIAL",manifest.GetProperty("state").GetString());Assert.False(manifest.GetProperty("complete").GetBoolean());
        var entries=manifest.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(1,entries.Single(x=>x.GetProperty("source").GetString()=="Sys_SecurityLog").GetProperty("recordCount").GetInt32());
        var missing=entries.Where(x=>x.GetProperty("status").GetString()=="UNKNOWN_SCOPE").ToArray();Assert.NotEmpty(missing);
        Assert.All(missing,x=>Assert.Equal(JsonValueKind.Null,x.GetProperty("recordCount").ValueKind));
        Assert.Contains(entries,x=>x.GetProperty("owner").GetString()=="CRM" && x.GetProperty("reason").GetString()=="SUBJECT_MAPPING_AND_EXPORT_ADAPTER_NOT_ADOPTED");
        Assert.Contains(entries,x=>x.GetProperty("source").GetString()=="Sys_OperLog" && x.GetProperty("reason").GetString()=="STABLE_SUBJECT_ID_UNAVAILABLE");
        Assert.Contains(entries,x=>x.GetProperty("source").GetString()=="Order" && x.GetProperty("status").GetString()=="UNKNOWN_SCOPE");
        var security=entries.Single(x=>x.GetProperty("source").GetString()=="Sys_SecurityLog");
        Assert.Contains(security.GetProperty("fields").EnumerateArray(),x=>x.GetProperty("name").GetString()=="ContextJson" && x.GetProperty("reason").GetString()=="UNCLASSIFIED_NESTED_CONTENT");
        using var original=JsonDocument.Parse((await h.Db.Sys_SecurityLogs.SingleAsync(x=>x.Id==log.Id)).ContextJson!);
        Assert.Equal("nested-secret-not-exported",original.RootElement.GetProperty("nested").GetProperty("token").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacySubject_known_empty_is_distinct_from_unavailable_and_failed_source_never_returns_success()
    {
        var hook=new PrivacyReadFailure();await using var h=await Seed(hook);
        using(var stream=await Privacy(h).ExportSubjectAsync(h.User))using(var doc=await JsonDocument.ParseAsync(stream))
        {
            var source=doc.RootElement.GetProperty("manifest").GetProperty("sources").EnumerateArray().Single(x=>x.GetProperty("source").GetString()=="Sys_UserRole");
            Assert.Equal("INCLUDED",source.GetProperty("status").GetString());Assert.Equal(0,source.GetProperty("recordCount").GetInt32());
        }
        hook.Fail=true;
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>Privacy(h).ExportSubjectAsync(h.User));Assert.Contains("required Owner",failure.Message);
        hook.Fail=false;
        Assert.Equal("E-SEC-032",(await Assert.ThrowsAsync<InvalidOperationException>(()=>Privacy(h).ExportSubjectAsync(Guid.NewGuid()))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacySubject_operator_tenant_and_same_name_never_expand_actual_source_ownership()
    {
        await using var target=await Seed();await using var other=await Seed();
        var wanted=new Sys_SecurityLog{UserId=target.User,UserName="previous-name",CreatedAt=DateTime.UtcNow};
        target.Db.Sys_SecurityLogs.Add(wanted);target.Db.Sys_UserRoles.Add(new(){UserId=target.User,RoleId=72});await target.Db.SaveChangesAsync();
        other.Db.Sys_SecurityLogs.AddRange(new(){UserId=target.User,UserName="price-owner",CreatedAt=DateTime.UtcNow},new(){UserId=other.User,UserName="price-owner",CreatedAt=DateTime.UtcNow});
        other.Db.Sys_UserRoles.Add(new(){UserId=other.User,RoleId=72});await other.Db.SaveChangesAsync();
        using var stream=await Privacy(other).ExportSubjectAsync(target.User);using var doc=await JsonDocument.ParseAsync(stream);var root=doc.RootElement;
        Assert.Equal(target.Tenant,root.GetProperty("manifest").GetProperty("tenantId").GetGuid());
        Assert.Equal(wanted.Id,Assert.Single(root.GetProperty("securityLogs").EnumerateArray()).GetProperty("Id").GetGuid());
        Assert.Equal(target.User,Assert.Single(root.GetProperty("roleAssignments").EnumerateArray()).GetProperty("UserId").GetGuid());
        Assert.Equal(other.Tenant,other.Db.CurrentTenantId);
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacySubject_retired_HTTP_preserves_current_platform_authority_and_never_returns_legacy_bytes()
    {
        await using var h=await Seed();
        h.Db.Sys_Users.Add(new(){UserName="privacy-backup-platform-admin",Password="simulated-unused-credential",IsPlatformAdmin=true,Enable=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsPlatformAdmin,true));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());var cookies=await Login(host,h);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/gdpr/export/subject/{h.User}")))
        {
            Assert.Equal(HttpStatusCode.Gone,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Contains("PRIVACY_USE_DURABLE_EXPORT",await response.Content.ReadAsStringAsync());
        }
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsPlatformAdmin,false));
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/gdpr/export/subject/{h.User}"));
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.DoesNotContain("sources",await denied.Content.ReadAsStringAsync());
        var fresh=await Login(host,h);
        using var noPlatform=await host.Send(OperationsRequest(fresh,HttpMethod.Get,$"/api/platform/gdpr/export/subject/{h.User}"));
        Assert.Equal(HttpStatusCode.Forbidden,noPlatform.StatusCode);Assert.DoesNotContain("sources",await noPlatform.Content.ReadAsStringAsync());
    }
}
