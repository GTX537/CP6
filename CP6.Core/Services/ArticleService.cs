using CP6.Core.BaseProvider;
using CP6.Entity.DomainModels;

namespace CP6.Core.Services;

/// <summary>
/// 文章服务 - 继承泛型基类，只需重写需要自定义的部分
/// </summary>
public class ArticleService : ServiceBase<Article>
{
    public ArticleService(IRepository<Article> repository) : base(repository)
    {
    }

    /// <summary>
    /// 重写分页查询，加上按标题搜索
    /// </summary>
    public override async Task<object> GetPageListAsync(int page, int pageSize, string? keyword)
    {
        // 如果传了 keyword，就按标题模糊搜索
        var (data, total) = await _repository.GetPageListAsync(
            string.IsNullOrEmpty(keyword) ? null : x => x.Title.Contains(keyword),
            page,
            pageSize);

        return new { rows = data, total };
    }
}
