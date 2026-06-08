namespace CP6.Entity.DTOs;

public class CreditNoteQuery
{
    public string? CustomerCd { get; set; }
    public string? WebOrderNo { get; set; }
    public string? Type { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class CreditNoteListItemDto
{
    public string CreditNoteNo { get; set; } = string.Empty;
    public string? WebOrderNo { get; set; }
    public string? RmaNo { get; set; }
    public string Type { get; set; } = string.Empty;
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? ProductCd { get; set; }
    public decimal Qty { get; set; }
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime CreateDate { get; set; }
}
