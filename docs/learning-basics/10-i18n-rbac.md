# 10 · i18n + 菜单权限（RBAC）

## 🌱 你将学到

- 多语言（i18n）的两种主流做法：JSON 文件 vs DB 表，分别什么场景
- 看懂 CP6 的 `Sys_Langs` 表设计 + 扁平 key 转嵌套对象
- 理解 RBAC（角色-菜单-用户）三张表怎么连
- 看懂前端"递归渲染菜单"那段奇怪代码

---

## 🍳 生活类比

### i18n：餐厅菜单的两个版本

**做法 A：印刷多语言菜单**（JSON 文件）
中英日韩各印一份纸质菜单。改一个菜名 → 重印所有版本。

**做法 B：菜单背后接电子屏**（DB 表）
菜单是电子屏，翻译存在后台 DB 里。改一个翻译 → 推一下，所有屏立刻更新。

CP6 用做法 B：翻译存数据库，翻译人员直接在后台改。

### RBAC：公司的门禁卡

公司里：

- 每个人有一张门禁卡（用户）
- 每张卡属于一种角色（员工 / 主管 / CEO）
- 每个房间有"哪些角色能进"的设置（菜单的权限）

新员工入职：发卡 + 设角色 → 自动有对应权限。换角色 → 改一行配置 → 立刻生效。

---

## 🔎 看 CP6 代码

### Sys_Langs 表

```csharp
public class Sys_Lang : BaseEntity
{
    public string LangKey { get; set; }   // 扁平 key，如 "login.username"
    public string? ZhCN { get; set; }     // 简体中文
    public string? ZhTW { get; set; }     // 繁体中文
    public string? En { get; set; }       // 英语
    public string? Ja { get; set; }       // 日语
    public string? Ko { get; set; }       // 韩语
}
```

一行存一个 key 的 5 种翻译。

种子 SQL（CP6 用这种 MERGE 模式 upsert）：

```sql
MERGE INTO Sys_Langs AS T
USING (VALUES
    ('login.username',     N'用户名',  N'用戶名',  N'Username', N'ユーザー名', N'사용자명'),
    ('login.password',     N'密码',    N'密碼',    N'Password', N'パスワード', N'비밀번호'),
    ('wms.stock.title',    N'库存查询', N'庫存查詢', N'Stock Query', N'在庫照会', N'재고 조회')
) AS S (LangKey, ZhCN, ZhTW, En, Ja, Ko) ON T.LangKey = S.LangKey
WHEN MATCHED THEN UPDATE SET T.ZhCN=S.ZhCN, T.ZhTW=S.ZhTW, ...
WHEN NOT MATCHED THEN INSERT (Id, LangKey, ZhCN, ZhTW, En, Ja, Ko, ...)
    VALUES (NEWID(), S.LangKey, S.ZhCN, S.ZhTW, S.En, S.Ja, S.Ko, ...);
```

### LangController API

```csharp
[HttpGet("{locale}")]
[AllowAnonymous]   // 翻译不需要登录就能拿
public async Task<IActionResult> Get(string locale)
{
    var data = await _cache.GetOrSetAsync($"lang:{locale}", async () =>
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
```

返回的是扁平字典：

```json
{
  "login.username": "用户名",
  "login.password": "密码",
  "wms.stock.title": "库存查询"
}
```

### 前端 i18n

`cp6.web/src/i18n/index.ts`：

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
  // 把扁平 key 转嵌套对象（vue-i18n 习惯嵌套）
  const messages = unflatten(data.data)
  i18n.global.setLocaleMessage(locale, messages)
}

function unflatten(flat: Record<string, string>) {
  const result: any = {}
  for (const [key, val] of Object.entries(flat)) {
    const parts = key.split('.')           // "login.username" → ["login", "username"]
    let cur = result
    for (let i = 0; i < parts.length - 1; i++) {
      if (!cur[parts[i]]) cur[parts[i]] = {}
      cur = cur[parts[i]]
    }
    cur[parts[parts.length - 1]] = val
  }
  return result
}
```

扁平 → 嵌套转换示例：

```
输入：
{ "login.username": "用户名", "login.password": "密码" }

输出：
{ login: { username: "用户名", password: "密码" } }
```

模板用：

```vue
<el-button>{{ $t('login.signin') }}</el-button>
```

### RBAC 三张表

```csharp
public class Sys_User : BaseEntity
{
    public string UserName { get; set; }
    public string PasswordHash { get; set; }
    public Guid? RoleId { get; set; }      // 关联到 Sys_Role.Id
}

