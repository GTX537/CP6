using CP6.Core.BaseProvider;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

/// <summary>
/// 文章管理 API（需要登录才能访问）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ArticleController : ControllerBase
{
    private readonly IService<Article> _service;

    public ArticleController(IService<Article> service)
    {
        _service = service;
    }

    /// <summary>
    /// 分页查询
    /// GET /api/article?page=1&pageSize=10&keyword=xxx
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var result = await _service.GetPageListAsync(page, pageSize, keyword);
        return Ok(result);
    }

    /// <summary>
    /// 根据 Id 查询
    /// GET /api/article/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var entity = await _service.GetByIdAsync(id);
        if (entity == null) return NotFound();
        return Ok(entity);
    }

    /// <summary>
    /// 新增
    /// POST /api/article
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] Article entity)
    {
        var result = await _service.AddAsync(entity);
        return Ok(result);
    }

    /// <summary>
    /// 修改
    /// PUT /api/article
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Article entity)
    {
        var result = await _service.UpdateAsync(entity);
        return Ok(result);
    }

    /// <summary>
    /// 删除（支持批量）
    /// DELETE /api/article
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] Guid[] ids)
    {
        var count = await _service.DeleteAsync(ids);
        return Ok(new { count });
    }
}
