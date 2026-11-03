using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class NativeInboxOwnerV2
{
    private async Task<NativeFormContentV2> QueryFileContent(Guid data,ReadIdentity actor,CancellationToken ct)
    {
        if(!actor.Delegated)
        {
            var row=await forms.Unbound(actor.UserId).SingleOrDefaultAsync(r=>r.Data==data,ct);
            if(row!=null)return(await forms.UnboundHistory(row,actor.Actual,ct)).Content;
        }
        var dataset=await Load(actor,ct);return dataset.Content.Values.Select(c=>c.Form).FirstOrDefault(f=>f?.FormDataId==data)??throw Error("RESOURCE_NOT_FOUND",404);
    }
    public async Task<FormFileViewV2> ReadQueryFileAsync(Guid data,FormBoundFileReadV2 input,Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var(actor,lease)=await Begin(behalf,ct,"oa-form-search");
        var content=await QueryFileContent(data,actor,ct);var result=await forms.BoundFileView(content,input.Field,input.Reference,ct);
        await FinalRead(actor,lease,ct);return result;
    }
    public async Task<FileContentDownloadV1> DownloadQueryFileAsync(Guid data,FormBoundFileReadV2 input,Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();FileContentDownloadV1? result;
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            var(actor,lease)=await Begin(behalf,ct,"oa-form-search");var content=await QueryFileContent(data,actor,ct);
            result=await forms.BoundFileBytes(content,input.Field,input.Reference,actor.UserId,ct);await FinalRead(actor,lease,ct);await tx.CommitAsync(ct);
        }
        return result??throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE");
    }
    public async Task<FormFileViewV2> ReadFileAsync(Guid instance,FormBoundFileReadV2 input,Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var(actor,lease)=await Begin(behalf,ct);
        var(_,content)=await Visible(instance,actor,ct);var form=content.Form??throw Error("PERSONAL_FORM_REQUIRED",422);
        var result=await forms.BoundFileView(form,input.Field,input.Reference,ct);await FinalRead(actor,lease,ct);return result;
    }
    public async Task<FileContentDownloadV1> DownloadFileAsync(Guid instance,FormBoundFileReadV2 input,Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();FileContentDownloadV1? result;
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            var(actor,lease)=await Begin(behalf,ct);var(_,content)=await Visible(instance,actor,ct);var form=content.Form??throw Error("PERSONAL_FORM_REQUIRED",422);
            result=await forms.BoundFileBytes(form,input.Field,input.Reference,actor.UserId,ct);
            await FinalRead(actor,lease,ct);await tx.CommitAsync(ct);
        }
        return result??throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE");
    }
}
