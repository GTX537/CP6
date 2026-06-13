# PUB 08 · 通用 CRUD 基座 / 代码生成 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-08 通用 CRUD 基座 / 代码生成 |
| 所属模块 | PUB 公共平台 · Part 2 公共模组（压轴） |
| 里程碑 | **M4** |
| 技术栈 | Vue3 + Element Plus / .NET8（泛型基类 + 模板引擎）/ SQL Server |
| 命名空间 | `Pub`（生成器）+ 生成产物落各业务命名空间 |
| 性质 | **新建**（运行时 CRUD 基座 + 设计时代码生成器） |

> **题眼**：前七章把权限（四粒度）、字典、采番、附件、导入导出都做成了公共能力。本章是**收口**：① 运行时——`BaseCrudService<T>` / `BaseCrudController<T>` 泛型基座，通用增删改查里**自动接好**数据权限、字段掩码、采番、字典、日志；② 设计时——**代码生成器**按表/字段元数据一键生成 Entity + Service + Controller + Vue 列表/表单 + DDL + 菜单/权限点。**生成出来的模块，开箱就带 PUB 全套能力**，后续每个业务模块不再从零写 CRUD。

---

## 目录
- 第1章 概述（重复 CRUD → 一键生成）
- 第2章 通用 CRUD 基座（运行时泛型基类）
- 第3章 代码生成器（设计时）
- 第4章 生成元数据（GenTable / GenColumn）
- 第5章 生成产物清单
- 第6章 生成模块默认带的全套能力 ★
- 第7章 代码生成配置画面
- 第8章 处理详细
- 第9章 API 接口设计
- 第10章 消息一览
- 第11章 集成与依赖

---

## 第1章 概述

| 维度 | 现状 | 升级后 |
|---|---|---|
| CRUD 代码 | 每模块从零写增删改查 | `BaseCrudService/Controller` 泛型复用 |
| 接权限/字典/采番 | 每处手工接 | 基座**自动接好**，生成即有 |
| 新模块脚手架 | 手敲 Entity/Service/页面 | **代码生成器**一键产出 |
| 一致性 | 各模块风格不一 | 模板统一，列表/表单/权限一个样 |

**范围**：运行时 CRUD 基座 + 设计时代码生成器 + 生成元数据 + 生成产物（后端/前端/DDL/菜单权限点）。

---

## 第2章 通用 CRUD 基座（运行时泛型基类）

```csharp
// CP6.Core/Services/Pub/BaseCrudService.cs —— 通用增删改查，自动接公共能力
public abstract class BaseCrudService<T> where T : BaseEntity, IDataScoped
{
    protected abstract string ResourceKey { get; }   // 资源键（权限/数据范围用）
    protected abstract string? SeqBizKey  { get; }   // 采番业务键（null=不采番）

    public virtual async Task<PagedResult<T>> QueryAsync(QueryDto q)
    {
        var query = _db.Set<T>().AsQueryable();
        query = _scope.Apply(query, ResourceKey, _ctx);     // ★数据权限注入（章03）
        query = ApplyDynamicFilter(query, q);                // 动态条件
        return await Paginate(query, q.Page, q.Size);
    }

    public virtual async Task<T> CreateAsync(T entity)
    {
        if (SeqBizKey != null) entity.SetNo(await _seq.NextAsync(SeqBizKey));  // ★采番（章05）
        entity.DeptId ??= _ctx.DeptId;                        // 自动赋归属部门（数据权限用）
        _db.Add(entity); await _db.SaveChangesAsync();
        return entity;
    }

    public virtual async Task UpdateAsync(T entity)
    {
        _fieldPerm.StripReadOnly(entity, ResourceKey);        // ★字段权限拒写（章04）
        _db.Update(entity); await _db.SaveChangesAsync();
    }

    public virtual async Task DeleteAsync(Guid id) { /* 软/硬删 + 校验 */ }
}
```

```csharp
// CP6.Core/Pub/BaseCrudController.cs —— 通用 REST，自动贴权限/掩码
public abstract class BaseCrudController<T> : ControllerBase where T : BaseEntity, IDataScoped
{
    protected abstract string Menu { get; }
    protected abstract string Resource { get; }

    [HttpPost("query")]  [RequirePermission("{Menu}","query")] [FieldMask("{Resource}")]
    public Task<PagedResult<T>> Query([FromBody] QueryDto q) => _svc.QueryAsync(q);

    [HttpPost]           [RequirePermission("{Menu}","add")]
    public Task<T> Create([FromBody] T e) => _svc.CreateAsync(e);

    [HttpPut]            [RequirePermission("{Menu}","edit")]
    public Task Update([FromBody] T e) => _svc.UpdateAsync(e);

    [HttpDelete("{id}")] [RequirePermission("{Menu}","del")]
    public Task Delete(Guid id) => _svc.DeleteAsync(id);
}
```

