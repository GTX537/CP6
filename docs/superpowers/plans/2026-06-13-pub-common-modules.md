# PUB 公共模组（章05~09）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**PUB 第三份计划**，依赖 B0（组织模型）+ B1（权限引擎，本计划的附件鉴权/CRUD 基座要用 `IDataScopeFilter`/`[RequirePermission]`/`[FieldMask]`/`ICurrentPermissionContext`）。

**Goal:** 落地 PUB Part 2 公共模组 + Part 3 集成——章05 公共基础纳管（补缺口）、章06 附件统一管理（新建）、章07 通用 Excel 导入导出（新建）、章08 通用 CRUD 基座 / 代码生成（新建）、章09 登录聚合管线集成 + 模块接入框架。完成后业务模块写代码时附件/导入导出/字典/采番/CRUD 脚手架全是现成统一接口，新模块"配元数据→生成→微调"即带 PUB 全套能力。

**Architecture:** 新公共基建落 `Pub` 命名空间（`DomainModels/Pub`、`Services/Pub`），纳管的现有能力沿用 `Sys`。附件 = `Pub_Attachment(BizType+BizId)` + 可切换 `IFileStore`（本地/OSS/MinIO）+ MD5 秒传引用计数 + 下载鉴权（回查业务数据权限）。导入导出 = 一份 `ExcelColumn` 列配置驱动导出/模板/导入 + 错误行标红回写。CRUD 基座 = `BaseCrudService<T>`/`BaseCrudController<T>` 把数据权限/字段掩码/采番/部门归属固化进泛型基类；代码生成器按 `GenTable/GenColumn` 元数据 + 模板引擎一键产出 8 类产物。集成 = 把 B1 的 `PermissionAggregator.BuildAsync` 挂进登录管线。

**Tech Stack:** .NET 8 + EF Core 8 + **EPPlus（新增，导入导出）** + **Scriban（新增，代码生成模板）** + SQL Server / xUnit + EF Core InMemory / Vue 3.5 + element-plus。源文档：`docs/pub/05~09`。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 文档（章05~09）原意 | CP6 现状（已勘察） | **本稿建议值** |
|---|---|---|---|---|
| **C-D1** | **章05 多少要做** | 字典/采番/多语言/日志"纳管 + 补配置页" | **大部分已实现**：`OperLogFilter`(自动记录)+`OperLogController`+Kafka异步+清理服务全套；`DictController`/`LangController` 已存在；`Sys_DictData`(int Id,TypeCode/Value/Label/OrderNo/Enable,无 CssClass)、`Sys_OperLog`(UserName/HttpMethod/...,无 UserId/Method) 已建 | **章05 大幅收缩**：操作日志=**已完成不动**（已有 Filter+Controller+异步，spec 的 `[OperLog]` 特性已由 `OperLogFilter` 等价实现）；字典/多语言=**已有 Controller**，仅补 **`IDictService`（缓存+值→标签翻译）** 供导出/列表消费（若缺）；**采番=唯一实质缺口**——现 `DocNumber`(静态,FuncCode+yyyyMM+seq4) 无富配置，spec 要 `DocSequence` 配置实体(Prefix/DateFormat/ResetCycle)。**建议本计划只补 IDictService + 采番配置可视化**，其余章05 标"已实现"。⚠️ 实体字段名以现状为准（Value/Label 非 DictValue/DictLabel） |
| **C-D2** | **采番升级范围** | 章05 富 DocSequence(BizKey/Prefix/DateFormat/ResetCycle/CurrentPeriod) | 现 `DocNumber.NextAsync` 13 字符固定格式，多模块在用 | **新增 `Pub_DocSequence` 配置实体 + `ISeqService.NextAsync(bizKey)`**（富规则），**保留 `DocNumber` 不动**（既有调用方不破），新模块用新 `ISeqService`。⚠️ 不强行迁移既有 DocNumber 调用方 |
| **C-D3** | **Excel 库** | EPPlus 或 NPOI | 均未引用；EPPlus 8+ 商用需 License | **用 EPPlus**（API 友好）；注意 **EPPlus License**（非商业 `NonCommercial` 或购买；或改 **ClosedXML/NPOI** 免费）——⚠️ 商用化请你确认 License 取向（建议 NPOI 免费 或 ClosedXML） |
| **C-D4** | **代码生成模板引擎** | Scriban/Razor/T4 | 无 | **Scriban**（轻、独立、无 Razor 运行时依赖） |
| **C-D5** | **TenantId / 审计** | 全表 TenantId + CreateTime | 零多租户；新 Pub 表继承 BaseEntity(Creator/CreateDate) | 同 B0/B1：本阶段不引入 TenantId（章节内唯一索引去 TenantId 前缀）；Pub 新表继承 `BaseEntity`（GUID Id + 真实审计字段） |
| **C-D6** | **IFileStore 默认实现** | 本地/OSS/MinIO 可切换 | 无 | v1 实现 **LocalFileStore**（落本地盘）+ 接口预留 OSS/MinIO（按 `Storage:Provider` 配置注入），云实现不在本计划 |

