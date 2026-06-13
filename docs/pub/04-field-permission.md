# PUB 04 · 字段级权限 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-04 字段级权限 |
| 所属模块 | PUB 公共平台 · Part 1 权限引擎（收尾·四粒度齐） |
| 里程碑 | **M3**（三权第三权：你能看哪些列） |
| 技术栈 | Vue3 + Element Plus / .NET8 Web API（序列化掩码 ResultFilter）/ EF Core |
| 命名空间 | `Sys` |
| 前置 | [章01 多角色](./01-rbac-multirole.md)（FieldPerms 最宽合并） |

> **题眼**：**前端不显示成本价 ≠ 安全——抓包/直调 API 照样拿到。** 字段级权限管"你能看哪些**列**"：返回 DTO 序列化时，后端**把隐藏字段置空/脱敏**（如成本、利润、客户手机号），只读字段下发标记并在保存时**拒绝写入**。这是三权的最后一权，把"列"也下沉到后端。写完本章，PUB 权限引擎四粒度（页面/操作/数据行/字段）齐。

---

## 目录
- 第1章 概述（挡行 vs 挡列）
- 第2章 三种访问级 Access
- 第3章 数据模型（Sys_RoleFieldPerm）
- 第4章 序列化掩码（隐藏字段后端置空）★
- 第5章 只读字段（前端禁用 + 后端拒写）
- 第6章 多角色取最宽（FieldPerms 聚合）
- 第7章 字段注册（可控字段声明）
- 第8章 字段权限配置画面
- 第9章 字段明细 / 控制矩阵
- 第10章 处理详细
- 第11章 API 接口设计
- 第12章 消息一览
- 第13章 集成与依赖（三权合一）

---

## 第1章 概述

| 维度 | 数据权限（章03） | 字段权限（本章） |
|---|---|---|
| 控制 | 哪些**行** | 哪些**列** |
| 实现 | 查询层注入 Where | 序列化层掩码 + 反序列化拒写 |
| 典型场景 | 只看本部门订单 | 看得到订单但看不到成本/利润 |
| 绕过 | 查询源头过滤 | 后端置空，抓包也是空 |

**范围**：三访问级 + 角色字段权限配置 + 序列化掩码 + 只读拒写 + 多角色最宽。

---

## 第2章 三种访问级 Access

| Access | 含义 | 前端 | 后端 |
|---|---|---|---|
| 1 | 可读可写 | 正常显示可编辑 | 正常返回、正常接受写入 |
| 2 | 只读 | 显示但禁用编辑 | 返回值；**保存时忽略该字段写入** |
| 3 | 隐藏 | 不显示 | **序列化时置空/脱敏** |

> **数值越小越可见**（1可读 > 2只读 > 3隐藏），多角色合并取 `MIN`（第6章）。未配置的字段默认 **可读(1)**——字段权限是"按需收紧"，不配就是正常可见。

---

## 第3章 数据模型

```csharp
// CP6.Entity/DomainModels/Sys/Sys_RoleFieldPerm.cs（新建）
[Table("Sys_RoleFieldPerm")]
public class Sys_RoleFieldPerm : BaseEntity
{
    public Guid   RoleId      { get; set; }            // → Sys_Role.Id
    public string ResourceKey { get; set; } = "";      // 业务资源，如 order
    public string FieldName   { get; set; } = "";      // 字段名（DTO 属性名），如 Cost / Price
    public int    Access       { get; set; }           // 1可读 / 2只读 / 3隐藏
}
```
```sql
CREATE UNIQUE INDEX UX_Sys_RoleFieldPerm ON Sys_RoleFieldPerm(TenantId, RoleId, ResourceKey, FieldName);
CREATE INDEX IX_Sys_RoleFieldPerm_Role ON Sys_RoleFieldPerm(TenantId, RoleId);
```

> 一个角色对一个资源的一个字段一条（UX 唯一）。只存"被收紧的字段"，可读(1)的不必落库（默认即可读）。

---

## 第4章 序列化掩码（隐藏字段后端置空）★

核心安全点：返回前把 Access=隐藏 的字段**在后端置空**，不依赖前端不显示。