> 基座把"增删改查 + 数据权限 + 字段权限 + 采番 + 部门归属"固化进泛型基类。业务模块继承它、声明 `ResourceKey/Menu/SeqBizKey`，就有了一套带全套能力的 CRUD，只需扩展业务特有逻辑（override）。

---

## 第3章 代码生成器（设计时）

```
生成元数据（表/字段/菜单/操作点）
  → 模板引擎（.NET 模板 + Vue 模板）
  → 产出：Entity / Service(继承基座) / Controller(继承基座) / Vue列表 / Vue表单 / 列配置 / DDL / 菜单&权限点注册脚本
```

- 元数据来源：① 可视化配置；② 从已有数据库表**反向导入**字段，再调整控件/字典/校验。
- 模板引擎：Scriban / Razor / T4 等，模板里嵌占位（实体名/字段/资源键/菜单），渲染成代码。
- 生成策略：首次全量生成；二次生成可**只覆盖框架区、保留自定义区**（如用 `// <custom>` 标记保护块），避免覆盖手改代码。

---

## 第4章 生成元数据（GenTable / GenColumn）

```csharp
// CP6.Entity/DomainModels/Pub/GenTable.cs
[Table("Pub_GenTable")]
public class GenTable : BaseEntity
{
    public string TableName   { get; set; } = "";  // 物理表 Pur_PurchaseOrder
    public string EntityName  { get; set; } = "";  // PurchaseOrder
    public string Namespace   { get; set; } = "";  // Pur
    public string MenuName    { get; set; } = "";  // 采购订单
    public string ResourceKey { get; set; } = "";  // po（权限/数据范围/字段）
    public string? SeqBizKey  { get; set; }         // PO（采番）
}

// CP6.Entity/DomainModels/Pub/GenColumn.cs
[Table("Pub_GenColumn")]
public class GenColumn : BaseEntity
{
    public Guid   TableId    { get; set; }
    public string ColumnName { get; set; } = "";   // DB 列
    public string FieldName  { get; set; } = "";   // DTO 属性
    public string DataType   { get; set; } = "";   // string/int/decimal/datetime
    public string Control    { get; set; } = "";   // input/select/date/number/switch
    public string? DictType  { get; set; }          // 下拉/翻译用字典（章05）
    public bool   Required   { get; set; }
    public bool   ShowInList { get; set; } = true;  // 列表显示
    public bool   ShowInForm { get; set; } = true;  // 表单显示
    public bool   FieldPerm  { get; set; }          // 是否可做字段权限控制（章04 注册）
    public int    Sort       { get; set; }
}
```

---

## 第5章 生成产物清单

| 产物 | 路径 | 说明 |
|---|---|---|
| Entity | `CP6.Entity/DomainModels/{ns}/{Entity}.cs` | 实现 `IDataScoped` |
| Service | `CP6.Core/Services/{ns}/{Entity}Service.cs` | 继承 `BaseCrudService<T>` |
| Controller | `CP6.Api/Controllers/{ns}/{Entity}Controller.cs` | 继承 `BaseCrudController<T>` |
| Vue 列表页 | `cp6.web/src/views/{ns}/{entity}/List.vue` | 查询/分页/导入导出/操作按钮（v-permission） |
| Vue 表单 | `.../{entity}/Form.vue` | 新增/编辑表单（按控件类型渲染） |
| 列配置 | `.../{entity}/columns.ts` | 导入导出列配置（章07） |
| DDL | `sql/{Entity}.sql` | 建表脚本 |
| 菜单/权限点 | 注册脚本 | 写 `Sys_Menu` + `Sys_MenuAction`（操作点） |

---

## 第6章 生成模块默认带的全套能力 ★

这是代码生成的**回报**——生成出来的模块开箱即带前七章所有能力：

