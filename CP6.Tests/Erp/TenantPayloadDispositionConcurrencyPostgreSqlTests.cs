using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_actual_execution_native_refill_and_global_hold_serialize_in_both_orders()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var first=await TenantPayloadApproved(h,setup);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executing=o.ExecuteTenantPayloadDispositionAsync(first.PlanId,new(Guid.NewGuid(),first.Version,first.ScopeDigest,Guid.NewGuid()),PrivacySession(h,async _=>{entered.TrySetResult();await release.Task;}));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var native=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var refill=native.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_tenant_export_v1.payloads(artifact_id,bytes) VALUES({setup.Artifact},{new byte[]{1}})");
        try{await AssertNativeWait(native,refill);release.TrySetResult();await executing;Assert.Equal("PRIVACY_TENANT_PAYLOAD_DISPOSED_IDENTITY",(await Assert.ThrowsAsync<Npgsql.PostgresException>(async()=>await refill)).MessageText);}
        finally{release.TrySetResult();}
        var secondArtifact=(await PrivacyOwner(h).GenerateTenantAsync(TenantPrivacyCommand(h),s)).Receipt!.ArtifactId;await PrivacyOwner(h).RevokeTenantAsync(secondArtifact,s);var second=await TenantPayloadApproved(h,(secondArtifact,setup.Policy,setup.Approver));
        entered=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);var holder=h with{Db=native};var holding=PrivacyDispositionOwner(holder).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(holder,async _=>{entered.TrySetResult();await release.Task;}));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var waiting=o.ExecuteTenantPayloadDispositionAsync(second.PlanId,new(Guid.NewGuid(),second.Version,second.ScopeDigest,Guid.NewGuid()),s);PrivacyDispositionOperationV1 hold;
        try{await AssertNativeWait(h.Db,waiting);release.TrySetResult();hold=await holding;Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(async()=>await waiting)).Status);}
        finally{release.TrySetResult();}
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={secondArtifact}").SingleAsync());
        await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,hold.Version,false,Guid.NewGuid(),null),s);
    }
}
