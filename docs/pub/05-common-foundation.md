# PUB 05 · 公共基础纳管（字典 / 采番 / 多语言 / 日志）详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-05 公共基础纳管 |
| 所属模块 | PUB 公共平台 · Part 2 公共模组 |
| 里程碑 | **M4**（公共能力统一） |
| 技术栈 | Vue3 + Element Plus / .NET8 + EF Core / SQL Server |
| 命名空间 | `Sys`（沿用现有）+ 可视化配置页落 `cp6.web/src/views/pub` |
| 性质 | **纳管**现有（不重建）：数据字典、单据采番、多语言、操作日志 |

> **题眼**：字典、采番、多语言、日志这些公共能力 CP6 **已有**，但散在 `Sys`/`Common` 各处、缺统一可视化配置。本章把它们**收编到 PUB 公共平台**，补上配置页、缓存、消费接口——让所有业务模块用同一套字典/采番/i18n/日志，而不是各写各的。**纳管 = 收编现有 + 补配置页 + 统一消费，不推倒重来。**

---

## 目录
- 第1章 概述（散在 → 统一纳管）
- 第2章 数据字典（Sys_DictType / Sys_DictData）
- 第3章 单据采番（DocSequence）
- 第4章 多语言（Sys_Lang）
- 第5章 操作日志（Sys_OperLog）
- 第6章 配置画面总览
- 第7章 API 接口设计
- 第8章 消息一览
- 第9章 集成与依赖

---

## 第1章 概述

| 能力 | 现状 | 纳管后 |
|---|---|---|
| 数据字典 | `Sys_DictType/DictData` 已有，缺可视化维护 | 配置页 + 缓存 + 统一下拉/翻译接口 |
| 单据采番 | `DocSequence` 已有（采购等在用 `NextAsync`） | 配置页（规则可视化）+ 并发安全 |
| 多语言 | `Sys_Lang` 已有（登录页已 i18n） | 配置页 + 前端 i18n 统一取数 |
| 操作日志 | `Sys_OperLog` 已有 | 自动记录（特性/中间件）+ 统一查询页 |

**范围**：四类公共能力的统一配置页 + 消费接口 + 缓存。**不含**：附件（章06）、导入导出（章07）、代码生成（章08）。

---

## 第2章 数据字典（Sys_DictType / Sys_DictData）

### 2.1 数据模型（现有，列字段口径）
```csharp
[Table("Sys_DictType")]
public class Sys_DictType : BaseEntity     // 字典类型
{
    public string TypeCode { get; set; } = "";   // 唯一，如 order_status
    public string TypeName { get; set; } = "";
    public bool   Enable   { get; set; } = true;
}
[Table("Sys_DictData")]
public class Sys_DictData : BaseEntity      // 字典项
{
    public string  TypeCode  { get; set; } = "";  // → DictType
    public string  DictValue { get; set; } = "";  // 存值，如 "1"
    public string  DictLabel { get; set; } = "";  // 显示，如 "已确认"
    public int     Sort      { get; set; }
    public bool    Enable    { get; set; } = true;
    public string? CssClass  { get; set; }         // 标签样式（如 success/warning）
}
```

### 2.2 缓存与消费
```csharp
// IDictService：按类型取字典项（缓存），值→标签翻译
public interface IDictService
{
    Task<List<Sys_DictData>> GetItemsAsync(string typeCode);   // 下拉数据源（缓存）
    Task<string?> TranslateAsync(string typeCode, string value); // 值→标签
}
```
- 字典按 `TypeCode` 缓存（内存/Redis），**维护时失效该类型缓存**。
- 消费：前端下拉数据源、列表里枚举值→中文标签翻译、标签着色（CssClass）。

> 字典统一后，"订单状态/单据类型/是否标志"这类枚举不再散落硬编码，改一处全局生效。

---

## 第3章 单据采番（DocSequence）

### 3.1 数据模型
```csharp
[Table("DocSequence")]
public class DocSequence : BaseEntity
{
    public string BizKey     { get; set; } = "";  // 唯一，如 PO/SO/PR
    public string? Prefix    { get; set; }         // 前缀，如 "PO"
    public string? DateFormat{ get; set; }         // yyyyMMdd / yyyyMM / 空
    public int    SeqLength  { get; set; } = 4;    // 流水位数（补零）
    public int    ResetCycle { get; set; }         // 0不重置/1日/2月/3年
    public long   CurrentValue { get; set; }       // 当前流水
    public string? CurrentPeriod { get; set; }     // 上次采番的周期键（判断是否重置）
}
```

### 3.2 采番逻辑（并发安全）
```csharp
// 生成下一个单号：PO + 20260612 + 0001
public async Task<string> NextAsync(string bizKey)
{
    // 原子更新（行锁 / UPDATE ... SET CurrentValue=CurrentValue+1 OUTPUT，避免并发重号）
    var seq = await LockAndLoad(bizKey);
    var period = BuildPeriodKey(seq.ResetCycle);              // 如 "20260612"（日）
    if (seq.CurrentPeriod != period) { seq.CurrentValue = 0; seq.CurrentPeriod = period; } // 跨周期重置
    seq.CurrentValue += 1;
    await _db.SaveChangesAsync();
    return $"{seq.Prefix}{FormatDate(seq.DateFormat)}{seq.CurrentValue.ToString().PadLeft(seq.SeqLength,'0')}";
}
```

> **并发不能重号**：高并发下两个请求同时 `NextAsync` 不能拿到同一号。用数据库行锁或原子自增（`UPDATE ... OUTPUT inserted`）保证。跨周期（日/月/年）自动重置流水。采购/财务/销售各单据共用此采番，规则在配置页可视化维护。