> **测试基建**：xUnit + InMemory。附件秒传/引用计数、Excel 导入校验、CRUD 基座逻辑可单测；EPPlus 真实读写用临时文件测；代码生成器渲染产物做快照测。

---

## File Structure

### 章05 缺口补全（`CP6.Core/Services/Sys` + `Pub`）
- `IDictService.cs`/`DictService.cs`（缓存 + `TranslateAsync`，若现 DictController 未含则补）
- `CP6.Entity/DomainModels/Pub/Pub_DocSequence.cs` + `CP6.Core/Services/Pub/ISeqService.cs`/`SeqService.cs`（富采番）+ 采番配置 UI

### 章06 附件（`Pub` 新建）
- `CP6.Entity/DomainModels/Pub/Pub_Attachment.cs`
- `CP6.Core/Services/Pub/IFileStore.cs` + `LocalFileStore.cs`；`IAttachmentService.cs`/`AttachmentService.cs`（上传秒传/下载鉴权/删除引用计数）
- `CP6.WebApi/Controllers/Pub/AttachmentController.cs`；前端 `cp6.web/src/components/PubUpload.vue` + `src/api/pub/attachment.ts`

### 章07 导入导出（`Pub` 新建）
- `CP6.Core/Services/Pub/{ExcelColumn,ImportResult,IExcelService,ExcelService}.cs`（EPPlus/NPOI）
- 前端 `src/components/PubImportDialog.vue` + 导出按钮组合式

### 章08 CRUD 基座 / 代码生成（`Pub` 新建）
- `CP6.Core/Services/Pub/BaseCrudService.cs` + `CP6.Core/Pub/BaseCrudController.cs`
- `CP6.Entity/DomainModels/Pub/{GenTable,GenColumn}.cs` + `CP6.Core/Services/Pub/CodeGenService.cs`（Scriban 模板）+ 模板 `templates/*.sbn`
- `CP6.WebApi/Controllers/Pub/CodeGenController.cs` + 代码生成配置 UI

### 章09 集成（glue）
- 修改 `AuthController`（登录成功后 `PermissionAggregator.BuildAsync` + 缓存，或交给 `ICurrentPermissionContext` 懒构建）
- `DataScopeRegistry`/`FieldRegistry` 注册入口（B1 已建，本计划补"接入清单"文档化 + 一个示范模块接入）

### 测试
- `SeqServiceTests`、`AttachmentServiceTests`（秒传/引用计数）、`ExcelServiceTests`（导出/导入校验/错误回写）、`BaseCrudServiceTests`、`CodeGenServiceTests`（产物快照）

---

## 实施分五阶段（对应章05~09）

- **Phase A**（A-1..A-2）：章05 缺口——IDictService 缓存翻译 + 富采番（其余章05 已实现，仅确认）
- **Phase B**（B-1..B-4）：章06 附件统一管理（新建）
- **Phase C**（C-1..C-3）：章07 通用 Excel 导入导出（新建）
- **Phase D**（D-1..D-4）：章08 CRUD 基座 + 代码生成（新建·压轴）
- **Phase E**（E-1..E-2）：章09 登录聚合集成 + 示范模块接入 + 接入清单

