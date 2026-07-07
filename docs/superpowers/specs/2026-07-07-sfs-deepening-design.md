# SFS 低代码表单深化设计（子表格 / 附件 / 发布流 / 字段查询）

日期：2026-07-07
状态：用户已拍板四口径（子表格基础版 / 发布流对齐 WFS 版本治理 / JSON_VALUE 动态查询 / ——ABC 属 Space spec）。细 plan 开工令时出（照 F1 先例）。
依据：`oa-wfs-sfs-inventory-2026-07-07` SFS 缺口清单；范围锚定 2026-07-07 用户确认的四件事。

## 1. 目标与定位

SFS 运行时内核扎实（schema 驱动渲染、前后端同语义规则引擎、JSON 存储+快照），但产品面薄——本包补齐低代码表单的四个分水岭能力，使"OA 低代码商业化"成立：**复杂业务表单做得出（子表格+附件）、改版管得住（发布流）、数据查得出（字段查询）**。

**前置依赖**：OA 审批解耦包先行（FormDetail 已换装 ApprovalPanel、`Wf_FormData.SchemaSnapshotJson` 提交快照已在——本包发布流建立在快照真相之上）。

## 2. 子域 A：子表格明细（基础版，拍板）

- **Schema**：`FormField` 新增 `type:'table'`，携 `columns: FormField[]`（嵌套一层、不递归；列控件集限 `input/number/select/date/checkbox`）。
- **数据**：DataJson 中该字段值 = 行对象数组；服务端 `ValidateFields` 递归进行内校验（required/maxLength/pattern/number 沿用现有规则，按行报错定位 `字段名[行号].列名`）。
- **渲染**：DynamicForm 渲染 el-table 行内控件 + 增删行按钮；只读态（FormDetail/打印投影）表格化展示。
- **合计回写（关键设计，求值器零改动）**：不给 ExpressionEvaluator 加路径语法（它的语言克制是特性）。提交/变更时**前后端同步把明细列聚合注入平铺变量**：`<字段>_<列>_sum / _count / _min / _max`（如 `items_amount_sum`）；主表字段 compute/条件边/审批条件照常引用这些平铺变量。注入逻辑前端 ruleEngine 侧 + 后端 `RecomputeAndValidate` 侧各一份、语义一致（两端同步是既有铁律）。
- **设计器**：FormDesigner 控件库加"明细表格"，属性面板出列编辑器（列名/标签/类型/必填/选项，行式增删排序——照条件规则行编辑器同款交互）。
- **行内规则联动/跨行计算**：范围外（进阶版，记演进）。

## 3. 子域 B：附件控件（复用 PUB 附件公共模组）

- upload 控件补真渲染分支：调 PUB 既有附件上传组件/API（多文件、大小与类型限制沿用平台配置）。
- **存储铁律（docs/oa 08 章）**：DataJson 只存附件 ID 引用数组，文件本体在 PUB 附件表——绝不 base64 进 JSON。
- 只读态渲染文件名链接（下载走 PUB 附件权限）；删除表单数据不级联删附件（引用计数属平台演进，本期不做）。
- 顺带小件（同子域交付）：`dept` 控件换真部门树选择器（复用 PMS DeptTree 数据源）；`defaultValue` + `$currentUser`/`$today` 动态默认值；number `min/max` 校验（前后端同步）。

## 4. 子域 C：表单定义发布流（拍板：对齐 WFS 版本治理）

- **模型**：`Wf_FormDef` 加 `Status(Draft=0/Published=1)` + `PublishedAt/PublishedBy`；**多行版本制**——发布后编辑 = copy-on-write 衍生新 Draft 行（Version+1），Published 行不可变（守卫：任何对 Published 行 SchemaJson 的 UPDATE 拒绝）。与 WFS 四期流程版本治理（V-B copy-on-write/E-WF-030 语义）同构，用户一套心智。
- **运行时口径**：发起/提交永远取该 FormKey **最新 Published**；无 Published 可用 → 新错误码拒绝（fail-closed）。历史单据渲染继续走提交快照（不受版本切换影响——审批解耦包已交付）。
- **设计器**：保存=存草稿；新增「发布」按钮（发布前跑 designValidate 全量校验）；草稿可预览（DynamicForm 喂草稿 schema）；版本下拉查看历史（只读）。
- **迁移**：存量 FormDef 全部标记 Published（现状即生效语义，零行为变化）。
- **联动检查**：FlowAdmin 的表单↔流程绑定校验同步改为"存在 Published 版本"口径。

## 5. 子域 D：字段级查询与导出（拍板：JSON_VALUE 动态查）

- **后端**：FormQueryService 扩展字段条件数组 `[{field, op(eq/ne/gt/lt/contains), value}]` → 生成 `JSON_VALUE(DataJson, '$.field')` 谓词（number 类型 `CAST AS float` 比较；参数化防注入——field 名白名单校验自选中表单的 Published schema，**不信任客户端传入路径**）。先按 TenantId+FormKey 索引过滤再 JSON 扫描，万级单量够用；热点字段计算列+索引作为演进保留（本期不做）。
- **前端**：FormQuery 页改造——选表单 → 拉其 schema 字段清单 → 动态条件区（控件按字段类型渲染）→ 结果表可选列（元数据列+表单字段列混排）→ CSV 导出。
- 子表格字段不参与条件（JSON 数组谓词复杂度不值得，记演进）；可导出为嵌套 JSON 列。

## 6. 横切与错误码

- 全按 `docs/00-横切接线规范.md`（权限点：`oa_form_designer:publish` 高危独立；审计：Wf_FormDef 贴 IAuditable；五语词条种子）。
- 错误码锁号（承接审批解耦包 E-WF-031~035 之后）：**E-WF-036** 发起失败：该表单无 Published 版本 / **E-WF-037** Published 版本不可变 / **E-WF-038** 附件引用无效或不可访问 / **E-WF-039** 字段查询条件非法（字段不在 schema 白名单）。
- 测试：明细校验（行内必填/类型/行号定位）、聚合注入前后端等价、copy-on-write 与不可变守卫、无 Published fail-closed、JSON_VALUE 查询（含注入尝试拒绝）、附件引用往返。前端补 ruleEngine.spec（顺带偿还零单测欠账）+ DynamicForm 子表格组件测试。

## 7. 范围外（记录不做）

- 行内规则联动、跨行计算（进阶版）；布局栅格/分组/标签页；规则可视化编辑器（rules 仍手写 JSON，设计器出 JSON 校验提示即可）；设计器拖拽（维持点击添加）；热点字段计算列；子表格字段查询条件；附件引用计数回收。

## 8. 排期

前置=审批解耦包；与 WFS 深化无依赖可并行；建议排位=审批解耦包完成后的下一个 OA 包。开工令时按 writing-plans 出细计划（需实读 FormDesigner/DynamicForm/FormQueryService 现状），编码=Opus 4.8。
