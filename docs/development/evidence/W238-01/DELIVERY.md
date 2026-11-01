# W238-01 人工补货草稿与 prepare 登记

COORD 本聊天单人，延续 CP6-7D-20261022 的 D2。从最新 Main `37994dddbe3c5d4fa7eff4b70af37baeb5cf84b7` 创建 `codex/w238-01`，复用 DEV-1；Handoff 复用 DEV-2。实际执行初估 60–100 分钟，最终时长按官方 finish 核销。W237 已按远端采用及席位释放证据归档一次，没有重复实施或清理。

## 实际交付

新增人工补货创建、DRAFT 完整编辑、当前版本读取、服务端分页版本历史与 prepare 登记 API，并接入原补货页面。完整 Product／Technical／Owner／UOM、单一源地点及准确 Root 范围保留；数量使用字符串与服务端整数运算，八位小数无损。人工可选规则允许的非 FEFO 来源，容量按实际所选范围核算，不被自动建议的选源顺序误拒。

草稿不增加目标未到量；编辑必须带当前 revision，两个并发完整替换只接受一个，历史原件和原命令不覆盖。未保存的页面编辑不能直接 prepare。优先级 1 必须同时具有当前 expedite 权限和理由，备注遵守现有字段权限，历史与原操作读取也应用当前字段可见性。

prepare 与批量登记共用目标及来源预算，准确检查当前规则、来源位置／观察、库存可移动条件、typed 容量及两端权限；一次事务保存冻结选择、REGISTERED 版本、唯一 ActiveIntent、prepare 结果、稳定 dispatchKey／operationId／writerEpoch、NOT_SENT 发送知识、事件和原命令。任何登记写入失败全部回滚，响应丢失以原键恢复。新键对同一冻结选择返回原 prepare；批量 prepare 沿用原 ActiveIntent，不重复占目标预算。

当前 Transfer 准备适配仍未接入，因此返回 HTTP 202、WAITING_TRANSFER_ADOPTION、canDispatch=false，订单保持 REGISTERED；原义务持续计入。**没有将该状态称作 PREPARED，没有发送 Transfer 请求或产生 Stock／Claim／Mobile 效果。** 这轮完成的是人工草稿与 prepare 前的原子登记、原身份及持久等待，实际 Transfer prepare／派发／物理执行／停止继续推进。

