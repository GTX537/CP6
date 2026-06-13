# PUB 01 · 多角色 RBAC 升级 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-01 多角色 RBAC |
| 所属模块 | PUB 公共平台 · Part 1 权限引擎 |
| 里程碑 | **M0**（权限引擎的起点；章02-04 的合并框架在此建立） |
| 技术栈 | Vue3 + Element Plus + Pinia / .NET8 Web API + EF Core / SQL Server |
| 命名空间 | `Sys`（`DomainModels/Sys`、`Services/Sys`） |
| 前置 | [章00 组织模型](./00-org-model.md)（DataScope 最宽合并会用到部门） |

> **题眼**：CP6 现状是 `Sys_User.RoleId` **单个 int——一人只能一个角色**，多职能场景（既是采购员又是部门主管）塞不下。本章升级为**多角色**：新建 `Sys_UserRole` 中间表，并确立**权限并集求解**的统一框架——一个用户的最终权限 = 其全部角色的"菜单/操作取并集、数据范围取最宽、字段权限取最宽"。章02-04 往这个框架里填三类权限的细节。

---

## 目录
- 第1章 功能概述（单角色 → 多角色）
- 第2章 数据模型（Sys_UserRole + 主角色保留）
- 第3章 权限聚合框架 UserPermissionContext
- 第4章 多角色合并口径（并集 / 最宽 / 最宽）
- 第5章 用户角色分配画面
- 第6章 字段明细
- 第7章 字段控制矩阵
- 第8章 处理详细（分配 / 登录聚合 / 缓存 / 迁移）
- 第9章 权限并集求解算法
- 第10章 API 接口设计
- 第11章 消息一览
- 第12章 集成与依赖

---

## 第1章 功能概述（单角色 → 多角色）

| 能力 | 现状 | 升级后 |
|---|---|---|
| 用户↔角色 | `Sys_User.RoleId` 单个 int | `Sys_UserRole` 中间表，一人多角色 |
| 权限求解 | 单角色直接取 | 多角色**合并**：并集/最宽 |
| 兼容 | — | `Sys_User.RoleId` 保留为"主角色"，旧代码与默认角色不破 |

**范围**：多角色中间表 + 权限聚合框架 + 用户角色分配画面 + 单角色→多角色迁移。
**不含**：菜单/操作权限本身（章02）、数据权限（章03）、字段权限（章04）——本章只建"合并框架"，三类权限的内容由后续章填充。

---

## 第2章 数据模型

```csharp
// CP6.Entity/DomainModels/Sys/Sys_UserRole.cs（新建）
[Table("Sys_UserRole")]
public class Sys_UserRole : BaseEntity   // 含 Id/TenantId/CreateTime
{
    public Guid UserId { get; set; }   // → Sys_User.Id
    public Guid RoleId { get; set; }   // → Sys_Role.Id
}
```
```sql
CREATE TABLE Sys_UserRole (
    Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TenantId  UNIQUEIDENTIFIER NOT NULL,
    UserId    UNIQUEIDENTIFIER NOT NULL,
    RoleId    UNIQUEIDENTIFIER NOT NULL,
    CreateTime DATETIME2 NOT NULL
);
CREATE UNIQUE INDEX UX_Sys_UserRole ON Sys_UserRole(TenantId, UserId, RoleId);  -- 防重复授予
CREATE INDEX IX_Sys_UserRole_User ON Sys_UserRole(TenantId, UserId);
```

**`Sys_User.RoleId` 保留**，语义从"唯一角色"改为"**主角色**"：
- 兼容旧代码（仍读 `RoleId` 的地方不报错）；
- 作为"默认角色"（新建单据的默认归属、UI 默认显示）；
- **不再作为权限求解的唯一来源**——权限求解以 `Sys_UserRole` 全集为准（含主角色）。

> **为什么不删 `RoleId`、不全表大改？** 沿用 PUB"避免大重构"的命名空间策略：保留 `RoleId` 为主角色，新增中间表承载多角色，旧代码零改动平滑迁移。删字段会牵动一片调用方，得不偿失。

---

## 第3章 权限聚合框架 UserPermissionContext

用户登录后，把其全部角色的三类权限**聚合**成一个会话级上下文，后端每次校验据此判断（不每次查库）：

