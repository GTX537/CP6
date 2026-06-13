# PUB 00 · 组织模型（部门树 Sys_Dept）详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-00 组织模型 |
| 所属模块 | PUB 公共平台 · Part 0（基座前置） |
| 里程碑 | **M0 前置**（PUB 数据权限 与 OA 审批路由的共同根） |
| 技术栈 | Vue3 + TypeScript + Element Plus + Pinia / .NET8 Web API + EF Core / SQL Server |
| 命名空间 | 组织主数据落 `Sys`（`DomainModels/Sys`、`Services/Sys`）；审批人解析器 `IApproverResolver` 落 `Wf`（OA 阶段1 消费） |
| 双消费方 | ① PUB 数据权限 DataScope 用 `Path` 子树过滤；② OA 审批路由用 `LeaderId/ManagerId` 解析"直属上级/部门长" |

> **题眼**：组织模型是 CP6 此前的硬缺口（`Sys_User` 只有 UserName/Password/NickName/RoleId/Enable）。本章补**部门树 `Sys_Dept`（物化路径）+ `Sys_User` 三字段 + 审批人解析器**。它一处建、两处用——PUB 的"只看本部门及下级"和 OA 的"找直属上级/部门长"都从这里算。

---

## 目录
- 第1章 功能概述与定位
- 第2章 数据模型（DDL）
- 第3章 物化路径 Path 维护逻辑
- 第4章 部门管理画面
- 第5章 字段明细（部门 / 用户组织字段）
- 第6章 字段控制矩阵
- 第7章 处理详细（部门 CRUD / Path 重算 / 删除校验）
- 第8章 审批人解析器 IApproverResolver（OA 消费）
- 第9章 数据权限 DataScope 用法（PUB 消费）
- 第10章 API 接口设计
- 第11章 消息一览
- 第12章 集成与依赖

---

## 第1章 功能概述与定位

**目的**：建立 CP6 的组织架构主数据，供两个消费方使用：
1. **PUB 数据权限**：DataScope"本部门 / 本部门及下级"靠部门树的物化路径 `Path` 做子树前缀匹配。
2. **OA 审批路由**：审批人"直属上级 / 部门负责人"靠 `Sys_User.ManagerId` 和 `Sys_Dept.LeaderId` 解析。

**范围**：部门树 CRUD + 物化路径维护 + 部门负责人设定 + 用户的部门/上级/邮箱维护 + 审批人解析器。
**不含**：角色/权限本身（章01-04）、审批流程（OA 阶段1+）。

---

## 第2章 数据模型（DDL）

```csharp
// CP6.Entity/DomainModels/Sys/Sys_Dept.cs（新建）
[Table("Sys_Dept")]
public class Sys_Dept : BaseEntity   // BaseEntity 含 Id/TenantId/CreateTime/Creator 等审计
{
    public Guid?   ParentId  { get; set; }            // 上级部门；根部门为 null
    public string  DeptCode  { get; set; } = "";      // 部门编码（租户内唯一）
    public string  DeptName  { get; set; } = "";
    public string  Path      { get; set; } = "";      // ★物化路径，如 "/{rootId}/{midId}/{selfId}/"
    public Guid?   LeaderId  { get; set; }            // 部门负责人 → Sys_User.Id
    public int     Sort      { get; set; }            // 同级排序
    public bool    Enable    { get; set; } = true;
}
```

