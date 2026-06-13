# 10 · DB 驱动 i18n + 菜单权限

## 📍 学习目标

1. i18n 走 DB 表 vs 走 JSON 文件，分别适合什么场景？
2. CP6 的 `Sys_Langs` 表怎么设计？怎么从扁平 key 转嵌套对象？
3. 菜单（`Sys_Menu`）+ 角色（`Sys_Role`）+ 权限（`Sys_RoleMenu`）的标准 RBAC 数据模型
4. 树形菜单怎么递归渲染？
5. 怎么扩展到字段级权限 / 数据行级权限？

---

## 🔎 真实代码切片

### `Sys_Langs` 表结构

```csharp
// CP6.Entity/DomainModels/Sys_Lang.cs
public class Sys_Lang : BaseEntity
{
    public string LangKey { get; set; }   // 扁平 key，用 . 分隔层级，如 "login.username"
    public string? ZhCN { get; set; }     // 简中
    public string? ZhTW { get; set; }     // 繁中
    public string? En { get; set; }
    public string? Ja { get; set; }
    public string? Ko { get; set; }
}
```

种子 SQL（典型 MERGE 模式）：

```sql
-- docs/wms-stock-i18n-seed.sql
MERGE INTO Sys_Langs AS T
USING (VALUES
    ('wms.stock.title',          N'库存查询', N'庫存查詢', N'Stock Query', N'在庫照会',   N'재고 조회'),
    ('wms.stock.column.product', N'零件',     N'零件',     N'Product',     N'製品',       N'제품'),
    ('wms.stock.column.qty',     N'数量',     N'數量',     N'Quantity',    N'数量',       N'수량')
) AS S (LangKey, ZhCN, ZhTW, En, Ja, Ko) ON T.LangKey = S.LangKey
WHEN MATCHED THEN UPDATE SET T.ZhCN=S.ZhCN, T.ZhTW=S.ZhTW, T.En=S.En, T.Ja=S.Ja, T.Ko=S.Ko
WHEN NOT MATCHED THEN INSERT (Id, LangKey, ZhCN, ZhTW, En, Ja, Ko, Creator, CreateDate)
    VALUES (NEWID(), S.LangKey, S.ZhCN, S.ZhTW, S.En, S.Ja, S.Ko, 'system', GETDATE());
```

### `LangController` API

```csharp
[Route("api/lang")]
public class LangController : ControllerBase
{
    private readonly CacheService _cache;
    private readonly CP6Context _context;

    [HttpGet("{locale}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(string locale)
    {
        var key = $"lang:{locale}";
        var data = await _cache.GetOrSetAsync(key, async () =>
        {
            var rows = await _context.Sys_Langs.AsNoTracking().ToListAsync();
            return rows.ToDictionary(
                r => r.LangKey,
                r => locale switch
                {
                    "zh-CN" => r.ZhCN,
                    "zh-TW" => r.ZhTW,
                    "en"    => r.En,
                    "ja"    => r.Ja,
                    "ko"    => r.Ko,
                    _ => r.ZhCN
                });
        }, TimeSpan.FromMinutes(30));
        return Ok(new { code = 200, data });
    }
}
```

### 前端 `i18n/index.ts`

```typescript
import { createI18n } from 'vue-i18n'
import axios from 'axios'

const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('lang') || 'zh-CN',
  fallbackLocale: 'zh-CN',
  messages: {}
})

export async function initI18n() {
  const locale = i18n.global.locale.value
  const { data } = await axios.get(`/api/lang/${locale}`)
  // 把扁平 key 转嵌套
  const messages = unflatten(data.data)
  i18n.global.setLocaleMessage(locale, messages)
}

function unflatten(flat: Record<string, string>) {
  const result: any = {}
  for (const [key, val] of Object.entries(flat)) {
    const parts = key.split('.')
    let cur = result
    for (let i = 0; i < parts.length - 1; i++) {
      if (!cur[parts[i]]) cur[parts[i]] = {}
      cur = cur[parts[i]]
    }
    cur[parts[parts.length - 1]] = val
  }
  return result
}

export async function changeLang(locale: string) {
  localStorage.setItem('lang', locale)
  i18n.global.locale.value = locale as any
  await initI18n()
}

export default i18n
```

模板使用：

```vue
<el-button>{{ $t('wms.stock.button.refresh') }}</el-button>
```

### `Sys_Menu` 树形结构