---

# Phase A — 章05 公共基础缺口补全

> **先确认已实现**（C-D1，不做或仅微调）：操作日志（`OperLogFilter`+`OperLogController`+Kafka 异步+清理）已全套；字典/多语言已有 `DictController`/`LangController`。本阶段只补两处缺口。

## Task A-1: IDictService 缓存 + 值→标签翻译（章05 §2.2）

**Files:** Create/确认 `CP6.Core/Services/Sys/IDictService.cs`/`DictService.cs`; Test `DictServiceTests.cs`

- [ ] **Step 1: 先确认** Run: `grep -rn "IDictService\|TranslateAsync" CP6.Core` —— 若已存在则本任务跳过（仅在 Plan 中标注），否则继续。
- [ ] **Step 2: 失败测试**（GetItemsAsync 按 TypeCode 取项并缓存；TranslateAsync 值→Label；维护后失效缓存）`[InMemory + IMemoryCache]`

```csharp
[Fact]
public async Task Translate_ReturnsLabel_AndCaches()
{
    using var db = Db();
    db.Sys_DictDatas.Add(new Sys_DictData { TypeCode="order_status", Value="1", Label="已确认", Enable=true });
    await db.SaveChangesAsync();
    var svc = new DictService(db, new MemoryCache(new MemoryCacheOptions()));
    Assert.Equal("已确认", await svc.TranslateAsync("order_status", "1"));
}
```

- [ ] **Step 3: 实现**（按 TypeCode 缓存 `List<Sys_DictData>`；TranslateAsync 命中 Value→Label；`InvalidateType(typeCode)` 维护时调用——注意现状字段是 `Value/Label`，非 spec 的 DictValue/DictLabel）
- [ ] **Step 4: 跑绿 → Step 5: DI + 提交** → `git commit -m "feat(pub): IDictService cache + value-to-label translate (ch05 §2.2)"`

## Task A-2: 富采番 Pub_DocSequence + ISeqService（章05 §3，C-D2）

**Files:** Create `Pub_DocSequence.cs`, `ISeqService.cs`/`SeqService.cs`; Modify `CP6Context.cs`; migration; 采番配置 UI; Test `SeqServiceTests.cs`

- [ ] **Step 1: 失败测试**（NextAsync 拼 Prefix+日期+流水补零；跨周期重置流水；并发不重号）

```csharp
[Fact]
public async Task Next_BuildsNumber_AndResetsAcrossPeriod()
{
    using var db = Db();
    db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey="PO", Prefix="PO", DateFormat="yyyyMMdd", SeqLength=4, ResetCycle=1, CurrentValue=0 });
    await db.SaveChangesAsync();
    var svc = new SeqService(db);
    var n1 = await svc.NextAsync("PO");          // PO20260613 0001
    Assert.Matches(@"^PO\d{8}0001$", n1);
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（实体 + NextAsync：BuildPeriodKey 判跨周期重置、原子自增防重号[行锁/`UPDATE...OUTPUT`]、格式化；保留既有 `DocNumber` 不动）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 采番配置 UI（规则列表 + 预览号）+ DI + 提交** → `git commit -m "feat(pub): Pub_DocSequence + ISeqService rich numbering (ch05 §3)"`

---

# Phase B — 章06 附件统一管理（新建）

## Task B-1: Pub_Attachment + IFileStore（章06 §2/§3）

**Files:** Create `Pub_Attachment.cs`, `IFileStore.cs`/`LocalFileStore.cs`; Modify `CP6Context.cs`; migration; Test `FileStoreTests.cs`

- [ ] **Step 1: 失败测试**（LocalFileStore Save→Read 往返；Delete 后 Read 抛）
- [ ] **Step 2: 跑红 → Step 3: 实现实体（BizType/BizId/FileName/StoreName/StorePath/Size/ContentType/FileHash/Uploader，继承 BaseEntity）+ IFileStore + LocalFileStore（落配置根目录 `Storage:LocalRoot`）+ 索引 `IX_Pub_Attachment_Biz(BizType,BizId)`/`IX..._Hash(FileHash)`**
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + DI（`IFileStore`→LocalFileStore，按 `Storage:Provider` 切换）+ 提交** → `git commit -m "feat(pub): Pub_Attachment + IFileStore/LocalFileStore (ch06 §2/§3)"`

## Task B-2: AttachmentService — 上传秒传 + 删除引用计数（章06 §4/§9）★

**Files:** Create `IAttachmentService.cs`/`AttachmentService.cs`; Test `AttachmentServiceTests.cs`

- [ ] **Step 1: 失败测试**（上传校验大小 E-061/类型 E-062；同 MD5 秒传复用 StorePath 不重存；删除仅当 StorePath 无其他引用才物理删）

```csharp
[Fact]
public async Task Upload_SameHash_ReusesStorePath()
{
    // 上传两个内容相同文件 → 两条 Pub_Attachment 指向同一 StorePath；IFileStore.SaveAsync 只调一次
}
[Fact]
public async Task Delete_KeepsPhysical_WhenOtherRefsExist()
{
    // 两条引用同一 StorePath，删一条 → 物理文件保留；删第二条 → IFileStore.DeleteAsync 被调用
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（UploadAsync：GuardSize/GuardType[配置白名单]→Md5→查 FileHash 命中复用 StorePath 否则 `IFileStore.SaveAsync`→建记录；DeleteAsync：删记录→`count(StorePath 引用)==0` 才 `IFileStore.DeleteAsync`；DownloadAsync 返回 StorePath+FileName 供控制器鉴权后流式）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): attachment upload(md5 dedup) + delete(refcount) (ch06 §4/§9)"`