public class Sys_Role : BaseEntity
{
    public string RoleName { get; set; }
}

public class Sys_Menu : BaseEntity
{
    public int MenuId { get; set; }        // 注意：int 而不是 Guid，方便树形 ParentId
    public int? ParentId { get; set; }
    public string MenuName { get; set; }   // i18n key，如 "menu.wms.stock"
    public string? RoutePath { get; set; } // "/wms/stock"
    public string? Icon { get; set; }
    public int OrderNo { get; set; }
}

public class Sys_RoleMenu : BaseEntity
{
    public Guid RoleId { get; set; }       // 哪个角色
    public int MenuId { get; set; }        // 能进哪个菜单
}
```

关系：

```
Sys_User --N→1-- Sys_Role
Sys_Role --1→N-- Sys_RoleMenu --N→1-- Sys_Menu
```

### 获取用户菜单 API

```csharp
[HttpGet("user")]
[Authorize]
public async Task<IActionResult> GetUserMenus()
{
    var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // 1. 找用户的角色
    var roleId = await _context.Sys_Users
        .Where(u => u.Id == userId)
        .Select(u => u.RoleId)
        .FirstAsync();

    // 2. 找角色能进的菜单
    var menuIds = await _context.Sys_RoleMenus
        .Where(rm => rm.RoleId == roleId)
        .Select(rm => rm.MenuId)
        .ToListAsync();

    // 3. 拿菜单详情
    var menus = await _context.Sys_Menus
        .Where(m => menuIds.Contains(m.MenuId))
        .OrderBy(m => m.OrderNo)
        .AsNoTracking()
        .ToListAsync();

    return Ok(new { code = 200, data = menus });
}
```

### 前端递归渲染菜单

```vue
<script setup>
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()

// 平铺的 menus 列表 → 树形结构
const tree = computed(() => buildTree(auth.menus))

function buildTree(menus, parentId = null) {
  return menus
    .filter(m => m.parentId === parentId)
    .map(m => ({ ...m, children: buildTree(menus, m.menuId) }))
}
</script>

<template>
  <el-menu router>
    <template v-for="m in tree" :key="m.menuId">
      <!-- 有 children → 子菜单 -->
      <el-sub-menu v-if="m.children.length" :index="String(m.menuId)">
        <template #title>{{ $t(m.menuName) }}</template>
        <MenuItem v-for="c in m.children" :key="c.menuId" :menu="c" />
      </el-sub-menu>
      <!-- 没 children → 直接菜单项 -->
      <el-menu-item v-else :index="m.routePath">
        {{ $t(m.menuName) }}
      </el-menu-item>
    </template>
  </el-menu>