审查同时修复 [Issue #663](https://github.com/GTX537/CP6/issues/663)：已登记来源范围按 Root Owner/id 保留预算，换观察版本不能再次分配同一物理范围。版本仍用于准确选择与新鲜度检查，单个完整上游观察也不能同时包含同根不同版本。该直接相关修复保留在同一个补货预算任务分支；Issue 待远端 main 采用核对后关闭。

接口及合同见 [补货合同](../../replenish-planning-v1.md)。迁移新增不可变 order_versions、可变 order_heads 和不可变 prepare_registrations，为既有批量订单回填版本 1；后续批量创建也登记版本。

## 实际验证

生产业务验收输入为**明确模拟的规则、库存 Root／观察、外部未到义务、容量和 IAM 测试账号**。实际运行隔离 PostgreSQL 18、Main Owner/UoW 和真实 API 宿主；页面组件使用模拟 API 返回。模拟业务资料不等于跳过业务代码，验收状态与部署状态分开记录，未部署生产。

14 个不同 PostgreSQL／HTTP 场景通过（11 个人工草稿／prepare 场景、3 个受影响建议／批量回归）：

- 草稿不计义务、完整编辑及不可变分页历史、旧版本冲突、原命令返回原版本、prepare 只计一次。
- 人工明确选择非 FEFO Root，容量按实际所选范围而非自动分配核算。
- 人工与批量并发争用同一缺额和来源，只有一份登记；两个并发草稿编辑只接受一个当前版本。
- prepare 写入中途失败，订单版本／义务／原命令整组回滚；原步骤重试可完成。
- 提交成功丢响应，撤销 prepare 权后仍能找回原结果；当前源区权限和跨租户隔离保护读取与历史。
- 当前容量 LIMITED(0)、来源观察变化及禁用目标拒绝新登记，零新增义务。
- 加急独立权限、备注显式拒绝／允许及撤读后历史与原命令投影。
- 同 Root 换观察版本仍扣除已登记范围；新草稿不能重复准备该范围。
- 批量 prepare 保留原单一义务、换键复用原结果，冻结规则变版拒绝新的准备。
- 真实 HTTP 创建 201、PATCH 200、prepare 202、原操作查询；0.00000001 无损，遗漏必需 source.expiryAt 为 400。
- 既有同 cut 3+2→5、不同建议并发唯一登记、交叠批次复用原订单三项回归。

15 个 Vue 场景通过（7 个新人工入口、8 个既有补货建议入口），覆盖准确引用／小数、当前 revision、保存后准备、原请求持久重试、已提交恢复及刷新失败保留结果、历史分页、身份变化丢弃迟到响应和动作权限。应用 `vue-tsc --noEmit -p tsconfig.app.json` 退出 0。

| 原始记录 | 实际结果 |
| --- | --- |
| `D:/CP6-local-test/wave238/foundation/pg-draft-first-20261101T095644Z.json` | 11 项运行：10 通过、1 失败；失败测试误将未配置字段限制当作拒绝，现有合同默认允许，改成显式 Access=3 拒绝夹具。失败记录保留。 |
| `D:/CP6-local-test/wave238/foundation/pg-draft-review-20261101T100156Z.json` | 8 项定向回归通过：权限夹具修正、Root 预算、批量 prepare、并发编辑及相关建议／登记。 |
| `D:/CP6-local-test/wave238/foundation/pg-draft-http-final-20261101T100416Z.json` | 最终受影响实际 HTTP 场景 1 项通过。 |
| `D:/CP6-local-test/wave238/foundation/pg-draft-head-final-20261101T101037Z.json` | 移除缺失 head 时回退旧快照、复用已序列化字节计算命令摘要后，历史版本／原键恢复 2 项通过。 |
| `D:/CP6-local-test/wave238-web-tests.log` | 2 个文件、15 个场景通过。 |
| `D:/CP6-local-test/wave238-web-typecheck.log` | 类型检查退出 0。 |

汇总按每项最新适用结果去重为 14 项，不累计重复执行。未变输入的成功证据复用；没有把初次失败改称通过。执行者完成一次任务级 SPEC/QUALITY 集中审查，覆盖迁移／版本原件、目标与来源预算、真实事务回滚、当前权限、冻结规则、原操作及页面未知结果；审查修正只做受影响回归，没有额外代理或全仓重复门禁。Issue #663 的旧条件由原完整 Ref 相等比较定位，当前原复现步骤已实际通过；没有声称执行过旧代码红测。

## 剩余范围与收尾快照

人工 DRAFT、编辑／历史、prepare 的登记及持久等待已实现；实际 Transfer 准备与 SourceDemand 共享采用、唯一派发、完整 Root/Claim/容量最终事务、物理执行和停止、同 cut 已移／未到衔接、新订单列表分页／导出及旧任务族恢复仍未完成。本轮不关闭整个 REP、WP10-10、WP10-31 或 D2；七天目标保持 ACTIVE。

未运行全仓回归、浏览器布局检查、生产包构建或外部事件消费；迁移只在隔离测试库执行，没有改既有数据库、触发 Actions 或部署。工作流及相关依赖无变化，推送前复用已验证的相同手动事件内容并核对实际差异路径。

本报告为合并前快照。Main/Handoff 正常 PR、远端采用、准确分支清理、进程停止和官方 finish 按同轮完成证据核销，不提前宣称席位已释放。正式任务状态及 125 目标／247 验收未因这个子任务虚增完成。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/214)已完整采用并finish。Main交付快照为19.07分钟；同轮最终Main合并和finish结果单独核对。
