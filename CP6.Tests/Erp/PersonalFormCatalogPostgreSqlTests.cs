using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PersonalCatalog_reads_selected_publication_and_never_unactivated_draft_or_newer_publication()
    {
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h);
        var initial=Assert.Single((await owner.CatalogAsync()).Items);var head=await form.ReadHeadAsync("personal");
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand(head.Version,initial.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand("new-name",next.ResourceVersion,"Unactivated private draft"));
        Assert.Equal(initial,Assert.Single((await owner.CatalogAsync()).Items));
        var published=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),"new-publication"))).Result!.Publication!;
        Assert.Empty((await owner.CatalogAsync(search:"Unactivated private draft")).Items);
        await form.SetActivationAsync("personal",FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current,published.VersionId,id:"select-new"));
        var selected=Assert.Single((await owner.CatalogAsync(search:"private draft")).Items);
        Assert.Equal(published.VersionId,selected.FormVersionId);Assert.Equal(2,selected.FormVersion);
        Assert.Equal((await owner.ContextAsync("personal")).Name,selected.Name);
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalCatalog_filters_current_policy_and_activation_before_count_search_and_pagination()
    {
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h);
        var saved=await form.SaveAsync("excluded",FormAuthoringCommand(name:"Excluded form"));
        var p=(await form.PublishAsync("excluded",FormPublicationCommand(await FormPreview(form,"excluded",saved)))).Result!.Publication!;
        await owner.ConfigureAsync("excluded",new(Guid.NewGuid(),"absent",true,true,[],"local-retention/1",30,true,"no use grant"));
        await form.SetActivationAsync("excluded",FormActivationCommand((await form.ActivationPreviewAsync("excluded")).Current,p.VersionId));
        var first=await owner.CatalogAsync(pageSize:1);Assert.Equal(1,first.Total);Assert.Equal("personal",Assert.Single(first.Items).FormKey);
        Assert.Empty((await owner.CatalogAsync(page:2,pageSize:1)).Items);Assert.Equal(0,(await owner.CatalogAsync(search:"Excluded")).Total);
        var active=(await form.ActivationPreviewAsync("personal")).Current;
        await form.SetActivationAsync("personal",FormActivationCommand(active,active.PublishedVersionId!.Value,false,"disable"));
        Assert.Empty((await owner.CatalogAsync()).Items);
        await form.SetActivationAsync("personal",FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current,active.PublishedVersionId.Value,id:"enable"));
        var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),false,true,policy.Roles,policy.RetentionPolicyRef,30,true,"disable use"));
        Assert.Equal(0,(await owner.CatalogAsync()).Total);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="oa-form-catalog");
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==menu.MenuId).ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CatalogAsync())).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalCatalog_does_not_disclose_another_tenants_selected_metadata_with_the_same_key_and_role_id()
    {
        await using var own=await Seed();var(owner,_,_)=await PreparePersonal(own);
        await using var foreign=await Seed();var(other,form,_)=await PreparePersonal(foreign);
        var initial=Assert.Single((await other.CatalogAsync()).Items);var head=await form.ReadHeadAsync("personal");
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand(head.Version,initial.FormVersionId));
        var edit=await form.SaveAsync("personal",FormAuthoringCommand("foreign-name",next.ResourceVersion,"Foreign tenant private metadata"));
        var published=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",edit),"foreign-publication"))).Result!.Publication!;
        await form.SetActivationAsync("personal",FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current,published.VersionId,id:"foreign-active"));
        Assert.Equal("Foreign tenant private metadata",Assert.Single((await other.CatalogAsync()).Items).Name);
        Assert.Empty((await owner.CatalogAsync(search:"Foreign tenant")).Items);
        var visible=Assert.Single((await owner.CatalogAsync()).Items);Assert.NotEqual(published.VersionId,visible.FormVersionId);Assert.Equal("draft",visible.Name);
    }
}