---

## 第4章 多语言（Sys_Lang）

### 4.1 数据模型
```csharp
[Table("Sys_Lang")]
public class Sys_Lang : BaseEntity
{
    public string LangKey  { get; set; } = "";   // 文案键，如 login.title
    public string LangCode { get; set; } = "";   // zh-CN / ja-JP / en-US
    public string Text     { get; set; } = "";
}
```
```sql
CREATE UNIQUE INDEX UX_Sys_Lang ON Sys_Lang(TenantId, LangKey, LangCode);
```

### 4.2 消费（前端 i18n）
- 前端按当前 `LangCode` 拉取文案字典（`{key: text}`），接入 vue-i18n。
- 切换语言 → 重新拉取 + 刷新；登录页/菜单/按钮文案统一走 `LangKey`。
- 维护页改文案后，前端缓存失效重取（CP6 已有"种子后清 Redis 语言缓存"机制可复用）。

> 多语言已在登录页落地；本章把它纳管成统一配置页（按 key × 语言维护），业务模块文案全走 `Sys_Lang`，不再硬编码中文。

---

## 第5章 操作日志（Sys_OperLog）

### 5.1 数据模型
```csharp
[Table("Sys_OperLog")]
public class Sys_OperLog : BaseEntity
{
    public Guid?   UserId   { get; set; }
    public string  Module   { get; set; } = "";   // 模块/功能
    public string  Action   { get; set; } = "";   // 操作（新增/删除/审批…）
    public string? Method    { get; set; }          // 后端方法/HTTP
    public string? Url       { get; set; }
    public string? Params    { get; set; }          // 入参（脱敏）
    public string? Result    { get; set; }          // 成功/失败 + 摘要
    public string? Ip        { get; set; }
    public int     Duration  { get; set; }          // 耗时 ms
}
```

### 5.2 自动记录
```csharp
// [OperLog("订单","导出")] 贴在 Action 上，由过滤器/中间件自动写日志
[AttributeUsage(AttributeTargets.Method)]
public class OperLogAttribute : Attribute, IAsyncActionFilter { /* 记录 模块/操作/入参/结果/耗时/IP */ }
```
- 自动记录：方法贴 `[OperLog(module, action)]`，过滤器统一落 `Sys_OperLog`（入参脱敏、记耗时与结果）。
- 统一查询页：按 用户/模块/操作/时间 检索；高频写入可异步落库（不阻塞业务）。

> 日志纳管 = 统一"怎么记（特性自动）+ 怎么查（统一页）"，业务模块不各写各的日志代码。

---

## 第6章 配置画面总览

PUB 公共平台 → 公共基础（4 个配置页）：

| 页面 | 内容 |
|---|---|
| 数据字典 | 左字典类型列表 + 右字典项表格（增删改、排序、启用、CssClass） |
| 单据采番 | 采番规则列表（业务键/前缀/日期格式/流水位数/重置周期/当前值），可视化编辑 + 预览号 |
| 多语言 | 文案表（key × 语言矩阵），按 key 维护各语言文案 |
| 操作日志 | 查询页（用户/模块/操作/时间过滤 + 明细） |

---

## 第7章 API 接口设计（.NET8）

| 端点 | 方法 | 说明 |
|---|---|---|
| `/api/pub/dict/types` | GET/POST/PUT/DELETE | 字典类型 CRUD |
| `/api/pub/dict/{typeCode}/items` | GET/PUT | 字典项维护（保存失效缓存） |
| `/api/pub/dict/{typeCode}` | GET | 消费：取字典项（缓存，前端下拉用） |
| `/api/pub/seq` | GET/PUT | 采番规则维护 + 预览 |
| `/api/pub/lang` | GET/PUT | 多语言维护 |
| `/api/pub/lang/{langCode}` | GET | 消费：取某语言全部文案（前端 i18n） |
| `/api/pub/oper-log` | GET | 操作日志查询 |

`IDictService` / `DocSequence.NextAsync` / `IOperLogger` 为内部消费服务，业务模块直接注入。

---

## 第8章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-051 | Error | 字典类型编码已存在 | TypeCode 重复 |
| E-PUB-052 | Error | 采番业务键已存在 | BizKey 重复 |
| E-PUB-053 | Error | 多语言键+语言已存在 | (LangKey,LangCode) 重复 |

---

## 第9章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 现有 Sys_DictType/DictData、DocSequence、Sys_Lang、Sys_OperLog | 纳管复用，不重建 |
| → 全业务模块 | 字典翻译/下拉、单据采番、i18n 文案、操作日志 统一从 PUB 取 |
| ← PUB 多租户 | 全部带 TenantId，按租户隔离字典/采番/语言/日志 |
| → 章08 代码生成 | 生成的 CRUD 默认接入字典翻译/采番/日志 |

> 公共基础是"被所有模块复用的地基能力"。纳管后，业务模块写代码时字典、采番、i18n、日志都有现成统一接口，不重复造。

---

## 自检
- [ ] "纳管"是什么意思？为什么不重建？
- [ ] 字典为什么要缓存？维护后怎么保证生效？
- [ ] 单据采番并发下怎么防重号？跨周期怎么重置？
- [ ] 多语言文案怎么被前端 i18n 消费？改文案怎么生效？
- [ ] 操作日志怎么做到"业务不各写各的"？

---

*实现：复用 `Sys_DictType/DictData`、`DocSequence`、`Sys_Lang`、`Sys_OperLog`；新建 `IDictService`/`IOperLogger`/`OperLogAttribute` + 4 个配置页（`cp6.web/src/views/pub`）。配套 xlsx 详细设计见同名 `.xlsx`。*
