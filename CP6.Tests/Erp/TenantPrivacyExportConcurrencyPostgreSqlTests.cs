using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_native_permission_and_artifact_revocation_serialize_with_generation_and_disclosure_in_both_orders()
    {
        await using var h=await Seed();await TenantPrivacyAuthority(h);var o=PrivacyOwner(h);var s=PrivacySession(h);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generating=o.GenerateTenantAsync(TenantPrivacyCommand(h),PrivacySession(h,async _=>{entered.TrySetResult();await release.Task;}));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var withdrawing=writer.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="tenant-download").ExecuteDeleteAsync();TenantPrivacyExportOperationV1 saved;
        try{await AssertNativeWait(writer,withdrawing);release.TrySetResult();saved=await generating;await withdrawing;}
        finally{release.TrySetResult();}
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadTenantAsync(saved.Receipt!.ArtifactId,s))).Status);
        await EngineeringAction(h,"privacy-export","tenant-download");h.Db.ChangeTracker.Clear();var next=(await o.GenerateTenantAsync(TenantPrivacyCommand(h),s)).Receipt!.ArtifactId;
        var worker=h with{Db=writer};entered=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var revoking=PrivacyOwner(worker).RevokeTenantAsync(next,PrivacySession(worker,async _=>{entered.TrySetResult();await release.Task;}));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var waiting=o.DownloadTenantAsync(next,s);
        try{await AssertNativeWait(h.Db,waiting);release.TrySetResult();await revoking;Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(async()=>await waiting)).Status);}
        finally{release.TrySetResult();}
        next=(await o.GenerateTenantAsync(TenantPrivacyCommand(h),s)).Receipt!.ArtifactId;entered=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading=o.DownloadTenantAsync(next,PrivacySession(h,async _=>{entered.TrySetResult();await release.Task;}));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        revoking=PrivacyOwner(worker).RevokeTenantAsync(next,PrivacySession(worker));
        try{await AssertNativeWait(writer,revoking);release.TrySetResult();Assert.NotEmpty((await reading).Bytes);await revoking;}
        finally{release.TrySetResult();}
        Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadTenantAsync(next,s))).Status);
    }
}
