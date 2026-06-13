# PUB 07 · 通用导入导出（Excel 模板框架）详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-07 通用导入导出 |
| 所属模块 | PUB 公共平台 · Part 2 公共模组 |
| 里程碑 | **M4** |
| 技术栈 | Vue3 + Element Plus / .NET8 + EPPlus（或 NPOI）/ SQL Server |
| 命名空间 | **`Pub`** |
| 性质 | **新建**（统一 Excel 导入导出框架） |

> **题眼**：每个模块各写各的 Excel 导入导出，重复造轮子且质量参差。本章新建**通用框架**：导出按**列配置**（字段→标题/宽/格式/字典翻译）一行生成，受数据权限约束只导能看的行；导入则**逐行校验**（必填/类型/字典/业务），**错误行标红 + 回写错误原因**供下载修正，全通过才事务入库。**一套框架，所有列表页/单据复用。**

---

## 目录
- 第1章 概述（各写各的 → 统一框架）
- 第2章 列配置 ExcelColumn
- 第3章 导出（列配置 / 字典翻译 / 数据权限 / 大数据量）
- 第4章 模板导出
- 第5章 导入（解析 / 逐行校验 / 错误回写 / 入库）
- 第6章 通用框架接口（IExcelService）
- 第7章 前端（导出按钮 / 导入对话框）
- 第8章 字段明细 / 控制矩阵
- 第9章 处理详细
- 第10章 API 接口设计
- 第11章 消息一览
- 第12章 集成与依赖

---

## 第1章 概述

| 维度 | 现状 | 升级后 |
|---|---|---|
| 导出 | 各模块手写 | 列配置驱动，一行 `Export(data, cols)` |
| 字典翻译 | 各自处理 | 导出自动把枚举值→标签（接章05 字典） |
| 导入校验 | 简陋/无 | 逐行校验 + 错误行标红回写 |
| 数据范围 | 易越权导出 | 导出受章03 数据权限约束 |
| 大数据量 | 易 OOM | 分批/流式 |

**范围**：列配置 + 导出 + 模板 + 导入校验 + 错误回写 + 通用框架接口 + 前端组件。

---

## 第2章 列配置 ExcelColumn

```csharp
// CP6.Core/Services/Pub/ExcelColumn.cs —— 一份列配置驱动 导出/模板/导入
public class ExcelColumn
{
    public string  Field    { get; set; } = "";   // DTO 属性名，如 Status
    public string  Title    { get; set; } = "";   // 列标题，如 订单状态
    public int?    Width     { get; set; }          // 列宽
    public string? DictType  { get; set; }          // 字典翻译（章05），如 order_status
    public string? Format    { get; set; }          // 日期/数字格式，如 yyyy-MM-dd
    public bool    Required  { get; set; }          // 导入必填校验
    public bool    Export    { get; set; } = true;  // 是否参与导出
    public bool    Import    { get; set; } = true;  // 是否参与导入
}
```

> **一份列配置，三处复用**：导出按它写表头+取值+翻译；模板按它生成空表头；导入按它做列匹配+必填校验。改一处列定义，导入导出同步生效。

---

## 第3章 导出

```csharp
public byte[] Export<T>(IEnumerable<T> data, List<ExcelColumn> cols)
{
    // 1. 写表头（cols.Title）
    // 2. 逐行取值：反射 Field → 值
    // 3. 字典翻译：col.DictType 非空 → IDictService.Translate(dictType, value)（章05）
    // 4. 格式化：col.Format（日期/数字）
    // 5. 列宽 col.Width
    return excelBytes;
}
```

- **数据来源接数据权限**：导出的 `data` 来自业务 service 的查询，已被 [章03 `IDataScopeFilter`](./03-data-scope.md) 注入过滤——**只导出当前用户能看的行**，不会越权导全量。
- **字段权限**：隐藏字段（章04）在 DTO 已被掩码置空，导出自然不含。
- **大数据量**：分批查询 + 流式写（EPPlus 的流式 / SXSSF 思路），避免一次性加载全表 OOM。

---

## 第4章 模板导出

```csharp
public byte[] Template(List<ExcelColumn> cols)
{
    // 只写表头（Import=true 的列），必填列标题标红/加*，可附字典可选值批注
    // 供用户下载、填写后再导入
}
```

> 导入模板和导入校验用**同一份列配置**，保证用户拿到的模板列就是系统认的列——避免列对不上的导入失败。

---

## 第5章 导入（解析 / 逐行校验 / 错误回写 / 入库）

```csharp
public ImportResult<T> Import<T>(Stream excel, List<ExcelColumn> cols, Func<T, List<string>> bizValidate)
{
    var result = new ImportResult<T>();
    foreach (var (rowIdx, rawRow) in ReadRows(excel))
    {
        var errors = new List<string>();
        var dto = MapRow<T>(rawRow, cols, errors);        // 列匹配 + 类型转换（失败记错误）
        errors.AddRange(ValidateRequired(dto, cols));      // 必填
        errors.AddRange(ValidateDict(dto, cols));          // 字典值有效性（接章05）
        errors.AddRange(bizValidate(dto));                 // 业务校验（唯一性/外键存在…）
        if (errors.Count == 0) result.ValidRows.Add(dto);
        else result.Errors.Add((rowIdx, string.Join("；", errors)));
    }
    if (result.Errors.Count > 0)
        result.ErrorFile = BuildErrorFile(excel, result.Errors);  // 原表 + 错误行标红 + 追加"错误原因"列
    return result;
}
```

