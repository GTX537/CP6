# PUB 02 · 功能权限：菜单 + 按钮 + 后端强校验 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-02 功能权限 |
| 所属模块 | PUB 公共平台 · Part 1 权限引擎 |
| 里程碑 | **M1 ★（整个权限引擎的痛点核心）** |
| 技术栈 | Vue3 + Element Plus / .NET8 Web API（Authorization Filter）/ EF Core / SQL Server |
| 命名空间 | `Sys` |
| 前置 | [章01 多角色](./01-rbac-multirole.md)（`UserPermissionContext.ActionKeys` 在此填充） |

> **题眼**：**前端藏菜单不是安全，后端每个操作都被校验才是安全。** CP6 现状只做到"前端按角色隐藏菜单/按钮"——绕过前端直接 POST API 就裸奔（只有 `[Authorize]`，登录即放行）。本章把校验**下沉到后端**：给操作打 `[RequirePermission("order","export")]` 特性，请求进来先查当前用户多角色的操作权限并集，**不命中直接 403**。前端隐藏只是体验，后端强校验才是闸门。

---

## 目录
- 第1章 概述（裸奔 → 后端强校验）
- 第2章 数据模型（Sys_MenuAction / Sys_RoleAction + 复用 Sys_Menu/RoleMenu）
- 第3章 资源键约定（menu:action）
- 第4章 [RequirePermission] 特性与授权管线
- 第5章 IPermissionService 与 ActionKeys 聚合
- 第6章 前端 v-permission（体验） vs 后端强校验（安全）
- 第7章 菜单 + 按钮授权画面
- 第8章 操作点标准化（ActionCode 字典）
- 第9章 字段明细 / 控制矩阵
- 第10章 处理详细
- 第11章 API 接口设计
- 第12章 消息一览
- 第13章 集成与依赖

---

## 第1章 概述

| 层级 | 现状 | 升级后 |
|---|---|---|
| 页面/菜单 | `Sys_Menu` + `Sys_RoleMenu`，仅前端隐藏 | 保留，**后端也校验** |
| 按钮/操作 | 无 | `Sys_MenuAction` 定义操作点 + `Sys_RoleAction` 授权 |
| 后端校验 | 仅 `[Authorize]`（登录即放行） | `[RequirePermission(menu,action)]` 操作级强校验 |
| 绕过前端 | **裸奔** | **403 挡住** |

**范围**：操作点定义 + 角色操作授权 + `[RequirePermission]` 特性 + 授权管线 + 前端 `v-permission` 指令 + 菜单/按钮授权画面。

---

## 第2章 数据模型

```csharp
// CP6.Entity/DomainModels/Sys/Sys_MenuAction.cs（新建）—— 给页面挂"操作点"
[Table("Sys_MenuAction")]
public class Sys_MenuAction : BaseEntity
{
    public Guid   MenuId     { get; set; }            // → Sys_Menu.Id
    public string ActionCode { get; set; } = "";      // add/del/edit/query/export/import/approve…
    public string ActionName { get; set; } = "";      // 显示名（新增/删除/导出…）
    public int    Sort       { get; set; }
}

// CP6.Entity/DomainModels/Sys/Sys_RoleAction.cs（新建）—— 按钮级授权
[Table("Sys_RoleAction")]
public class Sys_RoleAction : BaseEntity
{
    public Guid   RoleId     { get; set; }            // → Sys_Role.Id
    public Guid   MenuId     { get; set; }            // → Sys_Menu.Id
    public string ActionCode { get; set; } = "";      // 该角色在该菜单可执行的操作
}
```
```sql
CREATE UNIQUE INDEX UX_Sys_MenuAction ON Sys_MenuAction(TenantId, MenuId, ActionCode);
CREATE UNIQUE INDEX UX_Sys_RoleAction ON Sys_RoleAction(TenantId, RoleId, MenuId, ActionCode);
CREATE INDEX IX_Sys_RoleAction_Role ON Sys_RoleAction(TenantId, RoleId);
```

**复用现有**（不新建、升级校验）：
- `Sys_Menu`（菜单树，页面级）：保留；新增 `MenuKey`（稳定业务键，如 `order`）作为资源键前缀。
- `Sys_RoleMenu`（角色↔菜单）：保留；从"仅前端隐藏"升级为"后端也校验页面可访问"。

> **MenuAction = 该页面有哪些操作点；RoleAction = 某角色在该页面被授予哪些操作。** 两者分开：操作点是页面的固有能力（设计期定义），授权是运营期按角色勾选。

---

## 第3章 资源键约定