| 能力 | 来自 | 生成模块怎么自动有 |
|---|---|---|
| 多角色权限求解 | 章01 | 走统一 `UserPermissionContext` |
| 操作强校验 | 章02 | Controller 自动贴 `[RequirePermission(menu,action)]` + 注册操作点 |
| 数据权限 | 章03 | Service 继承基座，查询自动 `IDataScopeFilter.Apply` |
| 字段权限 | 章04 | 查询贴 `[FieldMask]`、更新走 `StripReadOnly`；可控字段自动注册 |
| 字典翻译 | 章05 | 下拉/列表枚举按 `GenColumn.DictType` 自动翻译 |
| 采番 | 章05 | `SeqBizKey` 非空则 `CreateAsync` 自动采番 |
| 附件 | 章06 | 表单可选挂 `<PubUpload bizType bizId>` |
| 导入导出 | 章07 | 列表页带导入导出，用生成的 `columns.ts` |

> **这就是 PUB 作为"平台底座"的意义**：前七章建好能力，第八章用代码生成把它们**默认装配**到每个新模块。开发一个新业务表 = 配元数据 → 生成 → 微调业务逻辑，权限/字典/采番/附件/导入导出全是现成的。

---

## 第7章 代码生成配置画面

| 区域 | 内容 |
|---|---|
| 表信息 | 表名/实体名/命名空间/菜单名/资源键/采番键（可从已有表导入） |
| 字段配置 | 表格：字段名 / 控件类型 / 字典 / 必填 / 列表显示 / 表单显示 / 字段权限 / 排序 |
| 生成选项 | 勾选生成哪些产物（Entity/Service/Controller/Vue/DDL/菜单权限点） |
| 操作 | 预览生成代码 / 生成（落盘或下载 zip） |

---

## 第8章 处理详细

### 8.1 配置元数据
```
新建 GenTable + 从 DB 表导入 GenColumn（或手工加）→ 调整控件/字典/必填/显示
```

### 8.2 生成
```
读 GenTable/GenColumn → 模板引擎渲染 8 类产物 → 落盘(项目目录)或打包下载
菜单/权限点注册脚本：写 Sys_Menu(页面) + Sys_MenuAction(add/edit/del/query/export/import)
```

### 8.3 二次生成（保护自定义）
```
框架区覆盖，自定义保护块(// <custom>...</custom>)保留，避免覆盖手改
```

---

## 第9章 API 接口设计（.NET8）

前缀 `/api/pub/codegen`：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/tables` | GET/POST/PUT/DELETE | 生成表元数据维护 |
| `/import-db/{tableName}` | POST | 从数据库表反向导入字段 |
| `/{tableId}/columns` | GET/PUT | 字段元数据维护 |
| `/{tableId}/preview` | GET | 预览生成代码 |
| `/{tableId}/generate` | POST | 生成（落盘/下载 zip） |

---

## 第10章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-081 | Error | 实体名/资源键已存在 | 重复 |
| E-PUB-082 | Warning | 二次生成将覆盖框架区，自定义区保留 | 二次生成确认 |

---

## 第11章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 章01-07 | 生成产物默认装配 权限四粒度 + 字典 + 采番 + 附件 + 导入导出 |
| → 后续所有业务模块 | 新表先生成脚手架，再写业务逻辑 |
| → 章09 集成 | 各业务模块（含财务/采购/MES/WMS）通过本基座/生成接入 PUB 能力 |

> **Part 2 收尾**：公共基础（05）+ 附件（06）+ 导入导出（07）+ CRUD 基座/代码生成（08）四章，把"被所有模块复用的公共能力"建齐并打包成可一键装配的脚手架。下一章 [09 集成](./09-integration.md) 讲各业务模块（财务/采购/MES/WMS/OA）怎么挂上 PUB 的权限强校验与公共能力。

---

## 自检
- [ ] 通用 CRUD 基座把哪些公共能力固化进了泛型基类？
- [ ] 代码生成器的元数据来源、产物有哪些？
- [ ] "生成模块开箱带全套能力"具体指哪八项？分别来自哪章？
- [ ] 二次生成怎么不覆盖手改的业务代码？
- [ ] 为什么说本章是 PUB 作为"平台底座"的收口？

---

*实现：新建 `CP6.Core/Services/Pub/BaseCrudService.cs` + `CP6.Core/Pub/BaseCrudController.cs` + `CP6.Entity/DomainModels/Pub/{GenTable,GenColumn}.cs` + 模板引擎(Scriban/Razor) + 代码生成配置 UI；生成产物自动装配章01-07 能力。配套 xlsx 详细设计见同名 `.xlsx`。*
