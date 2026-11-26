# WP08-16 资源身份与独立能力目录交付记录

本记录用于同 PR 的本机验收复核。正式台账为 `PLANNED`，本机候选已补齐 W203 合成前向迁移证据。正式采用待总控确认部署配置，并取得对应 PR 与受保护主线的回执。

## 固定来源

| 对象 | 提交 |
| --- | --- |
| 开工 Main | `3d62a018aa4bfb9c0e2ac835468bfebfbfc078e5` |
| 既有受测产品 | `1cef273e82fe170003522349f77bcbe08a58b3fc` |
| 前向迁移受测原件 | `b7034ac0b90890dcb96efd350be8082e0768adf3`，树 `0fd6d3ecac064345a86da50659562f24ca22a633` |

在 `LAPTOP-3QQ44FJS` 的 `DEV-1` 上由 `CP6-EXEC` 登记执行。批准范围、严格前置及采用基线见本机派工决定 `D:/CP6-test-artifacts/WP08-16/WP08-16-LOCAL-DECISION-20261126.md`。

产品候选复用实际 Machine、WorkCenter 和工时 UOM 的原身份，写入独立归属版本和精确批准来源，沿用当前授权、原结果和审计事务。[原卡](../../task-plans/WP08.md#wp08-16) · [接口与版本契约](../../resource-capabilities-v1.md)

## 卡级本机验收

| 已验证范围 | 结论 |
| --- | --- |
| 初始资源登记与归属 | `PASS`。没有路线也可登记模拟设备、固有能力和归属。更新归属后，旧路线的原文和固定资源引用仍可读。 |
| 能力与工时原件 | `PASS`。当前能力读取保留精确声明和模拟批准来源。有声明、无批准为 `DECLARED_NOT_QUALIFIED`，缺证为 `UNKNOWN`。固定工时 UOM 及其原时间含义可读。 |
| 事务、原键与权限 | `PASS`。相同原键可恢复，过期版本受约束，失败时归属头和原结果共同回滚。实际 HTTP 覆盖当前对象和权限。 |
| 资源能力页 | `PASS`。8 个模拟组件用例通过，包括能力与归属各自的原键恢复。 |

以上沿用既有 `1cef273e82fe170003522349f77bcbe08a58b3fc` 的隔离 PostgreSQL 与实际 Main HTTP 9 PASS、0 FAIL、0 SKIP，以及 8 项组件用例、锁定还原、Release 构建、Vue 类型检查和 Vite 构建。从该提交起产品源码、迁移、已有测试、构建配置均一致，故沿用原证据。原始记录在 `D:/CP6-test-artifacts/WP08-16/exec9-20261126T030752Z/DELIVERY.md`，TRX 位于 `pg-run-01/wp08-16.trx`，SHA-256 为 `3ca6109d60b3e5a49126a28b5c703c4cdce5668c1f7708337bfb6e226c576e54`。

## W203 前向迁移补验

使用 SDK `10.0.404`、.NET 10 Release 和 PostgreSQL `18.6`，仅连接独立的 `127.0.0.1:55441`。在迁移 `20261124161000_PlanningDemandSourceIntakeV1` 的库上，用 W203 持久化格式合成写入同一机器的 v1/v2 声明及事务原回执。旧原件约束与公共原键日志在写入时生效，实际应用 `20261126031000_ResourceAssignmentAndCapabilityApprovalV1` 后启动 Main HTTP 验证。

- 原文、操作输入、回执及审计内容逐版本比对一致。HTTP 当前版为 v2，v1 的 `IsCurrent=false`。两个原键均返回 `COMMITTED` 和各自原回执。
- 新批准动作定义已登记。迁移前后逐条比对的 12 条角色授权一致，原声明角色申请批准收到 HTTP 403。当前既有声明保持 `DECLARED_NOT_QUALIFIED`。
- 单项执行 **1 PASS，0 FAIL，0 SKIP**。实际 API `127.0.0.1:59193` 已退出，`pg_ctl status` 返回 3，postmaster PID 文件已撤除，复核本用例无监听及残留进程。保留隔离数据与原始证据。

运行原件在 `D:/CP6-test-artifacts/WP08-16/premerge-20261126T034015Z`。TRX `results-01/wp08-16-w203-forward.trx` 的 SHA-256 为 `f9371c1b653e1ab0bcb8a5941a270452a25ea067f0af71ccc598d406cc2e79f3`。`validation-01.json` 记录精确过滤、程序集校验、停机读回。逐版摘要与原操作键在 `permission-consumption-wp08-16-w203-01/wp08-16-w203-forward.json`，SHA-256 为 `44ebf09797f278bf295024fa1654f2152301723bcbf21cfb9728ef6d13264495`。

## 采用和待验

同 PR 台账材料位于 `D:/CP6-test-artifacts/WP08-16/premerge-20261126T034015Z/ledger-preview`。其中有本卡验证证据字段及官方生成器的验收中预览。正式 `delivery_evidence` 待实际 PR、主线采用 SHA 和源码包含性确认，最终状态页按正式台账重新生成。

`MES-RES-01` 保持开放。205 条原清单和 4 条必需修订场景保留 `NOT_RUN`，其中若干断言有上述局部证据，完整场景仍待验。路线相关适用性、完整 `RES-WRITE-1`、生产 Start／WO 准入、真实外部批准资料、既有实际环境迁移及浏览器端到端均为 `NOT_RUN`。

本机分支 `codex/wp08-16` 随 `DEV-1` 停留在活动登记。正式采用前保全分支和证据，合并后的席位处置按工作区政策办理。[工作区政策](../../worktrees/POLICY.md)