## Task B-3: AttachmentController — 上传/列表/下载鉴权/删除（章06 §5/§10）

**Files:** Create `AttachmentController.cs`

- [ ] **Step 1: 实现**——`/upload`(multipart)、`/list?bizType=&bizId=`、`/{id}/download`（**鉴权：回查业务单据数据权限**——v1 至少校验 `[Authorize]` + 按 bizType 的 `[RequirePermission(bizType,"query")]` 或回查 IDataScopeFilter，E-063 无权）、`/{id}/preview`、`/{id}` DELETE。流式下载带 `Content-Disposition` 原文件名。
- [ ] **Step 2: 集成测（WebApplicationFactory）**：上传→列表→下载→删除冒烟 + 无权 403。
- [ ] **Step 3: DI + 提交** → `git commit -m "feat(pub): attachment controller with download authz (ch06 §5/§10)"`

## Task B-4: 前端 PubUpload 组件 + 草稿转正（章06 §6/§7）

**Files:** Create `cp6.web/src/components/PubUpload.vue`, `src/api/pub/attachment.ts`

- [ ] **Step 1: 实现**——`<PubUpload bizType bizId :maxSize :accept :multiple>`：拖拽/点击上传 + 进度 + 列表(文件名/大小/上传人/时间 + 下载/预览/删除)；图片缩略图；新建单据 bizId 未定 → 临时 token 暂存，单据保存后回填 bizId（草稿转正，章06 §9.4——后端 `AttachmentService` 加 `RebindAsync(token, bizId)`）。
- [ ] **Step 2: 冒烟 + 提交** → `git commit -m "feat(pub): PubUpload component + draft rebind (ch06 §6/§7)"`

---

# Phase C — 章07 通用 Excel 导入导出（新建）

## Task C-1: ExcelColumn + IExcelService 导出/模板（章07 §2/§3/§4）

**Files:** Modify `CP6.Core/CP6.Core.csproj`(EPPlus/NPOI); Create `ExcelColumn.cs`, `ImportResult.cs`, `IExcelService.cs`/`ExcelService.cs`; Test `ExcelServiceTests.cs`

- [ ] **Step 1: 装 Excel 库**（C-D3）Run: `dotnet add CP6.Core package EPPlus`（或 NPOI/ClosedXML —— 按你 License 决定）；EPPlus 需设 `ExcelPackage.LicenseContext`。
- [ ] **Step 2: 失败测试**（Export 写表头+反射取值+字典翻译[DictType→IDictService]+格式化；Template 只写 Import=true 列、必填标红）