```sql
CREATE TABLE Sys_Dept (
    Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER NOT NULL,
    ParentId    UNIQUEIDENTIFIER NULL,
    DeptCode    NVARCHAR(50)  NOT NULL,
    DeptName    NVARCHAR(100) NOT NULL,
    Path        NVARCHAR(900) NOT NULL,         -- 物化路径，建索引支持 LIKE 'prefix%'
    LeaderId    UNIQUEIDENTIFIER NULL,
    Sort        INT NOT NULL DEFAULT 0,
    Enable      BIT NOT NULL DEFAULT 1,
    CreateTime  DATETIME2 NOT NULL,
    Creator     NVARCHAR(50) NULL
);
CREATE UNIQUE INDEX UX_Sys_Dept_Tenant_Code ON Sys_Dept(TenantId, DeptCode);
CREATE INDEX IX_Sys_Dept_Path  ON Sys_Dept(TenantId, Path);     -- 子树前缀匹配
CREATE INDEX IX_Sys_Dept_Parent ON Sys_Dept(TenantId, ParentId);
```

**`Sys_User` 补三字段**（不改名、不动现有）：
```csharp
public Guid?   DeptId    { get; set; }   // 所属部门 → Sys_Dept.Id
public Guid?   ManagerId { get; set; }   // 直属上级 → Sys_User.Id
public string? Email     { get; set; }
```
```sql
ALTER TABLE Sys_User ADD DeptId UNIQUEIDENTIFIER NULL, ManagerId UNIQUEIDENTIFIER NULL, Email NVARCHAR(100) NULL;
CREATE INDEX IX_Sys_User_Dept ON Sys_User(TenantId, DeptId);
```

> **为什么用物化路径而非纯 ParentId？** "本部门及下级"是高频查询（每次数据权限过滤都用）。纯 `ParentId` 要递归 CTE；物化路径 `Path` 一次 `LIKE '前缀%'` 命中整棵子树，查询层零递归。代价是移动部门时要重算子树 Path（低频，见第3章）。

---

## 第3章 物化路径 Path 维护逻辑

`Path` = 从根到自身的 Id 串，形如 `/{rootId}/.../{selfId}/`（首尾带 `/`，保证前缀匹配不误命中）。

| 操作 | Path 维护 |
|---|---|
| 新增根部门 | `Path = "/" + Id + "/"` |
| 新增子部门 | `Path = parent.Path + Id + "/"` |
| 移动部门（换父级） | 自身新 `Path = newParent.Path + Id + "/"`；**所有子孙** Path 把旧前缀替换为新前缀 |
| 删除部门 | 见第7章删除校验 |

```csharp
// CP6.Core/Services/Sys/DeptService.cs —— 移动部门时重算子树 Path
public async Task MoveAsync(Guid deptId, Guid? newParentId, string? user)
{
    var dept = await _db.Sys_Depts.FindAsync(deptId);
    GuardNotMoveIntoOwnSubtree(dept, newParentId);              // 不能移到自己的子孙下（成环）
    var oldPrefix = dept.Path;
    var newParentPath = newParentId is null ? "/" : (await _db.Sys_Depts.FindAsync(newParentId)).Path;
    var newPrefix = newParentPath + dept.Id + "/";

    var subtree = await _db.Sys_Depts.Where(d => d.Path.StartsWith(oldPrefix)).ToListAsync();
    foreach (var d in subtree)
        d.Path = newPrefix + d.Path.Substring(oldPrefix.Length); // 旧前缀→新前缀，子孙整体平移
    dept.ParentId = newParentId;
    await _db.SaveChangesAsync();
}
```

> **防成环**：移动目标不能是自身或自身子孙（`newParent.Path` 不能以 `dept.Path` 开头），否则部门树成环、Path 失效。

---

## 第4章 部门管理画面

**布局**（线框见配套 xlsx『画面イメージ』）：左侧部门树 + 右侧部门详情表单。

| 区域 | 内容 |
|---|---|
| 左·部门树 | `el-tree`，展示部门层级；支持拖拽调序/移动父级；节点显示 部门名（负责人）|
| 左·工具条 | 新增根部门 / 新增子部门 / 删除 / 刷新 |
| 右·详情表单 | 部门编码、部门名称、上级部门（只读，树选）、部门负责人（用户选择弹出）、排序、启用 |
| 右·按钮 | 保存 / 取消 |

操作种别：**维护**（新增/编辑/删除/移动）。

