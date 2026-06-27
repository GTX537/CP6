namespace CP6.Core.Services.Oa;

public interface IFavoriteService
{
    Task AddAsync(Guid userId, string formKey);        // 幂等
    Task RemoveAsync(Guid userId, string formKey);
    Task<IReadOnlyList<string>> ListAsync(Guid userId); // 收藏的 FormKey
}
