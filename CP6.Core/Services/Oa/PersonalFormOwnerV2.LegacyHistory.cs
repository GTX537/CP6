using System.Globalization;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record LegacyPersonalDraftItemV1(Guid Id,string? Title,string? FormKey,int? RecordedVersion,int Status,
    string RecordedUpdatedAt,string Source,string EvidenceState="UNVERIFIED_LEGACY",bool CanWrite=false);
public sealed record LegacyPersonalDraftPageV1(LegacyPersonalDraftItemV1[] Items,int Total,int Page,int PageSize);

public sealed partial class PersonalFormOwnerV2
{
    public async Task<LegacyPersonalDraftPageV1> LegacyDraftHistoryAsync(int page=1,int pageSize=20,CancellationToken ct=default)
    {
        if(page is <1 or >100000||pageSize is <1 or >100)throw Error("PAGE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);
        var query=from draft in db.Wf_FormDrafts.AsNoTracking()
            join form in db.Wf_FormDefs.AsNoTracking() on draft.FormDefId equals form.Id into forms
            from form in forms.DefaultIfEmpty()
            join version in db.Wf_FormDefVersions.AsNoTracking() on new{Id=draft.FormDefVersionId,draft.FormDefId} equals new{version.Id,version.FormDefId} into versions
            from version in versions.DefaultIfEmpty()
            where draft.OwnerUserId==actor.UserId
            select new{draft.Id,draft.Title,Key=form==null?null:form.FormKey,
                Version=version==null?(int?)null:version.Version,VersionCreator=version==null?null:version.Creator,
                draft.Status,Updated=draft.ModifyDate??draft.CreateDate};
        var total=await query.CountAsync(ct);
        var rows=await query.OrderByDescending(x=>x.Updated).ThenByDescending(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        // These rows have no proven native publication/field-policy mapping. A historical
        // owner ID permits this directory, not current field disclosure or a write upgrade.
        var items=rows.Select(x=>new LegacyPersonalDraftItemV1(x.Id,TitleReadable(actor)?x.Title:null,x.Key,x.Version,x.Status,
            x.Updated.ToString("O",CultureInfo.InvariantCulture),x.Version==null?"MISSING_VERSION_SNAPSHOT":
                LegacyDefinitionViewV2.Reconstructed(x.VersionCreator)?"LEGACY_HEAD_BACKFILL":"LEGACY_VERSION_SNAPSHOT")).ToArray();
        await Final(actor,lease,ct,false);return new(items,total,page,pageSize);
    }
}
