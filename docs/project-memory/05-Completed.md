# 已完成能力与近期里程碑

## 2026-08-02 E12-S03 距离、拥堵、容量、吞吐和成本仿真

- 以 `ab21aed4` 完成确定性仿真引擎、不可变证据、迁移、API/UI/权限/SDK，并先行推送远端备份；no-ff 集成提交为 `PENDING_INTEGRATION_COMMIT`。
- 距离、拥堵、容量、吞吐、人工和成本均有显式规划口径与未知覆盖；只读生产隔离场景和脱敏历史数据集，不读取实时运行态、不排名方案且不允许生产回写。
- 新增 3 个 API、2 个权限、两张租户隔离证据表，OpenAPI 增至 77 operations；规划页提供容量/时间桶/币种/单价配置、五类 KPI、热点和哈希证据。41 个新增词条具备五语运行时种子，既有 i18n 欠账仍为 908。
- 全量门禁为 Space Unit 268、默认 Space Integration 242 passed / 63 SQL-gated skipped、CP6.Tests 2771 passed / 17 environment-gated skipped、前端 122 files / 670 tests、非增量 Release 0 error / 3 条既有 warning；双 EF、SDK、TypeScript 与生产构建通过。
- 下一张独立实施卡为 E12-S04 多场景比较与决策记录；受控集成和临时资源清理结果待补记，`main` 未修改。

## 2026-08-02 E12-S02 脱敏历史任务数据集与回放时钟

- 以 `4fb6941d` / `d89919b8` / `c8ccbf56` 完成数据/时钟/迁移、API/UI/权限/SDK 与 no-ff 受控集成；最多 10,000 条历史任务可固定到克隆成功且生产隔离的场景。
- 合同只接受不可逆 SHA-256 task/worker token 和显式脱敏确认，位置必须存在于场景快照；数据集、任务与回放证据不可变且不允许生产回写。
- 新增 3 个 API、2 个权限、两张租户隔离表、Migration/幂等 SQL，OpenAPI 增至 74 operations；规划页提供 Ready 场景导入、列表和确定性回放证据。
- 全量门禁为 Space Unit 264、默认 Space Integration 239 passed / 63 SQL-gated skipped、CP6.Tests 2767 passed / 17 environment-gated skipped、前端 121 files / 667 tests、非增量 Release 0 error / 10 条既有 warning；双 EF、SDK、TypeScript、生产构建和合并态复验通过。28 个新增页面词条具备五语运行时种子，既有 i18n 欠账仍为 908。
- 功能历史已备份并进入远端受控集成；功能工作树及本地/远端临时分支已删除，释放约 2.03 GiB，`main` 未修改。下一张独立实施卡为 E12-S03 距离、拥堵、容量、吞吐和成本仿真。

## 2026-08-02 E12-S01 生产隔离规划分支

- 以 `c673b7ec` / `8d75e79e` / `0ac603d4` / `3d41c8d9` 完成隔离模型、功能、no-ff 集成与五语收口；内部规划人员可从当前生产 Published 快照创建多个不可变血缘的异步克隆场景。
- `PlanningScenario` 版本不占生产 Draft/Published 指针，领域与数据库均拒绝其进入生产发布生命周期；生产后续发布不会改变已固定的场景基础快照。
- 新增 3 个 planning API、2 个权限、动态菜单、场景工作区、Migration/幂等 SQL、OpenAPI 71 operations 与同步的 C#/TypeScript SDK。
- 全量门禁为 Space Unit 261、默认 Space Integration 235 passed / 63 SQL-gated skipped、CP6.Tests 2763 passed / 17 environment-gated skipped、前端 120 files / 664 tests、完整 solution Release 0 error / 10 条既有 warning；EF/SDK/TypeScript/生产构建通过。20 个页面词条具备五语运行时种子，既有 i18n 静态欠账仍为 908。
- 功能历史进入远端受控集成后已删除功能工作树及本地/远端临时分支，释放约 2.68 GiB；`main` 未修改。下一张独立实施卡为 E12-S02 脱敏历史任务数据集和回放时钟。

## 产品能力

