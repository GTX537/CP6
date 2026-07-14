# E-T1 报告：designerModel 子流程五字段 + palette 入口 + validateClient 镜像

**Commit** `6e3b663b`（初版）+ 审查修复（§6，feat/wfs-subflow，均已 push）
**测试** designerModel 全量 44/44；前端全量 **473 passed**（基线 463 + 新增 10 = subflow.spec 10 个 it()；palette 断言 +1 属既有测试体变化不计入新增）；type-check 0；build 过。
**变更纯前端**：designerModel.ts / designerModel.test.ts / designerModel.subflow.spec.ts，`git show --stat` 无跨模块污染。

---

## 1. 三处实现（designerModel.ts）

| 处 | 位置 | 内容 |
|---|---|---|
| **SchemaNode 五字段** | onBranchReject 之后 | `subFlowKey` / `subVarsInJson` / `subVarsOutJson` / `subCollectionVar` / `subCompletionPolicy`（全 `?: string`，camelCase 镜像后端 FlowNode Sub*） |
| **NODE_PALETTE 入口** | serviceTask 三入口之后 | `{ type: 'subFlow', label: '子流程' }`（无 color 字段，对齐 serviceTask/inclusive 先例；视觉归 E-T2 `.dot-subFlow` token） |
| **validateClient 镜像** | errBranchReject 块之后、`return errs` 之前 | subFlow 静态镜像块，任一不满足 → `errs.push('oa.designer.errSubFlowConfig')` |

## 2. 镜像与后端 D-T1 静态层 ⑪ 逐条对齐（E-WF-025 静态部分）

| # | D-T1 交接清单（§5） | E-T1 validateClient 实现 | 对齐 |
|---|---|---|---|
| 1 | subFlowKey 非空白串 | `!!n.subFlowKey?.trim()` | ✅ |
| 2 | 存在 ≥1 条 `from===node.id && !isError` 出边 | `edges.some(e => e.from === n.id && e.isError !== true)` | ✅ |
| 3 | subCompletionPolicy ∈ {null/undefined, all, any}（trim+lowercase 后比较） | `pol = n.subCompletionPolicy == null ? 'all' : String(...).trim().toLowerCase()` 后 `['all','any'].includes(pol)` —— 逐字镜像后端 `(SubCompletionPolicy ?? "all").Trim().ToLowerInvariant()`（FlowSchemaValidator.cs:141）；空串归一后 `''` 不在值域 → 拒，与后端 `??` 只接 null 语义全等 | ✅（审查修复） |
| 4 | subCollectionVar 若非 null 不得空白串 | `n.subCollectionVar == null \|\| n.subCollectionVar.trim() !== ''` | ✅ |
| 5 | subVarsIn/OutJson 若非空须 parse 为对象且每 value 为 string，且无数组下标 | `validMap()`：空串放行 / 下标双正则逐字镜像后端 `ContainsUnsupportedSubscript`（ServiceVarsHelper.cs:139-141）：`/\$\.[A-Za-z0-9_.]*[\[\]]/` 与 `/\{[A-Za-z0-9_.]*[\[\]][^}]*\}/` 任一命中即拒 / JSON.parse 为非数组对象且 `Object.values().every(v=>typeof v==='string')` | ✅（审查修复） |

**未镜像**（后端 SubFlowRefValidator DI 层独有，前端无法查库，最终以后端 SaveAsync 为准）：目标 FlowKey 存在性/启用（E-WF-025 DI）、引用环/深度 8（E-WF-026）。面板下拉软提示归 E-T2/E-T3。

## 3. round-trip 承载

新五字段经既有 spread 机制天然承载：`schemaToGraph` 建 `data:{...n}`、`graphToSchema` 展开 `...(n.data as SchemaNode)`，无需改动两函数。round-trip 测试（含 in/out JSON + 集合 + 策略）全过验证。

## 4. 既有测试零回归

唯一既有改动＝`designerModel.test.ts` palette 类型清单断言 +1（`'subFlow'` 追加尾部、排序后），沿二期 D-T1 先例。既有 validateClient 校验（start/end/悬边/approval/stage/serviceTask/HTTP 覆盖/inclusive/失败边/timeout/branchReject）零改动、零回归。

## 5. E-T2 交接清单

- **i18n 键**：`oa.designer.errSubFlowConfig` 校验错误文案键 + palette label `'子流程'`（当前硬编码中文，键化归 F-T1，t() 回退＝既定中间态）。E-T2 palette 项视觉呈现时 label 应过 t()。
- **视觉 token**：palette 项无 color 字段，`.dot-subFlow` CSS token 由 E-T2 在 DesignerCanvas 落地（沿 ServiceTaskNode.vue 既有 `.dot-<type>` 用法，禁硬编码色）。
- **SchemaNode 五字段**已就绪，NodePropertyPanel 子流程配置段（目标流程下拉 `designerApi.list()` 过滤 `enable && flowKey !== 当前`／映射编辑／多实例开关）可直接绑定。
- **失败边**：`ERROR_EDGE_SOURCE_TYPES` 已含 `'subflow'`（波⑤ B-T1 落地），subFlow 节点画错误出边前端已放行，无需 E-T2 改动。

## 6. 审查修复（Needs fixes → 已修，镜像算法等价）

审查者实跑探针证实两处前端更严误报（源头为 brief Step 3 原码；「algorithmically equivalent」约束 + D-T1 交接清单优先）：

| # | 缺陷 | 探针（后端放行/前端误报） | 修复 |
|---|---|---|---|
| Important #1 | 策略比较缺 trim+lowercase：`['all','any'].includes(n.subCompletionPolicy)` 原样比较 | `'All'` / `' all '` | 归一化 `String(v).trim().toLowerCase()` 后比较；同时以 `== null` 兜底 `'all'` 取代原 falsy 短路，与后端 `?? "all"` 精确等价（空串 `''` 归一后不在值域 → 拒，后端同拒） |
| Important #2 | 下标校验单正则 `/[$.{][A-Za-z0-9_.]*\[/` 把 `$`/`.`/`{` 揉进一个字符类，放宽前提误伤普通点串 | `'{"note":"file.test[old]"}'`（值内 `file.test[` 命中 `.`+标识符+`[`） | 拆两条正则逐字镜像后端：`\$\.[A-Za-z0-9_.]*[\[\]]`（字面 `$.` 序列）与 `\{[A-Za-z0-9_.]*[\[\]][^}]*\}`（须闭合 `}`），任一命中即拒 |

两探针场景已转正式用例钉住（subflow.spec +2：`'All'`/`' all '` 放行、`file.test[old]` 值放行），镜像等价可回归。Minor：§测试行计数措辞已改准（subflow.spec 实为 10 个 it()）。

## Concerns

- 无必修项。校验错误码 `oa.designer.errSubFlowConfig` 键文本尚未入 i18n seed（归 F-T1），落库前 t() 回退键名，属计划既定中间态，与 brief 一致。
- 修复 #1 附带一处与 brief 原码的有意偏离：策略空串 `''` 由「放行」改为「拒」——这是使前端与后端 `?? "all"`（只接 null）语义全等的必要项，非新增严格化；D-T1 清单第 3 条值域 {null/undefined, all, any} 本就不含空串。