---

## 第5章 字段明细

### 5.1 部门（Sys_Dept）

| 字段 | 中文名 | 控件 | 必填 | 最大长度 | 说明 |
|---|---|---|---|---|---|
| deptCode | 部门编码 | 文本 | 是 | 50 | 租户内唯一；重复报 E-PUB-001 |
| deptName | 部门名称 | 文本 | 是 | 100 | |
| parentId | 上级部门 | 树选(只读显示) | 否 | — | 根部门为空 |
| leaderId | 部门负责人 | 用户选择弹出 | 否 | — | → Sys_User；OA 部门长路由用 |
| sort | 排序 | 数字 | 否 | — | 同级排序，默认 0 |
| enable | 启用 | 开关 | 否 | — | 默认启用 |

### 5.2 用户组织字段（Sys_User，在用户管理画面维护，本章定义）

| 字段 | 中文名 | 控件 | 说明 |
|---|---|---|---|
| deptId | 所属部门 | 树选 | → Sys_Dept；DataScope 本部门用 |
| managerId | 直属上级 | 用户选择 | → Sys_User；OA 直属上级路由用 |
| email | 邮箱 | 文本 | 通知用 |

---

## 第6章 字段控制矩阵（部门管理·维护）

| 字段 | 新增 | 编辑 | 说明 |
|---|---|---|---|
| 部门编码 | 可用 | **只读** | 编码建后不可改（被引用） |
| 部门名称 | 可用 | 可用 | |
| 上级部门 | 只读(由"新增子部门"上下文决定) | 只读(改父级走拖拽/移动) | |
| 部门负责人 | 可用 | 可用 | |
| 排序 / 启用 | 可用 | 可用 | |

---

## 第7章 处理详细

### 7.1 新增部门
```
新增根部门 → Path="/"+Id+"/"
新增子部门 → Path=parent.Path+Id+"/"；继承 parent.TenantId
校验：DeptCode 租户内唯一（E-PUB-001）
```

### 7.2 编辑部门
```
改名称/负责人/排序/启用；DeptCode 只读
```

### 7.3 删除部门（逻辑/物理按 PUB 统一约定）
```
校验①：有子部门 → 拒绝，E-PUB-002「该部门下存在子部门，不能删除」
校验②：有在职用户（Sys_User.DeptId 指向它）→ 拒绝，E-PUB-003「该部门下存在用户，不能删除」
校验③：被审批流程引用为部门长路由 → 警告（OA 阶段1 后接入）
通过 → 删除
```

### 7.4 移动部门
```
见第3章 MoveAsync：重算自身及子孙 Path；防成环（E-PUB-004）
```

---

## 第8章 审批人解析器 IApproverResolver（OA 消费，落 Wf 命名空间）

```csharp
// CP6.Core/Services/Wf/IApproverResolver.cs
public interface IApproverResolver
{
    // 返回已解析审批人列表；解析不到返回空 + 缺位原因（不抛异常，供上层挂起人工指派）
    Task<ApproverResult> ResolveAsync(ApproverRule rule, ApproverContext ctx);
}

public record ApproverRule(ApproverStrategy Strategy, int? UpLevels, Guid? RoleId, Guid? SpecifiedUserId);
public enum ApproverStrategy { DirectManager, DeptLeader, Role, Specified, Starter }
public record ApproverResult(List<Guid> ApproverIds, string? MissingReason);
```

| 策略 | 解析逻辑 | 缺位兜底 |
|---|---|---|
| **DirectManager** 直属上级 | 取 `发起人.ManagerId`；`UpLevels=N` 时逐级上溯 N 层 | 无上级 → MissingReason="无直属上级" |
| **DeptLeader** 部门负责人 | 取 `发起人.Dept.LeaderId`；本部门无负责人则**逐级上溯**到有负责人的上级部门 | 整链无负责人 → MissingReason |
| **Role** 角色岗位 | 取拥有该 `RoleId` 的全部用户（复用 Sys_UserRole，章01） | 无人 → MissingReason |
| **Specified** 指定人 | 取 `SpecifiedUserId`（或表单字段指定） | 空 → MissingReason |
| **Starter** 发起人本人 | 取 `ctx.StarterId` | — |

