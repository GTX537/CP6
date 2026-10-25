using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.DatabaseCompatibility;

public sealed partial class GdprRelationalTests
{
    [Wp5ReportsFact]
    public async Task Subject_export_uses_target_tenant_and_stable_identity_not_names_or_impersonator()
    {
        output.WriteLine(fixture.SetupSummary);
        var targetTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var sharedName = "gdpr-" + Guid.NewGuid().ToString("N");
        var targetLogId = Guid.NewGuid();
        await using (var seed = fixture.CreateContext(targetTenant))
        {
            seed.Sys_Tenants.AddRange(
                new Sys_Tenant { Id = targetTenant, TenantCode = TenantCode(targetTenant), TenantName = "Subject export fixture" },
                new Sys_Tenant { Id = otherTenant, TenantCode = TenantCode(otherTenant), TenantName = "Other subject fixture" });
            seed.Sys_Users.AddRange(
                new Sys_User { Id = targetId, TenantId = targetTenant, UserName = sharedName, Password = "fixture-secret" },
                new Sys_User { Id = otherId, TenantId = otherTenant, UserName = sharedName, Password = "other-secret" });
            seed.Sys_SecurityLogs.AddRange(
                new Sys_SecurityLog { Id = targetLogId, TenantId = targetTenant, UserId = targetId, UserName = "old-name", CreatedAt = DateTime.UtcNow },
                new Sys_SecurityLog { Id = Guid.NewGuid(), TenantId = otherTenant, UserId = targetId, UserName = sharedName, CreatedAt = DateTime.UtcNow },
                new Sys_SecurityLog { Id = Guid.NewGuid(), TenantId = targetTenant, UserId = otherId, UserName = sharedName, CreatedAt = DateTime.UtcNow },
                new Sys_SecurityLog { Id = Guid.NewGuid(), TenantId = targetTenant, UserId = null, UserName = sharedName, CreatedAt = DateTime.UtcNow });
            // None has a stable subject key. Matching a current name or the separate impersonator is insufficient.
            seed.Sys_OperLogs.AddRange(
                new Sys_OperLog { TenantId = targetTenant, UserName = sharedName, RequestBody = "legacy-name-reuse", CreateDate = DateTime.UtcNow },
                new Sys_OperLog { TenantId = otherTenant, UserName = sharedName, RequestBody = "other-tenant", CreateDate = DateTime.UtcNow },
                new Sys_OperLog { TenantId = targetTenant, UserName = "unrelated", ImpersonatorId = targetId, RequestBody = "impersonator-only", CreateDate = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        // Existing platform-admin selection may target another tenant. Operator tenant must not replace target scope.
        var operatorTenant = new TenantContext { CurrentTenantId = TenantContext.DefaultTenant };
        await using var db = fixture.CreateContext(operatorTenant.CurrentTenantId);
        var service = new GdprService(db, null!, null!, null!, new SecurityAuditService(db), operatorTenant);
        using var stream = await service.ExportSubjectAsync(targetId);
        using var package = await JsonDocument.ParseAsync(stream);
        var root = package.RootElement;
        var ids = root.GetProperty("securityLogs").EnumerateArray().Select(x => x.GetProperty("Id").GetGuid()).ToArray();
        var operationCount = root.GetProperty("operLogs").GetArrayLength();
        Assert.True(ids.SequenceEqual(new[] { targetLogId }) && operationCount == 0,
            $"Expected only target-tenant stable-subject security event and no unprovable operation logs; actual security count={ids.Length}, operation count={operationCount}.");
        Assert.Equal("old-name", root.GetProperty("securityLogs")[0].GetProperty("UserName").GetString());
        Assert.Equal(targetId, root.GetProperty("user").GetProperty("Id").GetGuid());
        Assert.Equal(targetTenant, root.GetProperty("user").GetProperty("TenantId").GetGuid());
        Assert.False(root.GetProperty("user").TryGetProperty("Password", out _));
        var scope = root.GetProperty("scope");
        Assert.Equal("cp6.subject-export-scope.v1", scope.GetProperty("version").GetString());
        Assert.Equal(targetTenant, scope.GetProperty("tenantId").GetGuid());
        Assert.Equal(targetId, scope.GetProperty("subjectId").GetGuid());
        Assert.False(scope.GetProperty("complete").GetBoolean());
        Assert.Equal("UNKNOWN_SCOPE", scope.GetProperty("operLogs").GetProperty("status").GetString());
        Assert.Equal("STABLE_SUBJECT_ID_UNAVAILABLE", scope.GetProperty("operLogs").GetProperty("reason").GetString());
        Assert.Equal("UNKNOWN_SCOPE", scope.GetProperty("otherOwners").GetProperty("status").GetString());
        Assert.Equal("SEPARATE_READS", scope.GetProperty("consistency").GetString());
        Assert.True(scope.GetProperty("readStartedAtUtc").GetDateTime() <= scope.GetProperty("readCompletedAtUtc").GetDateTime());
        await using var verify = fixture.CreateContext(targetTenant);
        Assert.Equal(3, await verify.Sys_OperLogs.IgnoreQueryFilters().CountAsync(x => x.TenantId == targetTenant || x.TenantId == otherTenant));
        Assert.Equal(4, await verify.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync(x => x.TenantId == targetTenant || x.TenantId == otherTenant));
        Assert.Equal("fixture-secret", (await verify.Sys_Users.SingleAsync(x => x.Id == targetId)).Password);
        Assert.Equal(TenantContext.DefaultTenant, operatorTenant.CurrentTenantId);
    }
}