```csharp
// CP6.Core/Auth/FieldMaskAttribute.cs —— 输出结果过滤器，按字段权限掩码
[AttributeUsage(AttributeTargets.Method)]
public class FieldMaskAttribute : Attribute, IAsyncResultFilter
{
    private readonly string _resource;
    public FieldMaskAttribute(string resource) { _resource = resource; }

    public async Task OnResultExecutionAsync(ResultExecutingContext ctx, ResultExecutionDelegate next)
    {
        if (ctx.Result is ObjectResult { Value: { } value })
        {
            var fp = ctx.HttpContext.RequestServices.GetRequiredService<IFieldPermService>();
            fp.MaskHidden(value, _resource);          // 反射把隐藏字段置 null（含集合每项）
        }
        await next();
    }
}
```
```csharp
[HttpGet("{id}")]
[FieldMask("order")]                                  // 返回前按 order 的字段权限掩码
public async Task<OrderDto> Get(Guid id) { ... }
```

```csharp
// IFieldPermService.MaskHidden：反射遍历 DTO（及集合元素），Access==3 的属性置默认值/脱敏
public void MaskHidden(object dto, string resourceKey)
{
    var perms = _current.GetContext().FieldPerms.GetValueOrDefault(resourceKey);
    if (perms is null) return;
    foreach (var item in AsEnumerable(dto))                       // 单对象或集合统一处理
        foreach (var (field, access) in perms)
            if (access == 3) SetNullOrMask(item, field);          // 置空，或按脱敏规则打码
}
```

> **掩码在序列化前**：DTO 出 Controller 前就把隐藏字段清掉，抓包拿到的就是 `null`。脱敏可选（手机号 `138****0000` 而非全空），按字段配规则。

---

## 第5章 只读字段（前端禁用 + 后端拒写）

只读（Access=2）两端都要管：
- **前端**：下发只读字段列表，表单对应控件 `disabled`（显示但不可编辑）。
- **后端**：保存时**忽略只读字段的写入**——反序列化后从更新集合剔除，防止抓包改值。

```csharp
// 保存时，剔除只读字段，不让其参与更新
public void StripReadOnly(object updateDto, string resourceKey)
{
    foreach (var (field, access) in _current.GetContext().FieldPerms.GetValueOrDefault(resourceKey) ?? new())
        if (access == 2) RevertToOriginal(updateDto, field);     // 用 DB 原值覆盖，等于忽略改动
}
```

> **只读不是前端 disabled 就够**：前端禁用只防误操作，抓包仍能提交。后端必须在保存路径把只读字段的入参丢弃（或用原值覆盖），才是真只读。

---

## 第6章 多角色取最宽（FieldPerms 聚合）

章01 `PermissionAggregator` 填充 `ctx.FieldPerms`（每字段取**最宽**=Access 最小）：

```csharp
ctx.FieldPerms = (await _db.Sys_RoleFieldPerms.Where(fp => roleIds.Contains(fp.RoleId)).ToListAsync())
    .GroupBy(fp => fp.ResourceKey)
    .ToDictionary(g => g.Key,
        g => g.GroupBy(x => x.FieldName)
              .ToDictionary(fg => fg.Key, fg => fg.Min(x => x.Access)));   // 1可读 < 2只读 < 3隐藏 → MIN 最可见
```

`FieldPerms` 类型：`Dictionary<resourceKey, Dictionary<fieldName, accessInt>>`。

> 例：角色A 对 `order.cost` 配"隐藏(3)"、角色B 配"可读(1)" → 合并 = 可读(1)，该用户看得到成本。能力叠加取最宽（章01 口径）。

---

## 第7章 字段注册（可控字段声明）

声明每个资源**哪些字段**可做字段权限控制（不是所有字段都需控）：

```csharp
FieldRegistry.Register("order", new[] { "Cost", "Price", "Profit", "CustomerPhone" });
```

- 配置画面据此列出可配字段（如订单只对成本/价格/利润/客户电话开放字段权限）。
- 未注册字段不参与字段权限（永远可读），避免把全部列都暴露成可配项。

---

## 第8章 字段权限配置画面

角色管理 → 字段权限 Tab：