```csharp
[Fact]
public void Export_WritesHeaderAndTranslatesDict()
{
    var cols = new List<ExcelColumn> { new(){Field="Status",Title="状态",DictType="order_status"} };
    var bytes = new ExcelService(stubDict).Export(new[]{ new{Status="1"} }, cols);
    // 读回 bytes，断言表头"状态"、单元格"已确认"（字典翻译）
}
```

- [ ] **Step 3: 跑红 → Step 4: 实现 Export/Template**（反射 Field 取值；col.DictType 非空调 `IDictService.TranslateAsync`；col.Format 格式化；列宽；模板必填标题加*/标红 + 字典可选值批注）
- [ ] **Step 5: 跑绿 + 提交** → `git commit -m "feat(pub): IExcelService export + template (column-config driven) (ch07 §2/§3/§4)"`

## Task C-2: 导入 — 逐行校验 + 错误回写（章07 §5）★

**Files:** Modify `ExcelService.cs`; Test `ExcelServiceTests.cs`

- [ ] **Step 1: 失败测试**（Import 列匹配+类型转换+必填+字典值有效性+业务校验；有错行→ErrorFile 标红+错误原因列；全通过→ValidRows）

```csharp
[Fact]
public void Import_InvalidRow_ProducesErrorFile()
{
    var cols = new List<ExcelColumn>{ new(){Field="OrderNo",Title="订单号",Required=true} };
    var r = new ExcelService(stubDict).Import<OrderImportDto>(excelStreamWithEmptyOrderNo, cols, dto => new());
    Assert.Single(r.Errors); Assert.NotNull(r.ErrorFile);   // 空必填行 → 错误 + 回写文件
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现 Import**（ReadRows→MapRow[列匹配+类型转换记错]→ValidateRequired/ValidateDict/bizValidate→ValidRows/Errors；有错 BuildErrorFile[原表+标红+追加"错误原因"列]；列对不上 E-071；大数据量分块读）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): Excel import with row validation + error writeback (ch07 §5)"`

## Task C-3: 前端导入对话框 + 导出按钮（章07 §7）

**Files:** Create `cp6.web/src/components/PubImportDialog.vue` + 导出组合式

- [ ] **Step 1: 实现**——导入三步对话框（下模板/上传/校验结果[成功 N 失败 M + 错误明细 + 下载错误文件]）；导出按钮按当前查询条件请求下载。按钮受章02 `export`/`import` 操作权限（v-permission）。
- [ ] **Step 2: 冒烟 + 提交** → `git commit -m "feat(pub): import dialog + export button (ch07 §7)"`

---

# Phase D — 章08 CRUD 基座 + 代码生成（压轴）

## Task D-1: BaseCrudService<T> 泛型基座（章08 §2）★

**Files:** Create `CP6.Core/Services/Pub/BaseCrudService.cs`; Test `BaseCrudServiceTests.cs`

- [ ] **Step 1: 失败测试**（QueryAsync 自动接 IDataScopeFilter；CreateAsync 自动采番[SeqBizKey 非空]+ 赋 DeptId；UpdateAsync 走 StripReadOnly）—— 用一个 FakeEntity:IDataScoped 测。
- [ ] **Step 2: 跑红 → Step 3: 实现**（照章08 §2：抽象 `ResourceKey`/`SeqBizKey`；Query 注入 `IDataScopeFilter.Apply`；Create 采番[`ISeqService`]+ `DeptId ??= ctx.DeptId`；Update `IFieldPermService.StripReadOnly`；分页）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): BaseCrudService<T> with datascope/seq/dept/fieldperm wired (ch08 §2)"`

## Task D-2: BaseCrudController<T> 泛型 REST（章08 §2）

**Files:** Create `CP6.Core/Pub/BaseCrudController.cs`

- [ ] **Step 1: 实现**——泛型 query/add/edit/del；贴 `[RequirePermission]`/`[FieldMask]`（注：特性参数需常量，泛型基类无法用实例属性插值——**实现者注：改为在子类各 Action override 时贴特性，或用自定义 policy 从路由解析 menu/resource**；本稿建议子类贴特性、基类提供方法体）。
- [ ] **Step 2: tsc/build + 提交** → `git commit -m "feat(pub): BaseCrudController<T> generic REST (ch08 §2)"`