```csharp
public class Sys_Menu : BaseEntity
{
    public int MenuId { get; set; }          // int 主键（树形 ParentId 用）
    public int? ParentId { get; set; }       // 顶级 = null
    public string MenuName { get; set; }     // i18n key "menu.wms.stock"
    public string? RoutePath { get; set; }   // "/wms/stock"，null 表示纯分组
    public string? Icon { get; set; }
    public int OrderNo { get; set; }
    public bool Visible { get; set; }
}

public class Sys_RoleMenu : BaseEntity
{
    public Guid RoleId { get; set; }
    public int MenuId { get; set; }
}
```

### `MenuController.GetUserMenus` — 拉登录用户的菜单

```csharp
[HttpGet("user")]
[Authorize]
public async Task<IActionResult> GetUserMenus()
{
    var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    var roleIds = await _context.Sys_Users
        .Where(u => u.Id == userId)
        .Select(u => u.RoleId)
        .ToListAsync();

    var menuIds = await _context.Sys_RoleMenus
        .Where(rm => roleIds.Contains(rm.RoleId))
        .Select(rm => rm.MenuId)
        .Distinct()
        .ToListAsync();

    var menus = await _context.Sys_Menus
        .Where(m => menuIds.Contains(m.MenuId) && m.Visible)
        .OrderBy(m => m.OrderNo)
        .AsNoTracking()
        .ToListAsync();

    return Ok(new { code = 200, data = menus });
}
```

### 前端递归渲染菜单（LayoutView）

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()

const tree = computed(() => buildTree(auth.menus))

function buildTree(menus: Menu[], parentId: number | null = null): MenuTree[] {
  return menus
    .filter(m => m.parentId === parentId)
    .map(m => ({ ...m, children: buildTree(menus, m.menuId) }))
}
</script>

<template>
  <el-menu router>
    <template v-for="m in tree" :key="m.menuId">
      <el-sub-menu v-if="m.children.length" :index="String(m.menuId)">
        <template #title>
          <el-icon><component :is="m.icon" /></el-icon>
          <span>{{ $t(m.menuName) }}</span>
        </template>
        <!-- 递归 -->
        <MenuItem v-for="c in m.children" :key="c.menuId" :menu="c" />
      </el-sub-menu>
      <el-menu-item v-else :index="m.routePath">
        <el-icon><component :is="m.icon" /></el-icon>
        <span>{{ $t(m.menuName) }}</span>
      </el-menu-item>
    </template>
  </el-menu>
</template>
```

---

## 💡 资深视角

### i18n 走 DB vs 走 JSON

| 维度 | JSON 文件（如 i18next） | DB 表（CP6 方案） |
|---|---|---|
| 部署 | 跟前端打包 | 跟后端 DB 一起 |
| 热更新 | 改完要重新部署 | 改完清缓存即生效 |
| 非技术人员翻译 | 给翻译公司 JSON 文件 | 翻译人员直接登录后台改 |
| 多产品共享 | 拷贝 JSON | DB 表，多产品 join |
| 性能 | 加载快（本地文件） | 第一次需 HTTP（CP6 加缓存） |
| 复杂插值 | i18next 支持丰富 | 简单 key-value，复杂插值要自己实现 |

**CP6 选 DB**：因为这是 ERP/MES/WMS 多语言系统，翻译人员经常修改，DB 方案让他们能直接在后台改而不需要前端重新打包。

**JSON 适合**：开源项目、单品 SaaS、翻译人员配合开发上 Git 流程。

### `Sys_Langs` 表的扁平 key

```
login.username
login.password
wms.stock.title
wms.stock.column.product
```

**为什么用扁平 key**：

- DB 表设计简单（一列存 key）
- 改动单条翻译不影响其他
- 前端 unflatten 成嵌套对象后跟 i18next 风格一致

**为什么不用 (Module, Section, Item) 三列**：

- 嵌套层级不固定（`login.button.cancel` vs `wms.stock.column.qcStatus.tooltip`）
- 三列方案灵活性差
- 扁平 key + 程序拆分性价比最高

### 缓存策略

```csharp
_cache.GetOrSetAsync(key, factory, TimeSpan.FromMinutes(30));
```

30 分钟缓存。改翻译后怎么生效？

- 方案 A：每次写 `Sys_Langs` 时 `_cache.RemoveAsync("lang:*")`（CP6 的做法，每个 locale 单独 key）
- 方案 B：减少缓存时间到 5 分钟
- 方案 C：SignalR 推送"翻译变了"事件让前端重拉

CP6 的 `LangController.Update` 在保存后调 `_cache.RemoveAsync("lang:zh-CN")` 等 5 个 key，立刻生效。

### RBAC 标准三表

```
Sys_User --N---1-- Sys_Role
Sys_Role --1---N-- Sys_RoleMenu --N---1-- Sys_Menu
```

CP6 的 RBAC 是**单角色**（一个用户一个 Role）。**多角色**版本：

```
Sys_User --N---N-- Sys_Role  (via Sys_UserRole)
Sys_Role --N---N-- Sys_Menu  (via Sys_RoleMenu)
```

多角色更灵活但权限合并逻辑复杂（并集 vs 严格判断）。CP6 单角色够用。

### 字段级 / 数据行级权限

CP6 当前只做**菜单级**权限（能不能看到这个页面）。更细的级别：

#### 字段级（如"普通员工看不到工资字段"）

```csharp
public class FieldPermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public string EntityName { get; set; }   // "Sys_User"
    public string FieldName { get; set; }    // "Salary"
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
}
```

实现：API 返回前过滤字段，或前端按权限决定显示。

#### 行级（如"营业员只看到自己负责的客户"）

```csharp
public class DataScope : BaseEntity
{
    public Guid RoleId { get; set; }
    public string ScopeType { get; set; }    // "ALL" / "DEPT" / "OWN"
}