```csharp
// CP6.Core/Services/Sys/UserPermissionContext.cs
public class UserPermissionContext
{
    public Guid       UserId   { get; set; }
    public List<Guid> RoleIds  { get; set; } = new();   // 全部角色（Sys_UserRole ∪ 主角色）

    public HashSet<string> MenuKeys   { get; set; } = new();   // 菜单并集（本章）
    public HashSet<string> ActionKeys { get; set; } = new();   // 操作并集 "menu:action"（章02 填充）
    public Dictionary<string,int> DataScopes  { get; set; } = new();   // resourceKey→最宽 ScopeType（章03）
    public Dictionary<string,Dictionary<string,int>> FieldPerms { get; set; } = new(); // 章04
}
```

```csharp
// CP6.Core/Services/Sys/PermissionAggregator.cs
public async Task<UserPermissionContext> BuildAsync(Guid userId)
{
    var roleIds = await GetAllRoleIdsAsync(userId);            // Sys_UserRole ∪ {主角色}，去重
    var ctx = new UserPermissionContext { UserId = userId, RoleIds = roleIds };

    // 本章：菜单并集
    ctx.MenuKeys = (await _db.Sys_RoleMenus
        .Where(rm => roleIds.Contains(rm.RoleId))
        .Select(rm => rm.MenuKey).ToListAsync()).ToHashSet();

    // 章02 填充 ActionKeys（并集）、章03 填充 DataScopes（最宽）、章04 填充 FieldPerms（最宽）
    return ctx;   // 缓存：登录写入会话/分布式缓存，登出或角色变更时失效
}
```

> 聚合上下文在**登录时构建一次**，缓存到会话（或 Redis）。用户的角色被改动时（分配/移除角色）需**主动失效**该用户缓存，下次请求重建——否则权限变更不生效。

---

## 第4章 多角色合并口径

三类权限的合并规则**不同**，这是多角色的核心：

| 权限类型 | 合并规则 | 理由 | 章 |
|---|---|---|---|
| 菜单 / 操作 | **并集（OR）** | 任一角色给了就有——多职能叠加能力 | 本章 / 02 |
| 数据范围 DataScope | **取最宽** | 角色A"本部门" + 角色B"全部" = 全部 | 03 |
| 字段权限 | **取最宽**（可读 > 只读 > 隐藏） | 任一角色可读则可读 | 04 |

```
最宽 DataScope：ScopeType 数值越大越宽（1本人<2本部门<3及下级<4自定义<5全部 见章03），取 MAX
最宽 字段权限：Access 1可读 > 2只读 > 3隐藏，取"最可见"
```

> **为什么数据/字段取最宽而非并集/最严？** 角色代表"被授予的能力"，多个角色是能力叠加，不是限制叠加——给你两顶帽子，你两顶的视野都有。取最严会让多角色用户反而看得更少，违反直觉。**能力叠加 = 取最宽。**

---

## 第5章 用户角色分配画面

在用户管理画面中，为用户分配多角色（穿梭框 `el-transfer` 或多选）：

| 区域 | 内容 |
|---|---|
| 用户基本信息 | 用户名/昵称/所属部门/直属上级（章00 字段）/邮箱/启用 |
| 角色分配区 | **穿梭框**：左=可选角色，右=已授予角色（多选）|
| 主角色 | 在已授予角色中**单选**一个标为主角色（写 `Sys_User.RoleId`）|
| 按钮 | 保存 / 取消 |

操作种别：维护（编辑用户的角色集合）。

---

## 第6章 字段明细

| 字段 | 中文名 | 控件 | 必填 | 说明 |
|---|---|---|---|---|
| userId | 用户 | 只读 | — | 上下文用户 |
| roleIds | 已授予角色 | 穿梭框(多选) | 否 | 写 Sys_UserRole 全集 |
| primaryRoleId | 主角色 | 单选(在已授予中) | 是 | 写 Sys_User.RoleId；必须 ∈ roleIds |

---

## 第7章 字段控制矩阵（用户角色分配·维护）

| 字段 | 编辑 | 说明 |
|---|---|---|
| 已授予角色 | 可用 | 多选增减 |
| 主角色 | 可用 | 必须从"已授予角色"里选；移除某角色时若它是主角色 → 报 E-PUB-011 |

---

## 第8章 处理详细

### 8.1 分配角色
```
保存 → diff 计算新增/移除的 (UserId,RoleId)
  新增 → insert Sys_UserRole（防重复：UX 唯一索引 / 先查）
  移除 → delete Sys_UserRole
校验：primaryRoleId 必须 ∈ 最终 roleIds（E-PUB-011）
完成 → 失效该用户的 UserPermissionContext 缓存
```

