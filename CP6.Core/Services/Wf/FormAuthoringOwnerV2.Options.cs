using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;
public sealed partial class FormAuthoringOwnerV2
{
    public async Task<FormOptionTypeV1[]> OptionSourcesAsync(string search="",int page=1,CancellationToken ct=default)
    {
        if(search==null||search.Length>100||page is <1 or >100000)throw Error("OPTION_SOURCE_QUERY_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);
        await ReadCapability(actor,ct);
        if(!(await FieldAccess(false,ct)).Fields["SchemaJson"].CanRead)throw Error("FIELD_READ_FORBIDDEN",403);
        var result=await new SharedDictionaryOptionsV1(db).Types(search,page,ct);
        if((await Actor(ct)).UserId!=actor.UserId)throw Error("REAUTH_REQUIRED",401);CompletionClock();return result;
    }
}