// Service 查询时
var query = _context.Customers.AsQueryable();
if (currentScope == "OWN")
    query = query.Where(c => c.SalesUserId == currentUserId);
else if (currentScope == "DEPT")
    query = query.Where(c => c.DeptId == currentDeptId);
```

CP6 当前没做。这是常见的 ERP 后续扩展点。

### 树形菜单的两种实现

#### 1. 邻接表 + 递归构建（CP6 的做法）

```typescript
function buildTree(menus, parentId = null) {
  return menus.filter(m => m.parentId === parentId)
              .map(m => ({ ...m, children: buildTree(menus, m.menuId) }))
}
```

简单，但层级深时递归慢。100 个菜单 + 5 层一般够用。

#### 2. Materialized Path

```
MenuId | Path
1      | "/"
2      | "/1/"     (parent = 1)
3      | "/1/2/"   (parent = 2)
```

查询"某节点的所有后代"用 `WHERE Path LIKE '/1/2/%'`，无需递归。适合查询频繁但变动少的场景。

CP6 没用这套，因为菜单数量少，邻接表足够。

### 菜单 + 路由的双向一致性

```
后端 Sys_Menu.RoutePath = "/wms/stock"
前端 router viewModules["/wms/stock"] = () => import(StockQueryView)
```

两边必须一一对应。CP6 当前是**手维护**，容易漏。**改进方案**：

- 用 OpenAPI/Swagger 出 metadata，前端编译时校验
- 用 vite plugin 生成 viewModules
- 后端 Sys_Menu 加种子 SQL 时同步前端字典

---

## ⚠️ 踩坑记录

### 坑 1：i18n 首屏白屏

```typescript
// ❌ 反例：i18n 异步加载，组件先渲染了
createApp(App).use(i18n).mount('#app')   // 组件已渲染但翻译没好
await initI18n()                          // 太晚了
```

CP6 的修复：`await initI18n()` 必须在 `createApp` 之前（见 main.ts）。否则首屏看到一堆 `{{ $t('login.title') }}` 的 key。

### 坑 2：菜单种子 SQL 没同步前端 viewModules

后端加了一个 `/wms/new-feature` 菜单，前端没在 `viewModules` 加对应 import → 用户点击菜单显示空白，控制台 404。

**修复**：CP6 当前是手工同步。改进：

- 加 CI 校验：扫所有 Sys_Menu 种子 SQL 里的 RoutePath，对比前端 viewModules keys，不一致则 fail
- 或前端 viewModules 自动从 `Sys_Menu` 拉，但开发期不实用

### 坑 3：翻译 key 拼写错误显示原 key

```vue
<el-button>{{ $t('login.signin') }}</el-button>   <!-- 数据库里只有 login.signIn -->
```

vue-i18n 默认 fallback 到 key 本身 → 用户看到 `login.signin`。开发期可配 `missingWarn: true` 在 console 报警。生产可加 i18n 服务端校验。

### 坑 4：父子角色权限继承

CP6 的 Role 是平级的，没有"角色继承"（如 Admin 继承 User）。需要继承时要在 `GetUserMenus` 里实现：

```csharp
var allRoleIds = await GetAllAncestorRolesAsync(userRoleId);
var menuIds = await _context.Sys_RoleMenus
    .Where(rm => allRoleIds.Contains(rm.RoleId)).ToListAsync();
