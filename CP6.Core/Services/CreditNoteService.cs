using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

public class CreditNoteService : ICreditNoteService
{
    private readonly CP6Context _db;

    public CreditNoteService(CP6Context db)
    {
        _db = db;
    }

    public async Task<PagedResultDto<CreditNoteListItemDto>> SearchAsync(CreditNoteQuery query)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200);

        var dbQuery = BuildQuery(query);
        var total = await dbQuery.CountAsync();
        var notes = await dbQuery
            .OrderByDescending(x => x.IssueDate)
            .ThenByDescending(x => x.CreditNoteNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var customerNames = await LoadCustomerNamesAsync(notes.Select(x => x.CustomerCd).Distinct().ToList());
        var items = notes.Select(note => new CreditNoteListItemDto
        {
            CreditNoteNo = note.CreditNoteNo,
            WebOrderNo = note.WebOrderNo,
            RmaNo = note.RmaNo,
            Type = note.Type,
            CustomerCd = note.CustomerCd,
            CustomerName = customerNames.TryGetValue(note.CustomerCd, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : note.CustomerCd,
            ProductCd = note.ProductCd,
            Qty = note.Qty,
            Amount = note.Amount,
            Reason = note.Reason,
            IssueDate = note.IssueDate,
            CreateDate = note.CreateDate,
        }).ToList();

        return new PagedResultDto<CreditNoteListItemDto>
        {
            Total = total,
            PageIndex = page,
            PageSize = pageSize,
            Items = items,
        };
    }

    private IQueryable<CreditNote> BuildQuery(CreditNoteQuery query)
    {
        var dbQuery = _db.CreditNotes.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
        {
            var customerCd = query.CustomerCd.Trim();
            dbQuery = dbQuery.Where(x => x.CustomerCd == customerCd);
        }

        if (!string.IsNullOrWhiteSpace(query.WebOrderNo))
        {
            var webOrderNo = query.WebOrderNo.Trim();
            dbQuery = dbQuery.Where(x => x.WebOrderNo == webOrderNo);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim();
            dbQuery = dbQuery.Where(x => x.Type == type);
        }

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.Date;
            dbQuery = dbQuery.Where(x => x.IssueDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.Date.AddDays(1);
            dbQuery = dbQuery.Where(x => x.IssueDate < toExclusive);
        }

        return dbQuery;
    }

    private async Task<Dictionary<string, string?>> LoadCustomerNamesAsync(List<string> customerCds)
    {
        if (customerCds.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return await _db.BusinessPartners.AsNoTracking()
            .Where(bp => customerCds.Contains(bp.BpCd) && !bp.IsDeleted)
            .GroupBy(bp => bp.BpCd)
            .Select(g => new { CustomerCd = g.Key, CustomerName = g.Select(bp => bp.BpName).FirstOrDefault() })
            .ToDictionaryAsync(x => x.CustomerCd, x => x.CustomerName);
    }
}