> 解析器**纯查询** `CP6Context`，不抛异常；缺位返回原因，由 OA 流程引擎（阶段1）决定挂起人工指派还是按规则跳过。这是 OA 阶段1 路由的底座，本章只提供解析能力。

---

## 第9章 数据权限 DataScope 用法（PUB 消费，章03 详述）

```csharp
// 章03 IDataScopeFilter 中，"本部门及下级"范围的过滤注入：
var myDept = await _db.Sys_Depts.FindAsync(userCtx.DeptId);
query = query.Where(x => x.Dept.Path.StartsWith(myDept.Path));   // 物化路径前缀 = 整棵子树
// "仅本部门" → x.DeptId == userCtx.DeptId
```

> 本章只保证 `Path` 正确维护；DataScope 五范围的查询注入在 [章03 数据权限](./03-data-scope.md)。

---

## 第10章 API 接口设计（.NET8）

路由前缀 `/api/pub/dept`：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/tree` | GET | 部门树（按 TenantId，含负责人名） |
| `` | POST | 新增部门（含 Path 计算） |
| `/{id}` | PUT | 编辑部门（DeptCode 不可改） |
| `/{id}` | DELETE | 删除（三校验，第7.3章） |
| `/{id}/move` | POST | 移动部门（重算子树 Path，防成环） |
| `/{id}/leader` | PUT | 设部门负责人 |

用户组织字段维护并入用户管理 API：`/api/pub/user/{id}/org`（DeptId/ManagerId/Email）。
审批人解析 `IApproverResolver` 为内部服务，不直接暴露 HTTP（由 OA 流程引擎调用）。

---

## 第11章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-001 | Error | 部门编码已存在 | DeptCode 租户内重复 |
| E-PUB-002 | Error | 该部门下存在子部门，不能删除 | 删除有子部门 |
| E-PUB-003 | Error | 该部门下存在用户，不能删除 | 删除有在职用户 |
| E-PUB-004 | Error | 不能移动到自身或其子部门下 | 移动成环 |

---

## 第12章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← PUB 章03 数据权限 | 消费 `Path` 做子树过滤 |
| ← OA 阶段1 流程引擎 | 消费 `IApproverResolver`（直属上级/部门长/角色/指定） |
| ← 现有 Sys_User | 补 DeptId/ManagerId/Email 三字段，不改现有字段 |
| 多租户 | 全表 `TenantId`，部门树按租户隔离 |

> **归属（2026-06-12 复审定稿）**：组织模型归 PUB（章00）先落，OA 消费、不重复建。原 [OA 阶段0 计划](../superpowers/plans/2026-06-10-approval-stage0-org-model.md) 的组织主数据部分在此落地，审批人解析器并入 OA 阶段1。

---

## 自检
- [ ] 为什么用物化路径 Path 而非纯 ParentId？子树查询/移动各付什么代价？
- [ ] 移动部门时 Path 怎么重算？怎么防成环？
- [ ] 审批人解析的 5 种策略各靠哪个字段？缺位为什么返回原因而非抛异常？
- [ ] 删除部门的三道校验是什么？
- [ ] 组织模型为什么归 PUB 而非 OA？两个消费方各用哪个字段？

---

*实现：新建 `CP6.Entity/DomainModels/Sys/Sys_Dept.cs` + `Sys_User` 补三字段 + `CP6.Core/Services/Sys/DeptService.cs` + `CP6.Core/Services/Wf/IApproverResolver.cs` + `cp6.web/src/views/pub/dept`。配套 xlsx 详细设计见同名 `.xlsx`。*