统一资源键 = **`menuKey:actionCode`**，如 `order:export`、`po:approve`。

- `menuKey` 取 `Sys_Menu.MenuKey`（稳定业务键，不用易变的菜单 Id/名称）。
- `actionCode` 取标准操作码（第8章）。
- 这是 `[RequirePermission]` 特性、`UserPermissionContext.ActionKeys`、前端 `v-permission` 三处**同一套键**，保证前后端一致。

---

## 第4章 [RequirePermission] 特性与授权管线

```csharp
// CP6.Core/Auth/RequirePermissionAttribute.cs
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _menu, _action;
    public RequirePermissionAttribute(string menu, string action) { _menu = menu; _action = action; }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var perm = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        if (!await perm.HasActionAsync(_menu, _action))                       // 查多角色操作并集
            context.Result = new ObjectResult(new { code = 403, msg = "无操作权限" })
                { StatusCode = StatusCodes.Status403Forbidden };              // ★不命中即 403
    }
}
```

用法（贴在 Controller Action 上）：
```csharp
[HttpPost("export")]
[RequirePermission("order", "export")]          // 没有 order:export 权限 → 403
public async Task<IActionResult> Export(...) { ... }
```

> 走 `IAsyncAuthorizationFilter`（授权阶段），比 Action 内手写判断更早拦截、更难漏。`[Authorize]` 仍保留（先验证登录），`[RequirePermission]` 在其上叠操作级校验。

---

## 第5章 IPermissionService 与 ActionKeys 聚合

```csharp
// CP6.Core/Services/Sys/IPermissionService.cs
public interface IPermissionService
{
    Task<bool> HasActionAsync(string menu, string action);   // 操作级
    Task<bool> HasMenuAsync(string menu);                    // 页面级
}

// 实现：读章01 聚合好的会话上下文，O(1) 命中，不每次查库
public async Task<bool> HasActionAsync(string menu, string action)
{
    var ctx = await _current.GetContextAsync();              // UserPermissionContext（章01，会话缓存）
    return ctx.ActionKeys.Contains($"{menu}:{action}");
}
```

`ActionKeys` 由章01 `PermissionAggregator.BuildAsync` 填充（多角色**并集**）：
```csharp
ctx.ActionKeys = (await _db.Sys_RoleActions
    .Where(ra => roleIds.Contains(ra.RoleId))
    .Join(_db.Sys_Menus, ra => ra.MenuId, m => m.Id, (ra, m) => m.MenuKey + ":" + ra.ActionCode)
    .ToListAsync()).ToHashSet();                            // 多角色操作并集
```

> 强校验**零额外查库**：登录时一次性把操作并集算进 `ActionKeys`，每次请求 `HasActionAsync` 只是 HashSet 命中。角色授权变更 → 失效缓存（章01）→ 下次重建。

---

## 第6章 前端 v-permission（体验） vs 后端强校验（安全）

```js
// cp6.web/src/directives/permission.js —— 仅控制"显示/隐藏"，是体验不是安全
app.directive('permission', {
  mounted(el, binding) {
    const key = binding.value                       // 如 "order:export"
    if (!usePermStore().actionKeys.has(key)) el.parentNode?.removeChild(el)
  }
})
```
```html
<el-button v-permission="'order:export'" @click="exportData">导出</el-button>
```

| | 前端 v-permission | 后端 [RequirePermission] |
|---|---|---|
| 作用 | 隐藏没权限的按钮/菜单 | 拦截没权限的请求 |
| 性质 | **体验**（少点没用的按钮） | **安全**（绕过前端也挡） |
| 数据源 | 登录下发的 `actionKeys`（同一套键） | 会话 `UserPermissionContext.ActionKeys` |

> **必须两者都做，但安全只靠后端**：前端隐藏让界面干净，但抓包/直调 API 能绕过；后端 `[RequirePermission]` 是真正的闸门。**只做前端 = 裸奔；只做后端 = 安全但体验差。两者同一套键，一处授权两处生效。**

---

## 第7章 菜单 + 按钮授权画面

角色管理 → 选角色 → 配权限（菜单树勾选 + 每个菜单的操作点勾选）：

| 区域 | 内容 |
|---|---|
| 角色列表 | 左侧角色列表（选一个配权限） |
| 菜单树 | 中部 `el-tree` + checkbox：勾选页面（写 `Sys_RoleMenu`） |
| 操作点 | 选中菜单 → 右侧列出该菜单的操作点（来自 `Sys_MenuAction`），勾选授予（写 `Sys_RoleAction`） |
| 按钮 | 保存（diff 增删 RoleMenu/RoleAction）→ 失效该角色下所有用户缓存 |

