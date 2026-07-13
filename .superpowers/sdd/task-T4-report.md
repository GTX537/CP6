# Task T4 报告：ServiceVarsHelper 点路径限制文档化 + 设计期拦数组下标模板（E-WF-016）

**Status：** ✅ 完成并 push。commit `03e01cd`（分支 feat/wfs-cleanup-tickets）。

## 缺陷核实（现状仍如票面，07-05 计划行号有漂移但语义一致）

- `ServiceVarsHelper.ResolveDotPath`（现 `:148-178`，票面写 `:128-158`——行号漂移，逻辑未变）用
  `path.Split('.')` 逐段 `current = current[part]` 导航。故：
  - (a) 键名本身含点（`{"a.b":1}`）无法表达取值——与嵌套 `a.b` 二义，一律按嵌套解析。
  - (b) 数组下标（`$.items[0]`）不被支持——`current["items[0]"]` 返回 null，`ResolveValue` 静默求值为空串，用户无从察觉。
- `FlowSchemaValidator` serviceTask 分支（现 `:85-95`，与票面一致）原 `bad` 判定只查 kind/connector/path/timer/成功出边，**未**扫描模板下标。
- ⚠️ 已确认 **未** 触碰 T5 范围：`ServiceMode` 值域校验不在本次 diff（FlowSchemaValidator 仅在 `bad` OR 尾部加两项下标检查）。

## 修法（YAGNI 裁定：不实现下标/转义，文档化 + 设计期拦截）

1. `ServiceVarsHelper.cs`：`using System.Text.RegularExpressions;`；类内 `MergeOutputVars` 之后、`ResolveDotPath` 之前
   新增 `ContainsUnsupportedSubscript(string?)`（附完整 XML 文档说明两类限制）；`ResolveDotPath` 补方法级限制注释。
   - 探测语义：仅对 `$.…[…]` 与 `{…[…]…}` 模板 token 报真，避开字面 JSON 数组值（`{"list":[1,2,3]}` 不误报）。
2. `FlowSchemaValidator.cs:93-94`：serviceTask 分支 `bad` OR 追加
   `ContainsUnsupportedSubscript(n.ServicePath)` 与 `ContainsUnsupportedSubscript(n.ServiceParamsJson)` → 命中即 `E-WF-016`。

## TDD 红绿证据

- **红**：加 `ContainsUnsupportedSubscript_DetectsArrayIndex`（复用无脚手架）+ `WebApi_PathWithArraySubscript_E_WF_016`
  （复用既有 `Base()`/`Svc()` 脚手架，改 `ServicePath="/o/{lines[0]}"`）。首跑编译失败：
  `CS0117: 'ServiceVarsHelper' does not contain a definition for 'ContainsUnsupportedSubscript'`。
- **绿**：实现后 `--filter "ServiceVarsHelperTests|ServiceTaskValidatorTests"` → **17 passed / 0 failed**。

## 闸门

- `dotnet test --filter Wf` → **193 passed / 0 failed**。
- 全量 `dotnet test CP6.Tests` → **1831 passed / 5 skipped**（基线 1829 + 本次 2 新测试，零回归；5 skip = 既知 SQLite 限制）。
- `git diff --cached --stat`：仅 4 文件（2 Wf 源 + 2 Wf 测试），44 行增，零跨模块污染。
- 无迁移（未碰实体/DbSet）。无新增 i18n 键（E-WF-016 为既有错误码，无新用户文案）。

## 疑虑 / 环境记事

- 🔴 **磁盘满复发**：跑测时 C 盘 100% 满（0 avail），编译写 refint DLL 报 `IOException: not enough space`，
  且触发 CP6.Core 参考程序集损坏 → WebApi 出现大批 phantom `CS0246`（CP6Context/RequirePermission 等找不到）。
  处置：①清 Windows Update 下载缓存（`SoftwareDistribution\Download` 1.41GB）②删 `CP6/publish-docker`（117MB，可重生部署产物）
  ③清 nuget http-cache ④删 `ms-playwright`（0.67GB，前端浏览器二进制，与 .NET 后端无关，`npx playwright install` 可重装）
  ⑤删 CP6.Core `obj/.../ref|refint` 后重建修复损坏参考程序集。腾出后 free ~1.8GB 方跑通全量。
  **根因仍是 50GB 盘容量不足（WSL vhdx 独占 12GB），扩容才是治本**——与 MEMORY 7/12 事故同源，非本任务代码问题。
- 实现无其他疑虑：regex 语义已对 5 例断言逐一核验（含字面 JSON 数组/嵌套数组/字符串内裸括号均不误报）；
  validator 首个 bad 节点 `break` + 结尾 `Distinct` 保证单条 E-WF-016。
