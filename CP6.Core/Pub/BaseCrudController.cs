using CP6.Core.Services.Pub;
using CP6.Entity;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Core.Pub;

/// <summary>
/// 通用 CRUD 泛型控制器基座 —— PUB 章08 §2。提供 query/add/edit/del 方法体（virtual）。
/// <para>权限常量特性难题（C# 特性参数须编译期常量，泛型基类无法用实例属性）：
/// 由代码生成器（D-3）产出的具体子类在 override 上贴常量
/// <c>[RequirePermission("order","add")]</c> / <c>[FieldMask("order")]</c>，基类只兜逻辑。</para>
/// 具体子类须声明 <c>[ApiController][Route("api/...")]</c> 才可路由（泛型基类本身不被路由）。
/// </summary>
public abstract class BaseCrudController<T, TService> : ControllerBase
    where T : BaseEntity, IDataScoped
    where TService : BaseCrudService<T>
{
    protected readonly TService Svc;
    protected BaseCrudController(TService svc) => Svc = svc;

    /// <summary>分页查询（数据范围自动注入）。子类可 override 加 [FieldMask("resource")]。</summary>
    [HttpGet]
    public virtual async Task<IActionResult> Query(int page = 1, int pageSize = 10)
    {
        var (rows, total) = await Svc.QueryAsync(page, pageSize);
        return Ok(new { rows, total });
    }

    /// <summary>新增（自动采番 + 部门归属）。子类可 override 加 [RequirePermission("resource","add")]。</summary>
    [HttpPost]
    public virtual async Task<IActionResult> Add([FromBody] T entity) => Ok(await Svc.CreateAsync(entity));

    /// <summary>修改（自动拒写只读/隐藏字段）。子类可 override 加 [RequirePermission("resource","edit")]。</summary>
    [HttpPut]
    public virtual async Task<IActionResult> Edit([FromBody] T entity)
    {
        var r = await Svc.UpdateAsync(entity);
        return r == null ? NotFound() : Ok(r);
    }

    /// <summary>批量删除。子类可 override 加 [RequirePermission("resource","delete")]。</summary>
    [HttpDelete]
    public virtual async Task<IActionResult> Del([FromBody] Guid[] ids)
        => Ok(new { count = await Svc.DeleteAsync(ids) });
}
