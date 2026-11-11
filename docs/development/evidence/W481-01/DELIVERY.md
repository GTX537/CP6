# W481-01 · 同一 v11 完整候选恢复收口

状态：VALIDATED_PENDING_REMOTE。原任务 WP02-02，执行者 COORD，DEV-2，分支 `codex/w481-01`，起点远端 main `42072bffb5205c4eabb371264f026f58728275d2`。本任务覆盖 [12h 计划](../../planning/20261111-codex-12h-business-chain.md)的候选组合和现行差异；相关步骤保持在同一分支和报告。

原 v11 使用 W477 的 41 成员归档，manifest `f0d711a4b488e2df294364dacb8c1151f2c01036b84c24e35eed60b53cdd5e07`；W478 的真实租约/本地模拟确认及 W479 缺件/回执恢复证据按原范围复用。本次新增两个可显式运行的原生验证入口，既有导入、对账、Owner 和应用代码未改。

## 实际结果

1. `verify_combined_candidate.py` 在同一 PostgreSQL 18.6 候选执行 ERP Owner-only 导入 → Core commissioning → RMA 两表和原 journal → Engineering 同源前置 → FileContent。Core、journal、ERP C03 和 FileContent 四类回执均可独立恢复，文件落到本任务独立目录，原 45 字节保留。普通 A/B LOGIN 读取及 B 直读 A 的原生 42501 通过；候选始终无环境/恢复 admission，`recoveryReady=false`。
2. 错误顺序控制先向普通角色授予 CONNECT，ERP 在写入前按 `rma-stage-erp-candidate-not-isolated` 拒绝，Outbox 仍为零。FileContent 在原数据和 journal 已写、回执尚未写的真实事务晚段触发 23514：相关写入完整回滚，既有 RMA/ERP 和 B 保持；未提交的文件回执恢复拒绝，随后正确导入和全部恢复通过。
3. `verify_current_v11.py` 在原来源的独立停机副本运行实际对账 CLI。原 IAM 17 项、ERP 与 Stock 当前读回保持；真实 SQL 全局键探针将他租占用判为 BLOCKED/GLOBAL_KEY_CONFLICT，缺失原键判为 UNKNOWN/ORIGINAL_KEY_NOT_FOUND，不返回他租当前详情。
4. 在私有副本模拟当前租户 `Enable=false` 后，IAM 明确记录归档 `enabled=true`、当前 `enabled=false`；Stock 返回 BLOCKED/ROUTE_AUTHORITY_CONFLICT。只读对账保留现行撤权、未产生发送，B 的 ERP/Stock 原件、租户和 RMA 头保持。
5. 当前 Stock route 的直接 epoch 更新被真实 55000/CP6_OUTBOX_IMMUTABLE_EVIDENCE 拒绝，事务未提交，随后原 route 对账仍正常。**实际 epoch 变化 NOT_RUN**：当前固定 profile 没有本次可采用的合法轮换路径；须在相应 Owner/profile 中明确真实路径后执行，不能把这次拒绝或未来发包当作已运行轮换。

## 环境、复用和失败记录

本次实际运行 PostgreSQL 18.6、现有 .NET 租户/Owner 工具及 Python CLI。业务对象、A/B 租户及撤权输入为模拟数据；数据库、普通角色、原生约束、事务和 CLI 实际执行。DatabaseInit 使用 W351 封存宿主、W353 工具，原 journal 使用 W372 工具；W406 四个封存输入的既有 pin 与磁盘文件一致。该结果是组合恢复验证，不代表用当前 main 重新运行全部宿主/业务验收；应用源码未变，未重建应用。

同源 Engineering 产品、产品版本和修订头由来源私有副本提取，在单一事务准备为 FileContent 的先行依赖，延迟 FK 和原验证触发器保持启用。v11 本身只带其声明的被引用 working version；本次没有实现通用 Engineering 恢复。普通 LOGIN 验证也不替代恢复批准后的真实 HTTP 授权链。

首次夹具选择的 W473 tenant CSV 属于另一组租户，在导入前拒绝；本次改从同一 W477 来源提取，未改 TenantId 或业务键。`native-01` 在准备修订头时以 23503 暴露缺少产品及完整延迟外键图；补齐同源前置后 `native-02` 成功。随后为组合路径新增错误顺序/晚段回滚，`native-03` 成功。集中审查把 B 保护快照前移到 Core 导入前，并采用既有路径字面量转义；最终组合回归 `native-04` 成功。`current-01` 真实发现 route 不可变；最终 `current-02` 保留该拒绝为预期边界，明确实际 epoch 变化未运行，并通过撤权和全局键检查。上述原生运行的失败输出保留。

原始命令、参数和结果统一位于 `D:/CP6-local-test/CP6-12H-20261111/W481-01`：组合参数 `fixture-04.json` 对应 `native-04/combined/actual.json`，现行验证由 `run-current-02.py` 执行、结果 `current-02/actual.json`。两个仓库入口的 `--help` 列出完整参数；上述本地参数可原样复现。来源原目录未启动、未覆盖；本任务来源副本及四个组合夹具均已停机。

一次任务级 SPEC/QUALITY 集中审查已完成，修正后的 B 保护范围已通过上述组合回归。完整分支相对基线的代码/文档和台账语义已核对：其他原任务、目标、正式验收状态、合同来源及旧执行记录保持；新增中文编码问题已在提交前修正。文档结构/生成页、引用和 diff 检查通过，未因文档改动运行产品回归。

## 剩余和交付状态

仍缺完整 Stock 候选/相应正式制品采用、ERP 非本 C03 原图、当前权限/凭据与实际恢复批准链，以及 PGT-021/022/024 的完整正式采纳。Main 现锁 0.10.3，Stock 发布采用仍按结算中的 0.10.4 正式制品依赖处理；本次 epoch 限制也不宣称仅发包即可解决。

正式 PostgreSQL 保持 16 PASS、11 NOT_RUN；原 375 项保持 38 DONE、1 IN_PROGRESS、336 PLANNED。本报告不宣称整卡或业务目标完成。分支处置：集中审查通过、待正常远端采用；工作区：DEV-2；席位：尚未释放。未触发 Actions 或部署。
