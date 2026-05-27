using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class SampleStockService : ISampleStockService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "SMP";

    public SampleStockService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<SampleStock>> SearchAsync(SampleSearchQuery q)
    {
        var query = _db.SampleStocks.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.SampleNo)) query = query.Where(x => x.SampleNo.Contains(q.SampleNo));
        if (!string.IsNullOrWhiteSpace(q.SampleType)) query = query.Where(x => x.SampleType == q.SampleType);
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) query = query.Where(x => x.CustomerCd == q.CustomerCd);
        if (!string.IsNullOrWhiteSpace(q.ProductCd)) query = query.Where(x => x.ProductCd == q.ProductCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.OverdueOnly == true)
        {
            var today = DateTime.Today;
            query = query.Where(x => x.Status == SampleStatus.LentOut
                && x.ExpectedReturnDate.HasValue
                && x.ExpectedReturnDate.Value < today);
        }
        return await query.OrderByDescending(x => x.RegisteredAt)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<SampleStock?> GetAsync(string sampleNo)
        => await _db.SampleStocks.AsNoTracking().FirstOrDefaultAsync(x => x.SampleNo == sampleNo && !x.IsDeleted);

    public async Task<string> CreateAsync(SampleDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.SampleType))
            throw new InvalidOperationException("SampleType required");
        if (dto.Quantity <= 0) throw new InvalidOperationException("Quantity > 0");

        var no = await _seq.NextAsync(Prefix);
        _db.SampleStocks.Add(new SampleStock
        {
            SampleNo = no,
            SampleType = dto.SampleType,
            CustomerCd = dto.CustomerCd, CustomerName = dto.CustomerName,
            ProductCd = dto.ProductCd, ProductName = dto.ProductName,
            Quantity = dto.Quantity, UnitCd = dto.UnitCd,
            WarehouseCd = dto.WarehouseCd, LocationCd = dto.LocationCd,
            Status = SampleStatus.InStock,
            RegisteredAt = DateTime.Now,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task UpdateAsync(string sampleNo, SampleDto dto, string? userName)
    {
        var s = await GetTracked(sampleNo);
        if (s.Status == SampleStatus.Expired)
            throw new InvalidOperationException("WM-MSG-043: 失効品は訂正不可");

        s.SampleType = dto.SampleType;
        s.CustomerCd = dto.CustomerCd; s.CustomerName = dto.CustomerName;
        s.ProductCd = dto.ProductCd; s.ProductName = dto.ProductName;
        s.Quantity = dto.Quantity; s.UnitCd = dto.UnitCd;
        s.WarehouseCd = dto.WarehouseCd; s.LocationCd = dto.LocationCd;
        s.Remarks = dto.Remarks;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task LendAsync(string sampleNo, string lentTo, DateTime? expectedReturnDate, string? userName)
    {
        if (string.IsNullOrWhiteSpace(lentTo))
            throw new InvalidOperationException("lentTo required");
        var s = await GetTracked(sampleNo);
        if (s.Status != SampleStatus.InStock && s.Status != SampleStatus.Returned)
            throw new InvalidOperationException("WM-MSG-043: 保管中/返却済のみ貸出可");
        s.Status = SampleStatus.LentOut;
        s.LentTo = lentTo;
        s.LentAt = DateTime.Now;
        s.ExpectedReturnDate = expectedReturnDate;
        s.ReturnedAt = null;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task ReturnAsync(string sampleNo, string? userName)
    {
        var s = await GetTracked(sampleNo);
        if (s.Status != SampleStatus.LentOut)
            throw new InvalidOperationException("WM-MSG-043: 貸出中のみ返却可");
        s.Status = SampleStatus.Returned;
        s.ReturnedAt = DateTime.Now;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task ExpireAsync(string sampleNo, string? userName)
    {
        var s = await GetTracked(sampleNo);
        if (s.Status == SampleStatus.Expired)
            throw new InvalidOperationException("WM-MSG-043: 既に失効");
        s.Status = SampleStatus.Expired;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<SampleStock>> OverdueAsync()
    {
        var today = DateTime.Today;
        return await _db.SampleStocks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == SampleStatus.LentOut
                && x.ExpectedReturnDate.HasValue && x.ExpectedReturnDate.Value < today)
            .OrderBy(x => x.ExpectedReturnDate)
            .ToListAsync();
    }

    public async Task DeleteAsync(string sampleNo, string? userName)
    {
        var s = await GetTracked(sampleNo);
        if (s.Status == SampleStatus.LentOut)
            throw new InvalidOperationException("WM-MSG-043: 貸出中は削除不可（先に返却）");
        s.IsDeleted = true;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task<SampleStock> GetTracked(string sampleNo)
        => await _db.SampleStocks.FirstOrDefaultAsync(x => x.SampleNo == sampleNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
}
