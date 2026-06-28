namespace CP6.Core.Services.Oa;

public interface ICatalogService
{
    Task<IReadOnlyList<CatalogNode>> CatalogAsync(Guid userId);   // 分类树 + 收藏标注
}