- ERP 销售主线、MES 制造执行、WMS 仓储物流及 ERP→MES→WMS 闭环已成型。
- FIN、PUR、OA/WF、PUB、PLAN、Space、多租户和安全底座已有大规模实现，不再是 README 早期描述的“仅待编码”。
- 五语 i18n、动态菜单、角色/动作权限、操作日志、SignalR、后台 worker、Docker/K8s 部署均已落地。
- Space 已完成发布、查看器、库存覆盖、多楼层、路径与成本等多波建设。
- WF 已完成信箱、通知、引擎硬化、服务任务、触发器、基础设施、子流程等多波建设。

## 2026-07 横切收口

- 多模块权限写端点贴点与种子已覆盖 OA/WF、ERP、MES、WMS、FIN、PUR、PLAN/PUB、Space 等主要域。
- HttpPatch 已纳入八套权限反射扫描。
- 新增“后端贴点必须存在于种子”的跨模块互锁测试。
- WF 审批归属校验已下沉引擎：本人、有效委派或系统 Actor 才能操作；admin 不再天然越权。
- 标准一般用户角色 `RoleId=10` 已按租户幂等预置，含 OA 最小菜单与动作集合。

## 2026-07-30 Space V1 受控集成基线

- 将散落工作树中的未提交 Space 后续实现固化到安全检查点 `0d25da4d`，完成敏感信息、异常大文件和生成物审计；原 E01 S03 分支保持未污染。
- 从当前交付基线 `dcc1ac9a` 建立唯一集成分支 `integration/space-v1-20260730`，以 no-ff 方式合入 E00 S01–S04 与 E01 S01–S03，形成提交 `539d56de`。
- 合并冲突按双侧约束共存处理：保留 WMS 序列追踪不可降级、Definition 不可变、Space 审计追加写三套保存护栏；解决方案同时保留 Mobile 与六个 Space 项目。
- 集成态验证通过：Release build 0 error；Space Unit 35 passed；Space Integration 7 passed / 18 SQL-gated skipped；CP6 主测试 2664 passed / 17 environment-gated skipped；前端 86 files / 539 tests、type-check 与 production build 全通过。
- E01 S04 Published→Draft Clone 已从候选中重建为最小切片：功能提交 `bac76444`，no-ff 集成提交 `85792161`；未夹带后续 BeamHeight、资产范围、规划场景或历史重发布能力。
- S04 功能态全量回归通过；合并态 Space Unit 41 passed、Space Integration 9 passed / 22 SQL-gated skipped，`dotnet ef migrations has-pending-model-changes` 确认模型与 Migration 一致。
- E01 S05 Design API v1 已按冻结边界重建：功能提交 `3258d47f`，no-ff 集成提交 `36f534d9`；交付 6 条路径/8 个操作、Problem Details、RBAC/外部主体闸、Site cutover、cursor 分页、24 小时幂等重放及 90 天保留索引。
- S05 同步交付可重复生成的 OpenAPI、C# SDK 和 TypeScript SDK；漂移检查、C# build、TypeScript strict compile 均通过。合并态全解构建 0 error，Space Unit 44 passed、Space Integration 9 passed / 24 SQL-gated skipped、Design API/权限聚焦 17 passed，EF 模型无待迁移变更。
- E01 S06 文件安全与保留已按冻结边界重建：功能提交 `6daf1aeb`，no-ff 集成提交 `2ccdff7a`；交付 Quarantined→Scanning→Clean/Rejected、失败关闭扫描、隔离 Worker 契约、引用感知墓碑和对象删除补偿。
- S06 合并态 Release 全解构建 0 error，Space Unit 52 passed、Space Integration 17 passed / 29 SQL-gated skipped，EF 模型、SDK drift 与 TypeScript strict 检查通过；新增 5 个真实 SQL Server 测试因本机认证门禁记作 skipped，不记作 passed。
- E02 S01 的中立实验门禁已按非生产边界重建：功能提交 `fe959066`，no-ff 集成提交 `3742fbff`；交付数据包完整性/版本审计、确定性压力资产、适配器子进程证据、ODA/APS fail-closed preflight 与隔离 Aspose 淘汰复现，不包含生产 `ICadConverter` 或 E02 S02。
- E02 工具 10/10 测试通过，50MiB/100 万实体压力生成通过；Aspose 26.6.0 复验为 25 次中 20 次成功、5 次 L5 崩溃，且 20 个成功观察均只保留图层 `0`。E02 S01 最终选型仍因正式黄金集、授权、供应商环境和冻结 Worker 缺失而阻塞，不计作完整签收。
- E07 S01–S03 已按冻结边界从候选重建：功能提交 `d06a8bd1`，no-ff 集成提交 `6e67a9d1`；交付 `space-wms-adapter-v1`、CP6 真实适配器、`T_SpaceWmsOperation` 幂等账本、标准内存模拟器、同构库存/任务查询和五类故障注入。
- E07 功能态与合并态验证通过：Release 全解构建 0 error，Space Unit 73 passed，Space Integration 35 passed / 30 SQL-gated skipped，CP6 主测试 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型无待迁移变更；未夹带 E07 S04/S05、E08、E13、Workload 或发布 Saga。
- E07 S04 标准仓已按第 9 节冻结协议独立重建：功能提交 `74577015`，no-ff 集成提交 `6d751e0c`；交付确定性 500 货架、10,000 库位、SKU/库存/批次/容器、100 个拣货任务、DXF/底图/期望答案、WMS seed 与 6 个固定故障样本。
- S04 两次独立生成 17 个文件逐字节差异为 0，干净检出后的 Manifest 哈希错误为 0；合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 79 passed、Space Integration 40 passed / 30 SQL-gated skipped、CP6 主测试 2680 passed / 17 environment-gated skipped、Client 71 passed。DWG 外部门禁继续归 E02，不伪造资产；E07 S05 仍等待 E04 S04。
- E13 S01 已按 ADR-0002 和 AI Schema v1 冻结边界独立实现：功能提交 `8f7fc25e`，no-ff 集成提交 `ea161975`；交付 Provider/确定性端口、强类型输入输出、Provider 别名注册表、租户/Site/数据策略/外部开关门禁与原子配额租约端口。
- S01 默认依赖注入为租户 Disabled、Provider 空注册和配额失败关闭；新增 `space:model:generate-ai` / `space:model:review-ai` 权限及四个稳定 AI 错误码。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 97 passed、Space Integration 41 passed / 30 SQL-gated skipped、CP6 主测试 2680 passed / 17 environment-gated skipped、Client 71 passed；未新增 Migration、HTTP、外部适配器或 Provider 凭据。
- E13 S02/S03/S12 已分别以 `cff25a25` / `94822669`、`cebd401a` / `dca6e19c`、`54456946` / `b33929fb` 完成受控实现与集成：交付可审计 Run/Proposal/Decision/Usage 模型、Import/BuildScene 可恢复 Worker 控制面，以及数据库三并发槽和日/月预算原子账本；外部 Provider、CAD IR、输出校验、融合和 Apply 仍保持关闭。
- E05 S01–S04 已按独立边界交付通用元素、非均匀逐层货架、Design Revision 权威场景和 System/Tenant 版本化资产库；功能/集成提交依次为 `5bb0cdfb` / `49dbabe3`、`2fc03681` / `3d554852`、`00021f0a` / `a1edecef`、`85b57960` / `888de795`。
- E05 S05 以功能提交 `856f138c`、no-ff 集成提交 `a3864d9c` 交付 `space-parametric-v1` 确定性前端渲染链：逐层货架、box/path/polygon/point/asset、安全资产占位和稳定拾取映射均已覆盖；point 缺 Z、未知资产字段和运行态载荷失败关闭。
- E05 最新验证：Space Unit 203 passed；默认 Integration 46 passed / 41 SQL-gated skipped，真实 SQL 聚焦链 11/11 passed；前端 type-check、88 files / 546 tests 和 production build 通过，仅保留既有大 chunk 提示。
- E04 S01 以功能提交 `1d57a3b5`、no-ff 集成提交 `e8e84853` 交付 PDF/PNG/JPG 底图上传、E01 文件安全扫描复用、Ready/Clean 楼层挂接、受权 Blob 内容读取及 PDF.js/Konva 渲染；显隐、透明度和锁定已覆盖，S02 标定与 S03/S04 编辑命令未提前混入。
- E04 S01 验证：Space Unit 205 passed；默认 Integration 48 passed / 42 SQL-gated skipped，真实 SQL 6/6 passed；CP6.Tests 2685 passed / 17 environment-gated skipped；前端 type-check、90 files / 557 tests 和 production build 通过；合并态完整 solution 0 warning / 0 error。
- E04 S02 以功能提交 `20ee0af0`、no-ff 集成提交 `c1043d15` 交付两点等比标定、第三控制点动态阈值验证、坐标确认、append-only 审计记录、Floor/Version revision、来源复合外键与 Published→Draft Clone 保真；没有混入 S03/S04 编辑命令。
- E04 S02 验证：Space Unit 210 passed；默认 Integration 48 passed / 43 SQL-gated skipped，真实 SQL 9/9 passed；CP6.Tests 2687 passed / 17 environment-gated skipped；API/权限 20/20，前端聚焦 3 files / 15 tests、全量 91 files / 561 tests、type-check 与 production build 通过；合并态完整 solution 0 warning / 0 error。
- E04 S03 以功能提交 `b322e84a`、no-ff 集成提交 `39146c38` 交付通用元素 2D 单选、属性面板、RemoveRequested 删除、`UpdateProperties`/`DeleteObject` schema v1 原子命令批次、Floor/Version revision、持久化幂等响应与逐命令 before/after 审计；没有混入 S04 多选、对齐、分布、阵列或撤销栈。
- E04 S03 验证：Space Unit 213 passed；默认 Integration 48 passed / 44 SQL-gated skipped，命令闭环真实 SQL 1/1 passed；API/OpenAPI/权限 21/21；前端聚焦 4 files / 8 tests、全量 95 files / 569 tests、type-check 与 production build 通过；SDK/EF drift 通过。完整 solution 0 error / 10 个既有 warning；CP6.Tests 的 6 个 RFQ 固定日期失败已在 S03 前基线复现。
- E04 S04 以功能提交 `9a87dc30`、no-ff 集成提交 `f9c7fd21` 交付货架/通用元素统一多选、套索、对齐、等距、旋转、批量删除、货架阵列和保存后补偿式撤销/重做；阵列复制 Active 设计层与空编码、Generated/Unbound 库位，不复制 WMS 绑定语义。
- E04 S04 验证：Space Unit 213 passed；默认 Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed；API/OpenAPI/权限 25/25；前端全量 96 files / 575 tests、type-check 与 production build 通过；SDK/EF drift 通过，完整 solution 0 error / 10 个既有 warning。
- `0d25da4d` 中尚未独立提取的剩余范围仍为候选，不计入已完成实现，后续必须按依赖顺序逐项提取。

