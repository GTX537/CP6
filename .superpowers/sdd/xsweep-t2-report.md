# X-SWEEP T2 报告：「贴点⊆种子」互锁测试扩六模块

状态：**complete**（commits d3e6fcb5 主体 + 3afc16b4 审查Minor加固）
测试：新增 8 Facts 全绿；全量 **2198 绿（2190+8）/ 5 skip / 0 fail**（主体与加固后各跑一次全量，均此数）。

## 交付物
`CP6.Tests/PermissionSeedInterlockTests.cs`（单文件，零生产代码改动）：
- 六模块各一 Fact：反射收该模块命名空间控制器全部变更端点（POST/PUT/PATCH/DELETE，与 T1 同口径含 PATCH）的 `[RequirePermission]` 贴点 →测试内**字面量锚定表**（誊自 docs/seeds 真相源锚定小节，零生产常量引用，独立于 *PermissionAttributeTests oracle）换 MenuId →断言 (MenuId, action) ∈ 种子类 `Actions`（反射读 private static，ITuple 容忍元组形状差异）。⊆ 方向：种子多于贴点不红（view 键/词表补位属正常）。
- 三守卫 Fact：空扫假绿（零贴点即红）/ 豁免表全空现状 pin / 九种子 Actions 反射可读非空。

## 六模块互锁结果（首跑即全绿）
| 模块 | 命名空间 | 锚定键数 | 种子面 | 结果 |
|---|---|---|---|---|
| WMS | Controllers.Wms | 30 | WmsPermissionSeed | ✅ 0 offender |
| ERP | Controllers.Erp | 11 | ErpPermissionSeed | ✅ 0 offender |
| MES | Controllers.Mes | 9 | MesPermissionSeed | ✅ 0 offender |
| PUR | Controllers.Pur | 7 | PurPermissionSeed | ✅ 0 offender |
| PLAN/PUB | Controllers.Plan+Pub | 4 | PlanPubPermissionSeed | ✅ 0 offender |
| OA/WF | Controllers.Oa+Wf | 7 | Oawf∪InboxBatchTransfer∪FlowTrigger∪WorkCalendarConnector 四种子并集 | ✅ 0 offender |

## 首跑归因（brief §4）
全六模块首跑绿——各波 *PermissionSeed 本就按「与去重贴点 1:1」构造，不存在贴点∉种子形态。**豁免表全空**。M-OA/WF 票#4 的 2 个 view 豁免键（oa-form-catalog:view/oa-form-search:view）核实为**只读 POST 豁免、未贴键**（OawfPermissionAttributeTests.ReadOnlyPostExemptions 在案）→不产生贴点元组、天然不入 ⊆ 面，无需豁免登记。

## 审查（opus，独立子代理）
**Ready to merge，零 Critical/零 Important**。审查者逐一核对 68 条锚定映射×真相源文档×种子类**零誊写错**（含点名风险 pur-rfq=705/pur-pr=706 换序、mes-machine=310 历史错配修正）；命名空间面完备（FlowTriggerFireController 系 AllowAnonymous+ApiKey 蓄意不贴键，正确落面外）；OA/WF 四种子并集完备无第五种子；注释事实全核实。Minor×3：
1. **已采纳**（3afc16b4）：去 DeclaredOnly+遍历全部 [RequirePermission]（含类级、AllowMultiple 多贴）——互锁自立，不再依赖姊妹反射测试持续强制方法级贴键作结构背书。
2. 豁免 pin 近同义反复——按审查意见保留为蓄意现状快照（登记豁免须动工厂即红）。
3. **已采纳**（3afc16b4）：类 doc 注记 pub-* 键族拆分（pub-dept 等 4 键系 Controllers.Sys 平台面蓄意不入六模块面）。

## 边界注记（审查者提示，留档）
互锁绿证明**贴点⊆种子**，不证明锚定 MenuId 与运行时菜单播种赋的 MenuId 一致——那是 *MenuSeedTests/*PermissionSeedTests 的职责面。三闸合围（漏贴/种偏/贴而漏种）方闭合 admin-403 缺口，本测试精确封自己那一片。

## 自审/concerns
- RED 实证：本测试首跑绿属预期（种子 1:1 构造），破坏性验证依赖三守卫（空扫/锚定缺键/种子字段丢失均实际 throw/红路径）；审查者独立跑 8/8 绿。
- 环境注记：8:18a 构建曾 Roslyn analyzer OOM（既知内存压力，同型见 7/12 事故记忆）——`-m:1` 单线程构建绕开，非代码问题。