```csharp
public class ImportResult<T>
{
    public List<T> ValidRows { get; set; } = new();          // 校验通过的行
    public List<(int Row, string Error)> Errors { get; set; } = new();
    public byte[]? ErrorFile { get; set; }                    // 错误回写的 Excel（供下载修正）
}
```

入库策略（业务可选）：
- **全通过才入库**（默认）：有任一错误 → 不入库，返回错误文件，用户修正后重传。
- **部分导入**：通过的行入库、错误的行返回——按业务配置。
- 入库走**事务**，配合采番（章05）/数据权限自动赋部门。

> **错误回写是导入体验的核心**：不是只报"第5行错了"，而是把原 Excel 的错误行标红、在末尾加"错误原因"列说明每行错在哪，用户下载这个文件照着改再传。批量导入没有错误回写就是灾难。

---

## 第6章 通用框架接口

```csharp
// CP6.Core/Services/Pub/IExcelService.cs
public interface IExcelService
{
    byte[] Export<T>(IEnumerable<T> data, List<ExcelColumn> cols);
    byte[] Template(List<ExcelColumn> cols);
    ImportResult<T> Import<T>(Stream excel, List<ExcelColumn> cols, Func<T, List<string>> bizValidate);
}
```

业务模块用法：
```csharp
var cols = new List<ExcelColumn> {
    new() { Field="OrderNo", Title="订单号", Required=true },
    new() { Field="Status",  Title="状态",  DictType="order_status" },
    new() { Field="Amount",  Title="金额",  Format="#,##0.00" },
};
// 导出（data 已过数据权限）
return File(_excel.Export(orders, cols), XlsxMime, "订单.xlsx");
// 导入
var r = _excel.Import<OrderImportDto>(stream, cols, dto => ValidateOrder(dto));
```

---

## 第7章 前端（导出按钮 / 导入对话框）

```
导出：列表页「导出」按钮 → 用当前查询条件请求 → 下载 xlsx（导当前结果集）
导入：「导入」按钮 → 对话框：
  Step1 下载导入模板
  Step2 上传填好的 Excel（拖拽/选择）
  Step3 校验结果：成功 N 行 / 失败 M 行 + 错误明细表（行号 | 错误原因）
        失败 → [下载错误文件]（标红+原因）修正后重传
        成功 → [确认导入]
```

---

## 第8章 字段明细 / 控制矩阵

| 元素 | 控件 | 说明 |
|---|---|---|
| 导出按钮 | 按钮 | 受章02 操作权限 `export`；导当前查询结果 |
| 导入按钮 | 按钮 | 受章02 操作权限 `import` |
| 模板下载 | 链接 | 列配置生成的空模板 |
| 校验结果 | 表格 | 行号 + 错误原因；可下载错误文件 |

**控制矩阵**：无 `export`/`import` 操作权限 → 对应按钮隐藏/禁用（章02）。

---

## 第9章 处理详细

### 9.1 导出
```
取数据(业务 service，已过数据权限) → Export(data, cols) → 字典翻译/格式化/列宽 → 流式下载
```

### 9.2 导入
```
上传 → 解析行 → 逐行(列匹配+类型+必填+字典+业务校验) → 有错:标红回写错误文件 / 全通过:事务入库
```

### 9.3 大数据量
```
导出分批查询+流式写；导入分块读，避免一次性全载 OOM
```

---

## 第10章 API 接口设计（.NET8）

各业务模块自带导入导出端点，复用 `IExcelService`。约定：

| 端点（业务模块内） | 方法 | 说明 |
|---|---|---|
| `/{biz}/export` | POST | 入参=查询条件，导出当前结果（过数据权限） |
| `/{biz}/import-template` | GET | 下载导入模板 |
| `/{biz}/import` | POST | 上传 Excel，返回 ImportResult（成功数/错误明细/错误文件 token） |
| `/{biz}/import/error-file/{token}` | GET | 下载错误回写文件 |

---

## 第11章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-071 | Error | 导入文件列与模板不匹配 | 列标题对不上 |
| E-PUB-072 | Warning | 导入完成：成功 {n} 行，失败 {m} 行 | 部分失败 |
| E-PUB-073 | Error | 导出数据量超过上限，请缩小范围 | 超导出上限 |

---

## 第12章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 章05 字典 | 导出值→标签翻译、导入字典值校验 |
| ← 章03 数据权限 | 导出 data 已过 `IDataScopeFilter`，只导能看的行 |
| ← 章04 字段权限 | 隐藏字段已掩码，不导出 |
| ← 章02 操作权限 | 导出/导入按钮受 `export`/`import` 操作权限 |
| → 章08 代码生成 | 生成的列表页默认带导入导出（用本框架） |

> 导入导出是列表页标配。统一成框架后，业务模块给一份列配置 + 业务校验函数就有了带权限、带字典翻译、带错误回写的导入导出，不重复造。

---

## 自检
- [ ] 一份 ExcelColumn 列配置怎么同时驱动 导出/模板/导入？
- [ ] 导出为什么不会越权导全量？字典值怎么变成中文标签？
- [ ] 导入校验有哪几层？错误回写为什么重要、怎么做？
- [ ] 大数据量导入导出怎么避免 OOM？
- [ ] 导入入库为什么要事务？全通过 vs 部分导入怎么选？

---

*实现：新建 `CP6.Core/Services/Pub/{IExcelService,ExcelService,ExcelColumn,ImportResult}.cs`（EPPlus/NPOI）+ 前端导出按钮/导入对话框组件；导出接章03 数据权限、字典翻译接章05。配套 xlsx 详细设计见同名 `.xlsx`。*