> **实现者注（待你修订定）**：C# 特性参数必须编译期常量，`[RequirePermission(Menu, action)]` 无法用实例属性 `Menu`。三种解法：①子类每个 Action override 并贴常量特性（最直白，放弃"纯基类自动"）；②自定义 `IAsyncAuthorizationFilter` 从路由/约定推导 menu（约定 = controller 名→menuKey）；③代码生成器（D-3）生成子类时把常量特性写进去。**建议 ③**——生成器产出的 Controller 直接含常量特性，基类只兜逻辑。

## Task D-3: 代码生成器 GenTable/GenColumn + Scriban 模板（章08 §3/§4/§5）★

**Files:** Create `GenTable.cs`, `GenColumn.cs`, `CodeGenService.cs`, `templates/*.sbn`; Modify csproj(Scriban); Test `CodeGenServiceTests.cs`

- [ ] **Step 1: 失败测试**（给定 GenTable+GenColumn → 渲染产物含正确实体名/字段/资源键；快照比对 Entity/Service/Controller 关键片段）
- [ ] **Step 2: 跑红 → Step 3: 实现**（实体 GenTable/GenColumn；CodeGenService：读元数据→Scriban 渲染 8 类产物[Entity 实现 IDataScoped/Service 继承 BaseCrudService/Controller 含常量特性/Vue List+Form/columns.ts/DDL/菜单权限点脚本]；二次生成保护 `// <custom>` 块；从 DB 表反向导入字段 `ImportDbColumns`）
- [ ] **Step 4: 跑绿 → Step 5: CodeGenController + 配置 UI + 提交** → `git commit -m "feat(pub): codegen (GenTable/Column + Scriban templates → 8 artifacts) (ch08 §3-5)"`

## Task D-4: 生成产物端到端验证（章08 §6）

- [ ] **Step 1:** 用生成器产一个示范业务表（如 `Demo`）→ 编译通过 → 验证生成的 Controller 带 `[RequirePermission]`、Service 走数据权限、列表带导入导出。这条验证"生成模块开箱带全套能力"（章08 §6 八项）。
- [ ] **Step 2: 提交** → `git commit -m "test(pub): generated module carries full PUB capabilities (ch08 §6)"`

---

# Phase E — 章09 登录聚合集成 + 接入

## Task E-1: 登录聚合管线（章09 §3）

**Files:** Modify `AuthController.cs`（登录成功后预热/缓存上下文）; 确认中间件/`ICurrentPermissionContext` 注入链

- [ ] **Step 1: 实现**——登录成功（JwtHelper 验通过）后调 `PermissionAggregator.BuildAsync(userId)` 预热缓存（或依赖 B1 `ICurrentPermissionContext` 首次请求懒构建——二选一，建议登录预热 + 懒兜底）；前端登录后拉 `/my-actions`（B1）下发 actionKeys。验证三权校验读同一 `UserPermissionContext`。
- [ ] **Step 2: 集成测**：登录→带 token 请求受 `[RequirePermission]` 保护端点→按角色 403/200。
- [ ] **Step 3: 提交** → `git commit -m "feat(pub): login pipeline aggregates UserPermissionContext (ch09 §3)"`

## Task E-2: 示范模块接入 + 接入清单文档（章09 §5/§7）

**Files:** Create `docs/pub/接入清单.md`（或 README 补章）; 挑一个现有模块（如订单）做示范接入

- [ ] **Step 1:** 按章09 §5 五步给**一个现有模块**（建议订单/采购）接入：实体实现 IDataScoped（补 DeptId）→ 注册菜单/操作点/数据资源/字段 → Controller 贴特性 → service 接 Apply → 前端 v-permission。作为其余模块改造的范本。
- [ ] **Step 2:** 落"接入检查清单"文档（章09 §7 八项），供 B3 各业务模块改造时逐项核对。
- [ ] **Step 3: 提交** → `git commit -m "docs(pub): module integration checklist + sample module wiring (ch09 §5/§7)"`