</template>
```

---

## 🤔 为什么这样

### Q1: 翻译为什么不放 JSON 文件

放 JSON 简单，但缺点：

- 翻译人员要改 → 找开发改 → 重新部署
- 翻译多套（zh/en/ja/ko）易漏
- 没有审计（谁改的、什么时候改的）

CP6 放 DB 后：

- 翻译人员直接登录后台改（不需要开发参与）
- 改完清缓存立即生效
- DB 自带审计

代价：每次首屏要拉一次翻译（CP6 加缓存抵消）。

### Q2: 为什么 key 用扁平 + . 分隔

```
扁平：     "wms.stock.column.product"
嵌套：    { wms: { stock: { column: { product: "..." } } } }
```

DB 里存扁平方便（一列搞定）。前端用嵌套方便（vue-i18n 标准）。中间用 `unflatten` 转。

### Q3: Sys_Menu 为什么用 int 不用 Guid

CP6 大部分 entity 用 Guid 主键。Sys_Menu 用 int，因为：

- ParentId 自引用，int 更省 + 显示更直观（"id=5 的子是 id=12, 13, 14"）
- 菜单数量少（几十到几百）
- 前端排序按 OrderNo int，跟 MenuId int 类型一致

这是"特殊场景突破公约"的例子。学到资深时你会知道什么时候该破例。

### Q4: RBAC 为什么三张表不是两张

最简单方式：用户表直接有 `permissions` 字段（JSON 列表）。但：

- 改"主管能进哪些菜单"要更新每个主管的字段
- 没法独立管理角色

三张表分离：
- 改角色权限 → 只动 Sys_RoleMenu
- 用户调岗 → 只动 Sys_User.RoleId
- 单元清晰

### Q5: 单角色 vs 多角色

CP6 是**单角色**（每个用户一个 role）。

多角色版本：用户 N 对 N 角色（多一张 `Sys_UserRole` 表）。更灵活但权限合并逻辑复杂。

CP6 业务用户角色简单，单角色够用。

---

## ⚠️ 容易搞错的地方

### 1. i18n 首屏白屏

```typescript
// ❌ 反例
createApp(App).use(i18n).mount('#app')   // 立刻渲染
await initI18n()                          // 太晚
```

CP6 的修复（main.ts）：必须 `await initI18n()` 先，再 createApp。

### 2. 翻译 key 拼写错

```vue
<el-button>{{ $t('login.signin') }}</el-button>   <!-- DB 里只有 login.signIn -->
```

显示 `login.signin`（vue-i18n 默认 fallback 到 key 本身）。开发期可加 `missingWarn: true` 报警。

### 3. 菜单 RoutePath 和前端 viewModules 字典不同步

后端 Sys_Menu 加了 `/wms/new-feature`，但前端 `router/index.ts` 的 `viewModules` 没加这个 key → 点菜单空白页。

CP6 当前是手工同步两边。这是个改进点（CI 加校验）。

### 4. 父子角色不可继承

CP6 角色是平级的，没有"Admin 继承 User"。要的话要自己实现继承链。

### 5. 修改 RoleMenu 后用户菜单不立即更新

如果加了"用户菜单"缓存，改 RoleMenu 后老缓存还在。CP6 当前没缓存用户菜单（每次重新查），避免了这个坑。

---

## ✋ 动手试试

### 任务 1：看一次扁平 → 嵌套转换

新建一个 HTML 文件，把 `unflatten` 函数复制进去，自己试：

```javascript
const flat = {
  "login.username": "用户名",
  "login.password": "密码",
  "wms.stock.title": "库存查询",
  "wms.stock.column.product": "零件"
}

function unflatten(flat) {
  const result = {}
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

console.log(JSON.stringify(unflatten(flat), null, 2))
```

亲眼看到怎么从扁平变嵌套。

### 任务 2：在 DB 里加一个翻译，前端立刻看到

启动后端，连数据库执行：

```sql
INSERT INTO Sys_Langs (Id, LangKey, ZhCN, En, Ja, Ko, ZhTW, Creator, CreateDate, IsDeleted)
VALUES (NEWID(), 'test.hello', '你好', 'Hello', 'こんにちは', '안녕', '你好', 'me', GETDATE(), 0);
```

然后调 API：

```
GET http://localhost:9991/api/lang/zh-CN
```

应该看到返回 JSON 里有 `"test.hello": "你好"`。

**注意**：CP6 的 LangController 加了缓存 30 分钟。如果你刚改完调 API 没看到新值，要么等 30 分钟，要么改 API 里调 `_cache.RemoveAsync("lang:zh-CN")` 主动清。

### 任务 3：跑一次 RBAC 三张表 JOIN 查询

连数据库：

```sql
-- 看 admin 用户能进哪些菜单
SELECT m.MenuName, m.RoutePath
FROM Sys_Users u
JOIN Sys_RoleMenus rm ON rm.RoleId = u.RoleId
JOIN Sys_Menus m ON m.MenuId = rm.MenuId
WHERE u.UserName = 'admin'
ORDER BY m.OrderNo;
```

理解三张表是怎么连起来的。

### 任务 4：递归渲染树形菜单

在浏览器控制台执行：

```javascript
const flat = [
  { menuId: 1, parentId: null, menuName: '系统' },
  { menuId: 2, parentId: 1,    menuName: '用户管理' },
  { menuId: 3, parentId: 1,    menuName: '角色管理' },
  { menuId: 4, parentId: null, menuName: 'WMS' },
  { menuId: 5, parentId: 4,    menuName: '库存查询' }
]

function buildTree(menus, parentId = null) {
  return menus
    .filter(m => m.parentId === parentId)
    .map(m => ({ ...m, children: buildTree(menus, m.menuId) }))
}

console.log(JSON.stringify(buildTree(flat), null, 2))
```

看输出：平铺的 5 个变成 2 个根节点 + 子树。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/10-i18n-rbac.md`](../learning/10-i18n-rbac.md)——讲字段级 / 行级权限扩展
- Vue I18n 官方：[文档](https://vue-i18n.intlify.dev/)
- 关键词搜索："RBAC 权限设计"、"树形菜单 递归"
- 项目内：`docs/wms-menu-seed.sql`、`docs/wms-*-i18n-seed.sql`