| 区域 | 内容 |
|---|---|
| 角色（上下文） | 当前配置角色 |
| 资源选择 | 选业务资源（订单/客户…） |
| 字段列表 | 来自字段注册表的可控字段 |
| 访问级 | 每字段选 可读 / 只读 / 隐藏（单选） |
| 按钮 | 保存 → 失效该角色下用户缓存 |

---

## 第9章 字段明细 / 控制矩阵

| 字段 | 控件 | 说明 |
|---|---|---|
| resourceKey | 下拉 | 选资源 |
| fieldName | 只读(字段名) | 来自字段注册表 |
| access | 单选(可读/只读/隐藏) | 默认可读 |

**控制矩阵**：只列已注册的可控字段；未注册字段不出现（恒可读）。

---

## 第10章 处理详细

### 10.1 配置字段权限（保存）
```
每字段 upsert Sys_RoleFieldPerm(RoleId, ResourceKey, FieldName, Access)
Access=可读(1) 可不落库（删除已有记录，回归默认可读）
校验：FieldName ∈ 字段注册表（E-PUB-041）
完成 → 失效该角色下用户缓存
```

### 10.2 返回掩码（运行期·出）
```
Action 贴 [FieldMask(resource)] → 返回前 MaskHidden：隐藏字段置空/脱敏
```

### 10.3 保存拒写（运行期·入）
```
保存路径 StripReadOnly：只读字段用原值覆盖，忽略入参改动
```

---

## 第11章 API 接口设计（.NET8）

前缀 `/api/pub/field-perm`：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/fields/{resourceKey}` | GET | 资源的可控字段（字段注册表） |
| `/{roleId}` | GET | 取角色各资源字段的访问级 |
| `/{roleId}` | PUT | 保存（upsert + 失效缓存） |
| `/my-readonly/{resourceKey}` | GET | 当前用户该资源的只读字段（前端禁用用） |

掩码/拒写由 `IFieldPermService` + `[FieldMask]`/`StripReadOnly` 内部完成，不单独暴露。

---

## 第12章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-041 | Error | 字段未注册为可控字段 | FieldName ∉ 字段注册表 |

---

## 第13章 集成与依赖（三权合一）

| 关系 | 说明 |
|---|---|
| ← 章01 多角色 | `FieldPerms` 取最宽，存 UserPermissionContext |
| → 各业务 Controller | 查询 Action 贴 `[FieldMask(resource)]`；保存路径调 `StripReadOnly` |
| ← 章02/03 | 功能权限挡操作、数据权限挡行、本章挡列，三层叠加 |

**三权合一（权限引擎全景）**：
```
用户登录 → 取全部角色（章01）
  → 功能权限 ActionKeys（操作并集，章02）  —— 你能点什么
  → 数据权限 DataScopes（范围最宽，章03）  —— 你能看哪些行
  → 字段权限 FieldPerms（字段最宽，章04）  —— 你能看哪些列
  → 全缓存进 UserPermissionContext，后端三处校验：
      操作=特性403 / 数据=查询注入 / 字段=序列化掩码
前端隐藏只是体验，后端三权强校验才是安全。
```

> 至此 PUB 权限引擎四粒度齐：页面（Sys_RoleMenu）+ 操作（章02）+ 数据行（章03）+ 字段（章04），多角色按"操作并集、数据最宽、字段最宽"合并。CP6 从"前端藏菜单"升级为"后端三权强校验"。

---

## 自检
- [ ] 字段权限和数据权限的区别（列 vs 行）？
- [ ] 为什么前端不显示成本不算安全？隐藏字段在后端哪一步置空？
- [ ] 只读字段为什么前端 disabled 不够？后端怎么拒写？
- [ ] 多角色字段权限怎么合并？为什么取最宽（Access 最小）？
- [ ] 三权合一：功能/数据/字段权限分别在后端哪三个点强校验？

---

*实现：新建 `CP6.Entity/DomainModels/Sys/Sys_RoleFieldPerm.cs` + `CP6.Core/Auth/FieldMaskAttribute.cs` + `CP6.Core/Services/Sys/{FieldPermService,FieldRegistry}.cs`；章01 聚合填充 `FieldPerms`；业务 Controller 贴 `[FieldMask]`、保存调 `StripReadOnly`。配套 xlsx 详细设计见同名 `.xlsx`。本章完成 → 权限引擎四粒度齐。*
