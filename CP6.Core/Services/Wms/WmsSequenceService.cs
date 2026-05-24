using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS 採番サービス実装（MesSequenceService 同方式）
/// </summary>
public class WmsSequenceService : IWmsSequenceService
{
    private readonly CP6Context _db;
    public WmsSequenceService(CP6Context db) => _db = db;

    public async Task<string> NextAsync(string prefix, DateTime? date = null)
    {
        var d = (date ?? DateTime.Today).Date;
        var px = prefix.ToUpperInvariant();
        var dateKey = d.ToString("yyyyMMdd");

        var seq = await _db.WmsSequences
            .FirstOrDefaultAsync(x => x.Prefix == px && x.DateKey == dateKey);

        int next;
        if (seq == null)
        {
            seq = new WmsSequence { Prefix = px, DateKey = dateKey, NextNo = 1 };
            _db.WmsSequences.Add(seq);
            next = 1;
        }
        else
        {
            seq.NextNo += 1;
            next = seq.NextNo;
        }

        await _db.SaveChangesAsync();
        return $"{px}{dateKey}-{next:D5}";
    }
}
