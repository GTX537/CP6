# 项目当前状态

最后更新：2026-07-30

## Git

- 交付分支：`main`
- T6 通过 merge commit `d79a39c` 合入并推送；T7 冒烟修复为 `ffca422`
- Space 受控集成分支：`integration/space-v1-20260730`
- Space E00 + E01 S01–S03 集成提交：`539d56de`
- Space E01 S04 功能/集成提交：`bac76444` / `85792161`
- Space E01 S05 功能/集成提交：`3258d47f` / `36f534d9`
- Space E01 S06 功能/集成提交：`6daf1aeb` / `2ccdff7a`
- Space E02 S01 实验门禁功能/集成提交：`fe959066` / `3742fbff`
- Space E07 S01–S03 功能/集成提交：`d06a8bd1` / `6e67a9d1`
- Space E07 S04 功能/集成提交：`74577015` / `6d751e0c`
- Space E13 S01 功能/集成提交：`8f7fc25e` / `ea161975`
- Space 后续候选安全检查点：`checkpoint/space-candidate-20260730`（`0d25da4d`，不得整包合入）
- 远端：`origin`（GitHub 私有仓库）
- 换机标签：`migration-2026-07-18-ready`
- 数据备份：Git LFS 三对象，已推送并校验

## 当前波：Space V1 受控集成

| 范围 | 状态 | 证据 |
|---|---|---|
| E00 S01–S04 | 已进入集成基线 | `539d56de`；事实清单、兼容护栏、数据源契约、审计/可观测性 |
| E01 S01–S06 | 已进入集成基线 | `539d56de` + `85792161` + `36f534d9` + `2ccdff7a`；版本/来源文件/Job Ledger、Published→Draft Clone、Design API v1、生成 SDK、文件安全扫描与保留清理 |
| E02 S01 | 部分进入集成基线，最终签收受阻 | `fe959066` + `3742fbff`；中立审计/压力/运行证据/preflight 已集成，正式黄金集、授权、供应商包/凭据和冻结 Worker 尚缺 |
| E07 S01–S04 | 已进入集成基线 | `d06a8bd1` + `6e67a9d1` + `74577015` + `6d751e0c`；版本化能力合同、CP6 真实适配器、持久化幂等账本、标准模拟器、确定性 10,000 库位标准仓与故障包 |
| E13 S01 | 已进入集成基线 | `8f7fc25e` + `ea161975`；Provider/确定性端口、Schema v1 强类型契约、租户/Site/别名/外部开关门禁、默认 Disabled 与配额失败关闭 |
| E05–E12 剩余范围 | 候选证据，未集成 | `0d25da4d`；不得以候选报告替代集成验收 |

## 上一完成波：GR-VP

| 任务 | 状态 | 证据 |
|---|---|---|
| T1 标准一般用户角色种子 | 完成 | `ddcfa1ac`，7 测试 |
| T2 OA/WF v-permission | 完成 | `15823c38`，40 按钮/17 视图 |
| T3 ERP v-permission | 完成 | `4a48525e`，39 按钮/16 视图 |
| T4 MES v-permission | 完成 | `6e4ade1`，31 指令/12 视图/24 键 |
| T5 FIN v-permission | 完成 | `5732057`，66 指令/16 视图/51 键 |
| T6 PUR/PLAN/PUB v-permission | 完成 | `4bb7512` + `cf20d42`，37 页面级声明/12 视图/33 键；异步加载守权 |
| T7 部署与冒烟 | 完成 | API/Web 双镜像；A1 角色 SQL；`qa_general` 自审批/越权/403 冒烟 |

## 最近验证基线

- Space 集成基线已推进至 `2ccdff7a`：S06 功能态 CP6 主测试 2674 passed / 17 环境门禁 skipped；合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 52 passed，Space Integration 17 passed / 29 SQL 环境门禁 skipped，EF 模型与最新 Migration 一致，SDK drift 与 TypeScript strict 检查通过。强制连接本机 SQL 的补跑仍在业务断言前被 TLS/SSPI/Guest 执行身份认证阻断。
- E02 S01 实验门禁已推进至 `3742fbff`：中立工具 10/10 测试通过，Aspose 隔离实验适配器构建 0 warning / 0 error；5 个冻结 Seed 完整性通过，50MiB 与 100 万实体压力资产生成通过。严格 readiness 按预期退出 `3`，ODA/APS 模板 preflight 按预期退出 `4`，表明外部签收条件仍未满足。
- E07 S01–S03 已推进至 `6e67a9d1`：Release 全解构建 0 error（7 个既有测试 warning），Space Unit 73 passed，Space Integration 35 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型与 Migration 一致；新增代码精确格式门禁通过。
- E07 S04 已推进至 `6d751e0c`：500 货架、10,000 库位、100 SKU、5,000 库存记录、100 拣货任务和 6 个固定故障样本由同一固定种子生成；两次独立生成的 17 个文件差异为 0，干净检出后的 Manifest 16 个受管文件哈希错误为 0。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 79 passed，Space Integration 40 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2680 passed / 17 environment-gated skipped，Client 71 passed。
- E13 S01 已推进至 `ea161975`：既有租户与未配置租户默认 Disabled，默认无 Provider 且配额租约失败关闭；Provider 输入不含租户/Site/文件/存储标识，外部 Provider 需 Site、别名、数据策略与显式开关全部放行。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 97 passed，Space Integration 41 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2680 passed / 17 environment-gated skipped，Client 71 passed。
- Space 集成前端：type-check 通过，86 files / 539 tests passed，production build 通过；仅有既有大 chunk 提示。
- 后续候选检查点 `0d25da4d` 已独立通过更大范围候选回归，但它仍不是实现真相，也不授权整包合并。
- 后端在 GR-VP T1 报告中：2220 passed / 5 skipped。
- 前端在 T7 干净 `main` 重新验证：73 files / 488 tests passed，type-check 0，2649 modules production build 通过；在线 Web 与新 chunk 均为 200。
- T6 后端权限 oracle：11/11 passed。
- T7 真实权限链：4 菜单、8 动作；本人待办 200、他人待办 400、无权端点 403；测试流程数据已清理。
- 这些是最近任务报告基线，不代表生成本知识库时重新运行了全量测试。

## 数据状态

- `CP6DB`、`CP6DB_OA`、`CP6DB_SpaceQA` 已于 2026-07-18 备份。
- 三份均通过 SQL Server `RESTORE VERIFYONLY WITH CHECKSUM`。
- 新机恢复后需重新轮换 Secrets 并做登录、权限、i18n 与关键业务冒烟。
- 当前运行库 `CP6DB` 的租户注册表只有 `DEFAULT/A1`；RoleId=10 为 1 角色/4 菜单/8 动作，admin 为 148 菜单/323 动作。`qa_general` 保留为 A1 常驻测试用户。

## 下一动作

以 `ea161975` 为唯一 Space 代码集成基线：E13 S01 已完成，下一张可独立推进的内部卡为 E13 S02 Run/Proposal/Decision/Usage 数据模型。E13 S04 等待 E02 S03，E13 S05 等待 S04 与正式供应商证据；E07 S05 继续等待 E04 S04。E02 S01 等待正式 20 文件黄金集、DWG/DXF 矩阵、法务/采购授权、ODA SDK 或 APS 受控凭据及 8 vCPU / 32GiB 冻结 Worker 后完成同环境评分。禁止把剩余候选整包合入。SQL Server 环境可用时补跑当前 30 个门禁测试。GR-VP T1–T7 已完成，不要重做。