## 当前 GR-VP 波已完成

- T1：`StandardRoleSeed`，每租户创建一般用户角色，4 菜单、8 动作，insert-only；对应 7 个测试。
- T2：OA/WF 共 40 个按钮、17 个视图完成 `v-permission` 铺设。
- T3：ERP 共 39 个按钮、16 个视图完成 `v-permission` 铺设。
- T4：MES 共 31 条指令、12 个视图、24 个真实写权限键完成 `v-permission` 铺设；设备、工单、质检的新增/编辑模式已精确分流。
- T5：FIN 共 66 条指令、16 个视图、51 个真实权限键完成 `v-permission` 铺设；预算行内编辑在无 edit 权时保留只读值。
- T6：PUR/PLAN/PUB 扫描 12 个目标视图，37 个页面级权限声明覆盖 33 个唯一写权限键；`VolTable` 的 Seq 桌面/移动 CRUD 入口及异步权限加载完成守权，并新增 6 条权限回归测试。
- T1–T6 均有 SDD 报告与审查记录，Git 提交见 `CHANGELOG-AI.md`。
- T7：T6 已合入并推送 `main`；API/Web 从干净提交构建并部署。冒烟修复 OA 表单提交误依赖 `draft:add` 的链路，改走 `wf/flow/submit`；`qa_general` 的 4 菜单/8 动作、本人审批、他人待办拒绝和无权端点 403 均已实测。测试流程与临时定义清理归零。
- T7 数据环境仅注册 `DEFAULT/A1`，已验证全部现存租户；没有为满足旧计划的“四租户”描述而创建虚构租户。

## 换机资产

- 三个 SQL Server 数据库已在 2026-07-18 完成压缩、checksum 备份和 VERIFYONLY。
- `.bak` 已通过 Git LFS 上传至私有 GitHub 仓库。
- 所有本地分支、历史 marker 和迁移标签已推送。
- 恢复标签：`migration-2026-07-18-ready`。
