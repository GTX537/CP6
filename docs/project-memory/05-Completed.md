# 已完成能力与近期里程碑

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