---

## 第8章 操作点标准化（ActionCode 字典）

`Sys_MenuAction.ActionCode` 取标准码（可扩展，存字典）：

| ActionCode | 含义 | | ActionCode | 含义 |
|---|---|---|---|---|
| query | 查询 | | export | 导出 |
| add | 新增 | | import | 导入 |
| edit | 编辑 | | approve | 审批 |
| del | 删除 | | print | 打印 |
| audit | 审核/复核 | | … | 按页面扩展 |

> 操作点按页面挂（不是所有页面都有全套）：列表页可能只有 query/export，单据页有 add/edit/del/approve。设计期在 `Sys_MenuAction` 为每个菜单定义其操作点。

---

## 第9章 字段明细 / 控制矩阵

**授权画面字段**：

| 字段 | 控件 | 说明 |
|---|---|---|
| roleId | 角色列表(单选) | 当前配权限的角色 |
| menuKeys | 菜单树(多选 checkbox) | 写 Sys_RoleMenu |
| actions[menuId] | 操作点(多选 checkbox) | 写 Sys_RoleAction |

**控制矩阵**：菜单未勾选时其操作点禁用（不能给没授页面的操作权）。

---

## 第10章 处理详细

### 10.1 配置授权（保存）
```
diff 菜单：新增/移除 Sys_RoleMenu
diff 操作：新增/移除 Sys_RoleAction
校验：授予操作的菜单必须已授予（操作 ⊆ 已授菜单）（E-PUB-021）
完成 → 失效该角色下所有用户的 UserPermissionContext（章01）
```

### 10.2 请求强校验（运行期）
```
请求 → [Authorize] 验登录 → [RequirePermission(menu,action)]
  → IPermissionService.HasActionAsync → ctx.ActionKeys 命中？
      命中 → 放行
      不命中 → 403 {code:403, msg:无操作权限}
```

### 10.3 操作点定义（设计期）
```
为菜单定义操作点 Sys_MenuAction（add/edit/del/export…），授权画面据此列出可勾选项
```

---

## 第11章 API 接口设计（.NET8）

前缀 `/api/pub/role-perm`：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/menu-action/{menuId}` | GET/PUT | 维护某菜单的操作点（Sys_MenuAction） |
| `/{roleId}` | GET | 取角色的菜单 + 操作授权 |
| `/{roleId}` | PUT | 保存角色授权（diff RoleMenu/RoleAction + 失效缓存） |
| `/my-actions` | GET | 当前用户的 ActionKeys（前端 v-permission 下发用） |

---

## 第12章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-021 | Error | 授予操作的菜单未授权 | 给未授菜单授操作权 |
| 403 | HTTP | 无操作权限 | `[RequirePermission]` 不命中 |

---

## 第13章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 章01 多角色 | 读 `UserPermissionContext.ActionKeys`（多角色操作并集） |
| → 各业务模块 | Controller 贴 `[RequirePermission(menu,action)]`；前端贴 `v-permission` |
| ← 现有 Sys_Menu/RoleMenu | 复用；RoleMenu 升级为后端也校验，补 MenuKey |
| → 章03/04 | 功能权限通过后，数据/字段权限继续在查询/序列化层收窄 |

> **三权第一权**：功能权限（你能点什么）= 操作并集。它和章03 数据权限（你能看哪些行）、章04 字段权限（你能看哪些列）合起来才是完整授权——本章先把"操作闸门"在后端立住。

---

## 自检
- [ ] 为什么前端隐藏不算安全？后端在哪一层、用什么挡住绕过前端的请求？
- [ ] `Sys_MenuAction` 和 `Sys_RoleAction` 分别是什么？为什么要分开？
- [ ] 资源键 `menu:action` 为什么前后端用同一套？为什么用 MenuKey 而非菜单 Id？
- [ ] `[RequirePermission]` 怎么读到多角色的操作并集？为什么强校验零额外查库？
- [ ] 配置授权后为什么要失效缓存？不失效会怎样？

---

*实现：新建 `CP6.Entity/DomainModels/Sys/{Sys_MenuAction,Sys_RoleAction}.cs` + `CP6.Core/Auth/RequirePermissionAttribute.cs` + `CP6.Core/Services/Sys/PermissionService.cs`（HasActionAsync 读章01 上下文）+ 前端 `v-permission` 指令 + 菜单/按钮授权 UI；复用 `Sys_Menu/Sys_RoleMenu`（补 MenuKey、后端校验）。配套 xlsx 详细设计见同名 `.xlsx`。*
