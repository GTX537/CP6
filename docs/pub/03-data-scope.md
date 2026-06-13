# PUB 03 · 数据权限 DataScope 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-03 数据权限 DataScope |
| 所属模块 | PUB 公共平台 · Part 1 权限引擎 |
| 里程碑 | **M2**（三权第二权：你能看哪些行） |
| 技术栈 | Vue3 + Element Plus / .NET8 + EF Core（IQueryable 注入）/ SQL Server |
| 命名空间 | `Sys` |
| 前置 | [章00 组织模型](./00-org-model.md)（部门树 Path 子树）、[章01 多角色](./01-rbac-multirole.md)（DataScopes 最宽合并） |

> **题眼**：功能权限（章02）管"**能不能点这个按钮**"；数据权限管"**点了之后能看到哪些行**"。同一个"订单查询"功能，老板看全部、主管看本部门及下级、专员只看自己的——区别不在功能权限，在**数据范围**。本章在**查询层自动注入过滤条件**：`service` 查订单时，`IDataScopeFilter.Apply(query, "order", ctx)` 按角色的范围追加 `Where`，从源头收窄结果集。多角色取最宽。

---

## 目录
- 第1章 概述（能点 vs 能看哪些行）
- 第2章 五种数据范围 ScopeType
- 第3章 数据模型（Sys_RoleDataScope）
- 第4章 被授权实体的前提（IDataScoped）
- 第5章 IDataScopeFilter 查询注入
- 第6章 多角色取最宽（DataScopes 聚合）
- 第7章 资源键 ResourceKey 与注册
- 第8章 数据权限配置画面
- 第9章 字段明细 / 控制矩阵
- 第10章 处理详细
- 第11章 API 接口设计
- 第12章 消息一览
- 第13章 集成与依赖

---

## 第1章 概述

| 维度 | 功能权限（章02） | 数据权限（本章） |
|---|---|---|
| 问题 | 能不能执行某操作 | 能看到哪些数据行 |
| 实现 | `[RequirePermission]` 403 拦截 | 查询层注入 `Where` 收窄 |
| 粒度 | 操作（menu:action） | 资源 × 范围（resourceKey × ScopeType） |
| 绕过 | 后端 403 | **从查询源头过滤，查不到就是查不到** |

**范围**：五种数据范围 + 角色资源范围配置 + 查询注入过滤器 + 多角色最宽合并。
**不含**：字段级（章04，控制"哪些列"，本章控制"哪些行"）。

---

## 第2章 五种数据范围 ScopeType

| ScopeType | 含义 | 过滤逻辑 | 宽窄 |
|---|---|---|---|
| 1 | 仅本人 | `Creator == 当前用户` | 最窄 |
| 2 | 本部门 | `DeptId == 用户部门` | |
| 3 | 本部门及下级 | 记录部门 `Path` 以 用户部门 `Path` 为前缀（子树） | |
| 4 | 自定义部门 | `DeptId IN CustomDeptIds` | |
| 5 | 全部 | 不加过滤 | 最宽 |

> **数值越大越宽**（1<2<3<4*<5），多角色合并取 `MAX`（第6章）。`*` 自定义(4) 的宽窄取决于配的部门集，排序上置于"及下级"与"全部"之间作为合并兜底，实际范围以 CustomDeptIds 为准。

---

## 第3章 数据模型

```csharp
// CP6.Entity/DomainModels/Sys/Sys_RoleDataScope.cs（新建）
[Table("Sys_RoleDataScope")]
public class Sys_RoleDataScope : BaseEntity
{
    public Guid   RoleId        { get; set; }            // → Sys_Role.Id
    public string ResourceKey   { get; set; } = "";      // 业务资源，如 order / po / customer
    public int    ScopeType     { get; set; }            // 1本人/2本部门/3及下级/4自定义/5全部
    public string? CustomDeptIds { get; set; }           // ScopeType=4：逗号分隔部门Id（或JSON）
}
```
```sql
CREATE UNIQUE INDEX UX_Sys_RoleDataScope ON Sys_RoleDataScope(TenantId, RoleId, ResourceKey);
CREATE INDEX IX_Sys_RoleDataScope_Role ON Sys_RoleDataScope(TenantId, RoleId);
```

> 一个角色对一个资源**一条范围**（UX 唯一）。没配的资源走默认范围（第7章，建议默认"仅本人"或"无权限"，按资源注册时声明）。

---

## 第4章 被授权实体的前提（IDataScoped）

数据范围要过滤，记录本身得带"归谁/归哪个部门"。让可数据授权的实体实现接口：

