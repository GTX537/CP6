namespace CP6.Entity.DTOs.Mes;

/// <summary>分页结果通用 DTO（MES 一览查询专用，避免与 PA 系列冲突）</summary>
public class PagedResultDto<T>
{
    public int Total { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}