### 8.2 登录聚合
```
登录成功 → PermissionAggregator.BuildAsync(userId) → 缓存会话
```

### 8.3 缓存失效
```
触发点：用户角色变更 / 角色的权限变更(章02-04) / 角色被删
动作：失效相关用户的 UserPermissionContext，下次请求重建
```

### 8.4 单角色 → 多角色 迁移（一次性）
```
foreach 现有 Sys_User（RoleId 有值）:
    if not exists Sys_UserRole(UserId, RoleId): insert
Sys_User.RoleId 保留为主角色（不清空）
→ 迁移后：老用户的单角色变成 Sys_UserRole 里的一条 + 主角色，权限不变
```

---

## 第9章 权限并集求解算法

```csharp
// 取用户全部角色（中间表 ∪ 主角色），去重
private async Task<List<Guid>> GetAllRoleIdsAsync(Guid userId)
{
    var roles = await _db.Sys_UserRoles.Where(ur => ur.UserId == userId)
                    .Select(ur => ur.RoleId).ToListAsync();
    var primary = (await _db.Sys_Users.FindAsync(userId))?.RoleId;
    if (primary is Guid p && !roles.Contains(p)) roles.Add(p);   // 主角色并入
    return roles.Distinct().ToList();
}

// 合并示例：DataScope 取最宽（章03 调用）
int MergeDataScope(IEnumerable<int> scopeTypesAcrossRoles)
    => scopeTypesAcrossRoles.DefaultIfEmpty(0).Max();   // 数值越大越宽
```

| 场景 | 角色A | 角色B | 合并结果 |
|---|---|---|---|
| 菜单 | 订单、采购 | 采购、库存 | 订单、采购、库存（并集） |
| 数据范围(order) | 本部门(2) | 全部(5) | 全部(5)（最宽） |
| 字段(成本) | 隐藏(3) | 只读(2) | 只读(2)（最宽=最可见） |

---

## 第10章 API 接口设计（.NET8）

前缀 `/api/pub/user-role`：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/{userId}` | GET | 取用户已授予角色 + 主角色 |
| `/{userId}` | PUT | 保存用户角色集合（含主角色），diff 增删 + 失效缓存 |
| `/migrate` | POST | 一次性迁移：单角色 → Sys_UserRole（幂等） |

权限聚合 `PermissionAggregator` 为内部服务（登录管线调用），不单独暴露 HTTP。

---

## 第11章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-011 | Error | 主角色必须是已授予的角色之一 | primaryRoleId ∉ roleIds |
| E-PUB-012 | Warning | 该用户未分配任何角色 | roleIds 为空保存 |

---

## 第12章 集成与依赖

| 关系 | 说明 |
|---|---|
| → 章02 功能权限 | 往 `ActionKeys` 填操作并集；强校验读本上下文 |
| → 章03 数据权限 | 往 `DataScopes` 填最宽范围 |
| → 章04 字段权限 | 往 `FieldPerms` 填最宽 |
| ← 章00 组织模型 | DataScope 最宽合并涉及部门（章03） |
| ← 现有 Sys_User/Sys_Role/Sys_RoleMenu | 复用；`RoleId` 保留为主角色 |
| 多租户 | Sys_UserRole 带 TenantId |

> **本章是权限引擎的地基**：`UserPermissionContext` 这个会话级聚合上下文，是章02 强校验、章03 查询注入、章04 序列化掩码的共同数据源。三类权限"怎么合并"在本章定调（并集/最宽/最宽），后续章只填内容。

---

## 自检
- [ ] 为什么保留 `Sys_User.RoleId`？它现在是什么语义？
- [ ] 三类权限的多角色合并规则各是什么？数据/字段为什么取最宽而非最严？
- [ ] `UserPermissionContext` 何时构建、何时失效？不失效会怎样？
- [ ] 单角色→多角色怎么迁移才能保证老用户权限不变？
- [ ] 主角色与已授予角色集合是什么约束关系？

---

*实现：新建 `CP6.Entity/DomainModels/Sys/Sys_UserRole.cs` + `CP6.Core/Services/Sys/{PermissionAggregator,UserPermissionContext}.cs` + 用户角色分配 UI；`Sys_User.RoleId` 保留为主角色。配套 xlsx 详细设计见同名 `.xlsx`。*