```csharp
// CP6.Entity/IDataScoped.cs
public interface IDataScoped
{
    string? Creator { get; }   // 创建人（本人范围用）—— 多数实体 BaseEntity 已有
    Guid?   DeptId  { get; }   // 归属部门（本部门/及下级/自定义范围用）
}
```

- `Creator` 多数实体的 `BaseEntity` 已有。
- `DeptId` 需要业务实体补：记录创建时写入创建人的部门（或单据归属部门）。**没有 DeptId 的实体只能用 本人/全部 两种范围**。

> **数据权限不是凭空过滤**：它依赖记录上的 `Creator`/`DeptId`。接入数据权限的实体必须先有这两个锚点——这是数据权限可行的前提，接入清单在资源注册（第7章）声明每个资源支持哪些范围。

---

## 第5章 IDataScopeFilter 查询注入

```csharp
// CP6.Core/Services/Sys/IDataScopeFilter.cs
public interface IDataScopeFilter
{
    IQueryable<T> Apply<T>(IQueryable<T> query, string resourceKey, UserPermissionContext ctx)
        where T : class, IDataScoped;
}

// 实现：按聚合好的最宽范围追加 Where
public IQueryable<T> Apply<T>(IQueryable<T> q, string resourceKey, UserPermissionContext ctx)
    where T : class, IDataScoped
{
    var scope = ctx.DataScopes.GetValueOrDefault(resourceKey, /*默认*/ 1);
    switch (scope)
    {
        case 5: return q;                                            // 全部
        case 1: return q.Where(x => x.Creator == ctx.UserName);      // 仅本人
        case 2: return q.Where(x => x.DeptId == ctx.DeptId);         // 本部门
        case 3:                                                       // 本部门及下级（物化路径子树）
            return q.Where(x => _db.Sys_Depts
                .Any(d => d.Id == x.DeptId && d.Path.StartsWith(ctx.DeptPath)));
        case 4:                                                       // 自定义部门
            var ids = ctx.CustomDeptIds.GetValueOrDefault(resourceKey, new());
            return q.Where(x => x.DeptId != null && ids.Contains(x.DeptId.Value));
        default: return q.Where(x => false);                         // 未知 → 空（保守）
    }
}
```

业务 `service` 查询时一行接入：
```csharp
var q = _db.Orders.AsQueryable();
q = _scope.Apply(q, "order", ctx);          // ★数据权限在此注入
return await q.ToListAsync();
```

> **注入在 `IQueryable` 阶段**：过滤条件下推到 SQL，数据库层就把看不到的行挡在外面——不是查出来再内存过滤（那样既慢又可能泄露）。"及下级"用 `Path.StartsWith` 命中整棵子树，零递归。

---

## 第6章 多角色取最宽（DataScopes 聚合）

章01 `PermissionAggregator` 填充 `ctx.DataScopes`（每个资源取**最宽** ScopeType）：

```csharp
// 章01 BuildAsync 中（本章提供逻辑）
var rows = await _db.Sys_RoleDataScopes.Where(ds => roleIds.Contains(ds.RoleId)).ToListAsync();
ctx.DataScopes = rows.GroupBy(ds => ds.ResourceKey)
    .ToDictionary(g => g.Key, g => g.Max(x => x.ScopeType));         // 每资源取最宽
ctx.CustomDeptIds = rows.Where(ds => ds.ScopeType == 4)
    .GroupBy(ds => ds.ResourceKey)
    .ToDictionary(g => g.Key, g => g.SelectMany(x => Parse(x.CustomDeptIds)).Distinct().ToList()); // 自定义取并集
```

`UserPermissionContext` 为数据权限**扩展**三个字段（登录时从章00 组织取）：
```csharp
public string UserName { get; set; }      // 本人范围
public Guid?  DeptId   { get; set; }      // 本部门范围
public string DeptPath { get; set; }      // 及下级范围（子树前缀）
```

> 例：角色A 对 order 配"本部门(2)"、角色B 配"全部(5)" → 合并 = 全部(5)，该用户看全部订单。能力叠加取最宽（章01 口径）。

---

## 第7章 资源键 ResourceKey 与注册

- `ResourceKey` = 业务资源标识（`order`/`po`/`customer`/`gr`…），与功能权限的 `menuKey` 可同名但语义不同（这里指"数据资源"）。
- **资源注册表**：声明每个数据资源支持哪些范围、默认范围、实体类型：