```

代价：每次拉菜单要算继承链。CP6 没做，因为业务用户角色不深。

### 坑 5：菜单缓存 vs 用户菜单变化

如果加缓存"用户 X 的菜单"，管理员改了 X 的角色权限后，X 仍然看到旧菜单。

**修复**：
- 不缓存用户菜单（每次登录拉）
- 或缓存 key 包含 `RoleId` 而非 `UserId`，改 RoleMenu 时清对应缓存

CP6 没缓存用户菜单（每次 GetUserMenus 都查 DB），简化了一致性。

---

## 🧪 自检题

1. **i18n 性能**：5 种语言 × 5000 条 key，前端拉一次要 ~500KB，怎么优化？  
   <details><summary>答案</summary>(1) 按需加载——首屏只拉 login + common，进 view 时再拉对应 namespace；(2) 服务端按页面分组返回 <code>/api/lang/{locale}?ns=wms.stock</code>；(3) 客户端缓存到 IndexedDB 减少重复请求；(4) gzip / brotli 压缩 80%+ 网络节省。</details>

2. **RBAC 扩展**：要支持"用户在不同时间段拥有不同角色"，怎么改数据模型？  
   <details><summary>答案</summary>Sys_UserRole 加 EffectiveFrom / EffectiveTo 字段，GetUserMenus 时 <code>WHERE Now BETWEEN EffectiveFrom AND EffectiveTo</code>。需要时间维度的权限审计。</details>

3. **菜单设计**：菜单可以是"分组节点"（不跳页面）或"叶子节点"（点击跳页），怎么区分？  
   <details><summary>答案</summary>CP6 用 <code>RoutePath</code> 为 null 区分。前端递归渲染时 <code>v-if="m.children.length"</code> 显示子菜单，否则显示菜单项。也可以加 <code>MenuType</code> 字段（'GROUP' / 'PAGE' / 'BUTTON'）支持按钮级权限。</details>

4. **数据权限**：营业员只能看自己负责的客户，怎么在 EF Core 里全局加这个 WHERE？  
   <details><summary>答案</summary>用 <code>HasQueryFilter</code> 全局过滤 + 注入当前用户上下文：
   <pre><code>// CP6Context.OnModelCreating
   modelBuilder.Entity&lt;Customer&gt;().HasQueryFilter(c =&gt; c.SalesUserId == _currentUser.Id);</code></pre>
   注意：<code>HasQueryFilter</code> 用闭包捕获 <code>_currentUser</code> 时要小心，DbContext 是 Scoped 跟用户对应，但表达式只在 model 构建时编译一次。生产推荐用 <code>SetDbContext</code> 时注入。</details>

5. **质疑题**：有人提议"所有按钮也按权限控制（按钮级 RBAC）"，你怎么权衡？  
   <details><summary>答案</summary>价值：业务部门希望"普通员工不能看到删除按钮"。代价：Sys_Menu 表会膨胀 5~10 倍（每个页面 N 个按钮）。<b>折中方案</b>：菜单级控页面是否能进；按钮级在前端用统一 <code>v-permission="'wms.stock.delete'"</code> 指令实现，后端 API 加 <code>[Authorize(Policy="wms.stock.delete")]</code> 兜底校验。前后端校验同源——一份"权限点"清单，前端控显示，后端控访问。</details>

---

## 🔗 延伸阅读

- [Vue I18n 官方文档](https://vue-i18n.intlify.dev/)
- [RBAC 经典论文 (NIST)](https://csrc.nist.gov/publications/detail/conference-paper/2000/07/26/the-nist-model-for-role-based-access-control-towards-a-unified-st)
- [Materialized Path Tree (DB Patterns)](https://www.databasestar.com/database-design-patterns-for-tree-structures/)
- 项目内：`docs/wms-menu-seed.sql`、`docs/wms-*-i18n-seed.sql`、`CP6.WebApi/Controllers/LangController.cs`