> **注**：采购/财务/MES/WMS/销售 的**全量改造**（章09 §6）属各业务模块自己的工作（B3 阶段），不在本计划——本计划只立框架 + 一个示范 + 清单。

---

## Self-Review（对照章05~09 覆盖）

- **章05**：操作日志=**已实现确认**(C-D1) ✅ / 字典 IDictService 缓存翻译(A-1) ✅ / 富采番(A-2) ✅ / 多语言=已有 Controller 确认 ✅ / 配置画面（字典/多语言已有，采番新增 A-2）✅
- **章06**：Pub_Attachment + IFileStore(B-1) ✅ / 上传秒传 + 删除引用计数(B-2) ✅ / 下载鉴权(B-3) ✅ / PubUpload + 草稿转正(B-4) ✅
- **章07**：ExcelColumn 列配置(C-1) ✅ / 导出+字典翻译+模板(C-1) ✅ / 导入逐行校验+错误回写(C-2) ✅ / 前端导入导出 UI(C-3) ✅ / 数据权限/字段权限自然约束（导出 data 来自过滤后的 service，B1）✅
- **章08**：BaseCrudService(D-1) ✅ / BaseCrudController(D-2) ✅ / GenTable/GenColumn + 模板生成(D-3) ✅ / 生成模块带全套能力验证(D-4) ✅ / 二次生成保护(D-3) ✅
- **章09**：登录聚合管线(E-1) ✅ / 接入步骤+清单+示范(E-2) ✅ / 与 OA 共用 Sys_Dept（B0 已建，本计划文档化）✅

**已知缺口/推迟（已标注）：**
1. **章05 大部分已实现**（C-D1）—— 操作日志/字典/多语言 Controller 现成，仅补 IDictService + 富采番。
2. **EPPlus License / Excel 库选型**（C-D3）—— 商用需你确认（建议 NPOI/ClosedXML 免费）。
3. **OSS/MinIO FileStore**（C-D6）—— v1 仅 LocalFileStore，云存储留接口。
4. **BaseCrudController 常量特性难题**（D-2 注）—— 建议靠代码生成器写入常量特性，而非纯泛型基类。
5. **各业务模块全量改造**（章09 §6）—— 属各模块 B3 工作，本计划只立框架 + 示范 + 清单。
6. **TenantId**（C-D5）—— 章节内不引入，章09 多租户统一（注：本"章09"指 PUB 文档章09=集成，多租户实为另一议题，全系统统一时处理）。

**Type 一致性：** `IDictService.TranslateAsync`(A-1) 被 ExcelService(C-1) 消费；`ISeqService.NextAsync`(A-2) 被 BaseCrudService(D-1) 消费；`IFileStore`(B-1) 被 AttachmentService(B-2) 消费；`BaseCrudService`(D-1) 依赖 B1 的 `IDataScopeFilter`/`IFieldPermService`/`ISeqService`；代码生成产物(D-3) 装配 B1 四粒度 + 本计划字典/采番/附件/导入导出。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-pub-common-modules.md`。**PUB 第三份（公共模组，收尾）**。至此 **PUB 三份计划全齐**：
1. `2026-06-13-pub-b0-org-model.md`（章00 组织模型，根）
2. `2026-06-13-pub-b1-permission.md`（章01~04 权限引擎四粒度，痛点核心）
3. `2026-06-13-pub-common-modules.md`（章05~09 公共模组 + 集成）← 本文

三份覆盖 PUB 全章 00~09。**下一步按工作流是你修订**（拍板 C-D1~D6，尤其 C-D3 Excel 库 License）。定稿后执行：B0 → B1 → 公共模组（公共模组的附件鉴权/CRUD 基座依赖 B1 权限）。

---

*初稿生成于 2026-06-13。源：docs/pub/05·06·07·08·09。已勘察 CP6 真实代码：OperLogFilter/OperLogController/Kafka 异步日志/清理服务**已全套**、DictController/LangController/MenuController/RoleController/UserController **已存在**、DocNumber 静态采番(无富配置)、EPPlus/NPOI/Scriban **均未引用**、Sys_DictData/Sys_OperLog int Id 不继承 BaseEntity 无 TenantId。*