```csharp
DataScopeRegistry.Register("order", typeof(Order), supports: [1,2,3,4,5], @default: 1);
DataScopeRegistry.Register("customer", typeof(Customer), supports: [1,5], @default: 1); // 无DeptId只能本人/全部
```

> 注册表让配置画面知道"这个资源能配哪些范围"，也让未配资源有明确默认（保守起见默认最窄"仅本人"或资源声明的默认）。

---

## 第8章 数据权限配置画面

角色管理 → 数据权限 Tab：

| 区域 | 内容 |
|---|---|
| 角色（上下文） | 当前配置的角色 |
| 资源列表 | 来自资源注册表的数据资源（订单/采购单/客户…） |
| 范围下拉 | 每个资源选 ScopeType（仅显示该资源 supports 的范围） |
| 自定义部门 | ScopeType=自定义时 → 部门树多选（写 CustomDeptIds） |
| 按钮 | 保存 → 失效该角色下用户缓存 |

---

## 第9章 字段明细 / 控制矩阵

| 字段 | 控件 | 说明 |
|---|---|---|
| resourceKey | 只读(资源名) | 来自注册表 |
| scopeType | 下拉 | 仅列资源 supports 的范围 |
| customDeptIds | 部门树多选 | 仅 scopeType=自定义 时启用 |

**控制矩阵**：`customDeptIds` 仅在 `scopeType=4自定义` 时可用，其余禁用。

---

## 第10章 处理详细

### 10.1 配置数据范围（保存）
```
每资源 upsert Sys_RoleDataScope(RoleId, ResourceKey, ScopeType, CustomDeptIds)
校验：ScopeType ∈ 资源 supports（E-PUB-031）；自定义时 CustomDeptIds 非空（E-PUB-032）
完成 → 失效该角色下所有用户缓存
```

### 10.2 查询注入（运行期）
```
service 查询 → IDataScopeFilter.Apply(query, resourceKey, ctx)
  → 取 ctx.DataScopes[resourceKey]（多角色最宽）
  → 按 ScopeType 追加 Where（本人/本部门/及下级Path子树/自定义/全部）
  → SQL 层过滤
```

---

## 第11章 API 接口设计（.NET8）

前缀 `/api/pub/data-scope`：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/resources` | GET | 数据资源注册表（资源 + 支持范围） |
| `/{roleId}` | GET | 取角色各资源的数据范围 |
| `/{roleId}` | PUT | 保存（upsert + 失效缓存） |

`IDataScopeFilter` 为内部服务（各业务 service 调用），不暴露 HTTP。

---

## 第12章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-031 | Error | 该资源不支持所选数据范围 | ScopeType ∉ 资源 supports |
| E-PUB-032 | Error | 自定义范围必须选择部门 | scopeType=自定义 但 CustomDeptIds 空 |

---

## 第13章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 章00 组织模型 | "及下级"用部门 `Path` 子树；ctx 扩展 DeptId/DeptPath |
| ← 章01 多角色 | `DataScopes` 取最宽、`CustomDeptIds` 取并集，存 UserPermissionContext |
| → 各业务 service | 查询接 `IDataScopeFilter.Apply(q, resourceKey, ctx)` 一行注入 |
| ← 业务实体 | 须实现 `IDataScoped`（Creator/DeptId）；无 DeptId 仅支持本人/全部 |
| → 章04 字段权限 | 行过滤后，列再按字段权限掩码 |

> **三权第二权**：功能权限挡操作、数据权限挡行、字段权限（章04）挡列。三者按多角色合并（操作并集、范围最宽、字段最宽）共同决定一个用户的实际可见与可为。

---

## 自检
- [ ] 数据权限和功能权限的区别？为什么同一功能不同人看到的行不同？
- [ ] 五种范围分别怎么过滤？"及下级"为什么用 Path 而非递归？
- [ ] 实体接入数据权限的前提是什么？没有 DeptId 的实体怎么办？
- [ ] 多角色的数据范围怎么合并？为什么取最宽？
- [ ] 查询注入为什么在 IQueryable 阶段而非查出来再过滤？

---

*实现：新建 `CP6.Entity/DomainModels/Sys/Sys_RoleDataScope.cs` + `CP6.Entity/IDataScoped.cs` + `CP6.Core/Services/Sys/{DataScopeFilter,DataScopeRegistry}.cs`；扩展 `UserPermissionContext`（UserName/DeptId/DeptPath）+ 章01 聚合填充；业务实体实现 `IDataScoped`、service 接 `Apply`。配套 xlsx 详细设计见同名 `.xlsx`。*
