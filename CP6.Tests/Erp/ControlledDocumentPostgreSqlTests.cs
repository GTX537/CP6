using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 DocumentAuthority(Harness h, CP6Context? db=null)
    {
        var c=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"plm-controlled-document","file-content","plm-engineering-revision","plm-engineering-product"})
        {
            c.RegisterScope(key,key,[1,2,3,4,5],5);
            c.RegisterFields(key,key=="file-content"?[new FieldRegistry.Field("Content","Content"),new FieldRegistry.Field("FileName","Name")]:key=="plm-controlled-document"?[new FieldRegistry.Field("Content","Content"),new FieldRegistry.Field("Metadata","Metadata")]:[]);
        }
        c.Seal();return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static ControlledDocumentOwnerV1 Documents(Harness h,IFileStore store,CP6Context? db=null)
        =>new(db??h.Db,DocumentAuthority(h,db),new(db??h.Db,DocumentAuthority(h,db),store,new ContentInspector()));
    private static async Task<ControlledDocumentCommandV1> DocumentInput(Harness h,IFileStore store)
    {
        byte[] bytes=[1,2,3];var upload=await ContentInput(h,bytes);
        foreach(var action in new[]{"query","add","edit"})await EngineeringAction(h,"plm-controlled-document",action);
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="plm-controlled-document",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var fileOwner=ContentOwner(h,store,new ContentInspector());await fileOwner.UploadAsync(upload,new MemoryStream(bytes));
        var original=await fileOwner.ReadAsync(upload.FileId);
        byte[] derived=[4,5,6];var preview=upload with{OperationId=Guid.NewGuid(),FileId=Guid.NewGuid(),Digest=FileContentBytesV1.Digest(derived)};
        await fileOwner.UploadAsync(preview,new MemoryStream(derived));var pdf=await fileOwner.ReadAsync(preview.FileId);
        var parent=await BomRevisions(h).ReadAsync(upload.Parent.Id,null);var sourceId=Guid.NewGuid();
        return new(Guid.NewGuid(),Guid.NewGuid(),"CREATE",null,null,new("Original drawing","A",parent.Reference,parent.Draft.ProductRef!,
            new("EXTERNAL","Supplier drawing Owner","Supplier unit","V2","2026-10-27","Original supplied for review","zh-CN","Pending professional confirmation"),
            [new(sourceId,"SOURCE",original.ContentRef,null,"zh-CN",""),new(Guid.NewGuid(),"PREVIEW",pdf.ContentRef,sourceId,"zh-CN","Converted from exact original V2")]),"Register original and derived content");
    }
    private static ControlledDocumentCommandV1 DocumentRevise(ControlledDocumentSnapshotV1 value)=>new(Guid.NewGuid(),value.Reference.Id,"REVISE",value.Reference.Version,value.Etag,value.Body,"Revise working content");
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_original_derived_bindings_replay_history_and_diff()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);
        var result=await owner.CommandAsync(input);Assert.Equal(result,await owner.CommandAsync(input));var first=await owner.ReadAsync(input.DocumentId);
        Assert.Equal("DRAFT",first.State);Assert.Equal("REVIEW_REQUIRED",first.Readiness);Assert.Equal(2,first.ContentStatus!.Length);
        Assert.Contains("LANGUAGE_AUTHORITY_REVIEW_REQUIRED",first.Missing!);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_controlled_document_v1.bindings WHERE tenant_id={h.Tenant}").SingleAsync());
        var change=DocumentRevise(first) with{Body=first.Body with{Name="Revised title",RevisionLabel="B"}};
        await owner.CommandAsync(change);var second=await owner.ReadAsync(input.DocumentId);Assert.Equal("2",second.Reference.Version);
        Assert.Equal(first.OriginalJson,(await owner.ReadAsync(input.DocumentId,"1")).OriginalJson);
        Assert.Contains((await owner.DiffAsync(input.DocumentId,"1","2")).Changes,x=>x.Path=="name");
        Assert.Equal(result,(await owner.OperationAsync(input.OperationId,false)).Result);
        Assert.Equal(new byte[]{1,2,3},(await owner.DownloadAsync(input.DocumentId,"1",input.Body.Contents[0].OccurrenceId)).Bytes);
        foreach(var sql in new[]{"UPDATE cp6_controlled_document_v1.versions SET body_bytes=body_bytes","DELETE FROM cp6_controlled_document_v1.bindings","TRUNCATE cp6_controlled_document_v1.heads CASCADE"})
            await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_same_filename_new_bytes_never_replace_old_or_retarget_derivative()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);await owner.CommandAsync(input);
        var first=await owner.ReadAsync(input.DocumentId);var files=ContentOwner(h,store,new ContentInspector());byte[] replacement=[7,8,9];
        var upload=new FileUploadV1(Guid.NewGuid(),Guid.NewGuid(),input.Body.RevisionOrigin,"技术原件.txt",3,FileContentBytesV1.Digest(replacement),"New source bytes");
        await files.UploadAsync(upload,new MemoryStream(replacement));var next=await files.ReadAsync(upload.FileId);
        var original=input.Body.Contents[0];var command=DocumentRevise(first) with{Body=first.Body with{Contents=[original with{File=next.ContentRef},input.Body.Contents[1]]}};
        Assert.Equal("DOCUMENT_DERIVATION_REQUIRES_NEW_CONTENT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command))).Message);
        command=command with{OperationId=Guid.NewGuid(),Body=command.Body with{Contents=[original with{File=next.ContentRef}]}};
        await owner.CommandAsync(command);var oldFile=await files.ReadAsync(original.File.Id);await files.CommandAsync(ContentCommand(oldFile,"HOLD"));
        var historical=await owner.ReadAsync(input.DocumentId,"1");Assert.Equal(original.File,historical.Body.Contents[0].File);Assert.Contains(historical.ContentStatus!,x=>x.Availability=="HELD");
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.DownloadAsync(input.DocumentId,"1",original.OccurrenceId))).Status);
        Assert.Equal(replacement,(await owner.DownloadAsync(input.DocumentId,"2",original.OccurrenceId)).Bytes);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadAsync(input.DocumentId,"3"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_invalid_roles_source_edges_and_forged_file_references_rejected()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);var a=input.Body.Contents[0];var b=input.Body.Contents[1];
        foreach(var contents in new DocumentContentV1[][]{[a with{Role="APPROVED"}],[a,a],[a,b with{DerivedFromOccurrenceId=b.OccurrenceId}],[a with{DerivedFromOccurrenceId=b.OccurrenceId},b],[a,b with{File=a.File}],[a with{File=a.File with{Version="2"}}]})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(input with{Body=input.Body with{Contents=contents}}))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(input with{Body=input.Body with{Contents=[a with{File=a.File with{Digest=new string('a',64)}}]}}))).Status);
        Assert.Empty(await owner.ListAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_current_doc_file_parent_field_rights_protect_reads_history_and_receipts()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);await owner.CommandAsync(input);
        foreach(var resource in new[]{"plm-controlled-document","file-content"})
        {
            h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=resource,FieldName="Content",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadAsync(input.DocumentId,"1"))).Status);
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(input.OperationId,false))).Status);
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.DownloadAsync(input.DocumentId,"1",input.Body.Contents[0].OccurrenceId))).Status);
            await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey==resource).ExecuteDeleteAsync();
        }
        var menu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="plm-engineering-revision").Select(x=>x.MenuId).SingleAsync();await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ListAsync())).Status);
        await using var other=await Seed();await DocumentInput(other,new ContentStore());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Documents(other,store).ReadAsync(input.DocumentId))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task ControlledDocument_department_source_and_revocation_are_current(int type)
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin,type);Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(input))).Status);
        await BindStandalone(h,admin,"plm-engineering-product",input.Body.ProductRef.Id);await BindStandalone(h,admin,"plm-engineering-revision",input.Body.RevisionOrigin.Id);await owner.CommandAsync(input);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={input.Body.RevisionOrigin.Id}");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadAsync(input.DocumentId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_engineering_exact_adoption_keeps_history_and_unapproved_readiness()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);await owner.CommandAsync(input);var document=await owner.ReadAsync(input.DocumentId);
        var revisions=new EngineeringRevisionOwnerV1(h.Db,DocumentAuthority(h),owner);var parent=await revisions.ReadAsync(input.Body.RevisionOrigin.Id,null);
        var member=new EngineeringComponentDraftV1(Guid.NewGuid(),"DOCUMENT","drawing",0,"APPLIES","","",document.Reference);
        await revisions.CommandAsync(PatchRevision(parent,new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=member}));var bound=await revisions.ReadAsync(parent.Reference.Id,null);
        Assert.Equal(document.Reference,Assert.Single(bound.Draft.Components).SourceRef);Assert.Contains("DOCUMENT_APPROVAL_REQUIRED:"+member.OccurrenceId,bound.Missing!);
        await owner.CommandAsync(DocumentRevise(document) with{Body=document.Body with{RevisionOrigin=bound.Reference,RevisionLabel="B"}});
        Assert.Contains("SOURCE_CHANGED:"+member.OccurrenceId,(await revisions.ReadAsync(parent.Reference.Id,null)).Missing!);
        Assert.Equal(bound.OriginalJson,(await revisions.ReadAsync(parent.Reference.Id,null)).OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_concurrent_original_keys_and_stale_revision_conflict()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=Documents(h,store,peer);
        var results=await Task.WhenAll(owner.CommandAsync(input),other.CommandAsync(input));Assert.Equal(results[0],results[1]);var saved=await owner.ReadAsync(input.DocumentId);
        async Task<bool> Revise(ControlledDocumentOwnerV1 o){try{await o.CommandAsync(DocumentRevise(saved));return true;}catch(OrderPriceCorrectionException e){Assert.Equal(412,e.Status);return false;}}
        Assert.Single(await Task.WhenAll(Revise(owner),Revise(other)),x=>x);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(input with{Reason="different"}))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_final_authority_failure_rolls_back_bindings_result_and_audit()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);var reached=false;
        hook.Before=async command=>{
            if(!command.CommandText.Contains("WITH field_subject"))return;
            await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;probe.CommandText=$"SELECT count(*) FROM cp6_controlled_document_v1.bindings WHERE tenant_id='{h.Tenant}'";
            if(Convert.ToInt32(await probe.ExecuteScalarAsync())>0){reached=true;throw new IOException("Current document authority unavailable");}
        };
        try{Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(input))).Status);}finally{hook.Before=null;}
        Assert.True(reached);Assert.Empty(await owner.ListAsync());Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_controlled_document_v1.bindings WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(input.OperationId,false)).State);Assert.Equal("CLOSED",(await owner.OperationAsync(input.OperationId,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(input))).Message);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_controlled_document_v1.versions DISABLE TRIGGER document_bind");
        try{Assert.Equal("DOCUMENT_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ListAsync())).Message);}finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_controlled_document_v1.versions ENABLE TRIGGER document_bind");}
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_current_integrity_failure_is_persistently_quarantined()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);var owner=Documents(h,store);await owner.CommandAsync(input);
        var key=store.Bytes.Single(x=>x.Value.SequenceEqual(new byte[]{1,2,3})).Key;store.Bytes[key]=[7,8,9];
        Assert.Equal("FILE_CONTENT_INTEGRITY_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.DownloadAsync(input.DocumentId,"1",input.Body.Contents[0].OccurrenceId))).Message);
        Assert.Contains((await owner.ReadAsync(input.DocumentId)).ContentStatus!,x=>x.Availability=="QUARANTINED");store.Bytes[key]=[1,2,3];
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.DownloadAsync(input.DocumentId,"1",input.Body.Contents[0].OccurrenceId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_actual_HTTP_command_historical_download_and_revocation()
    {
        await using var h=await Seed();var store=new ContentStore();var input=await DocumentInput(h,store);
        var root=Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,"document-http-files");var local=new LocalFileStore(root);
        foreach(var item in store.Bytes)await local.SaveAsync(new MemoryStream(item.Value),item.Key);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>(){["Storage__Provider"]="Local",["Storage__LocalRoot"]=root});var cookies=await Login(host,h);
        using var command=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"/api/controlled-document/owner-v1/commands",input));Assert.Equal(HttpStatusCode.OK,command.StatusCode);
        using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/controlled-document/owner-v1/"+input.DocumentId));Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl?.NoStore);
        var url=$"/api/controlled-document/owner-v1/{input.DocumentId}/content/{input.Body.Contents[0].OccurrenceId}?version=1";
        using var download=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url));Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(new byte[]{1,2,3},await download.Content.ReadAsByteArrayAsync());Assert.Equal("attachment",download.Content.Headers.ContentDisposition?.DispositionType);
        var menu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="plm-controlled-document").Select(x=>x.MenuId).SingleAsync();await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}
