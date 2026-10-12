# 全量任务拆分：独立任务级审查

结论：最终规划与台账工具范围可交付；本轮发现的问题已修正，未发现尚未解决的实质阻塞。此结论不代表业务实现、数据库运行验收、生产候选或远端合并已完成。正常保护PR和远端main包含性由协调对话继续完成。

审查对象：D:/CP6-worktrees/full-task-breakdown-1011，相对已确认Main `312cde1577e073fa7ce0d317431edabf5d8dd18b` 的全部本轮变更，含未跟踪定义、14份生成任务卡、协作文档、计划、工具、四份项目状态和证据文件。先阅读AGENTS及DevOps/R2边界；按一次完整任务集中审查，修正后只复核受影响处。

最终输入身份：

- roadmap.json SHA-256：`6fc783374ae3631d7c80b00fe696a89dd5e132ae0e18544f873d5106f6d95138`
- task-definitions.json SHA-256：`91508cd5c506de5fd3e083fe2d1897f5e758b0efe09dd6da1177cb8ddc49d693`
- manage_roadmap.py SHA-256：`992e88abcaefd904ac9c0b555405df443f0e521170998237e3ca2f81c917545b`
- test_manage_roadmap.py SHA-256：`047354df0dca17afa25b2472580c57f58ddd3b4b0f394ee0534fde4ab9871a6c`

四个当前文件哈希均已与仓库 `docs/development/evidence/full-breakdown/validation-inputs.json` 独立核对一致。

## 已解决问题

1. **工具测试与新防绕过门冲突。** 旧 `test_legacy_policy_without_definition_path_keeps_pure_validation` 在仍有WP任务时删definition_path却期待成功；指定Python单项执行实际失败。现拆清helper无操作与整体WP必须拒绝两层语义，保留防绕过门。复用准确修正日志 `review-fixes-green.txt` 与最终62项回归。
2. **同一工作树的路径别名漏检。** 两活动任务分别登记 `D:/CP6-worktrees/claim-a` 与 `D:/CP6-worktrees/claim-b/../claim-a`，旧校验返回空错误。现worktree先 `Path.resolve()` 再比较大小写/分隔符，回归已覆盖；未要求目录预先存在。原RED与GREEN保留于 `worktree-alias-red.txt` / `review-fixes-green.txt`。
3. **WP01-02首轮派工前置矛盾。** 原卡要登记首轮任务，但同时要求四项首轮任务已DONE。现明确是四任务交付后的汇总及下一轮候选，首次领取属于协调常驻职责，不以本卡为前置。
4. **WP07-08把AP建草稿写作原义务。** 正例已改为一次真实Draft/CreateDraftResult；OriginalObligation必须由WP11-19真实过账应用证明，补充反例拒绝用ACK、匹配或草稿状态替代原义务。
5. **明确内部公共能力未进入DAG。** 逐卡对照external_prerequisites并计算传递祖先后，补齐CRM、OA、SPACE和GL所需NUM/FILE/PRIV/JOB硬依赖及TASK_DELIVERY绑定；最终同一检查无遗漏。CRM相关卡明确本仓真实版本适配、身份/当前输出门及禁止跨库直写，Main提供者完成不能冒CRM已经采用。

审查早期首次以包模块路径启动单测遇到 `ModuleNotFoundError: manage_roadmap`，是测试启动目录错误；改从 `docs/development/tools` 运行后才取得上述真实断言失败。未将该导入失败或草稿通过冒称最终检查。

## 最终覆盖、依赖与状态

- 共375任务：保留原13项全部既有字段，新增362项；356 CODE、10 DESIGN/协调、9 VALIDATION。全部PLANNED、assignment为空、entry_gate为NOT_CHECKED，无虚构任务验证或交付证据。
- 125目标均有其所属工作包的主体CODE任务，基础、设计和验证不替代主体；目标完成仍要求全部主体任务、完整原验收、必要修订场景及真实交付证据。
- 对基线逐结构比较：work_packages、targets、revision_acceptances、sources、baseline_totals完全未变。原13,404目标—验收关联、123/125完整原目录、247 NOT_RUN、41 REFERENCE_ONLY保持；ITER/REL及WMS-DEL历史限制未被规划修复或改称完整。
- 375节点、3,554条直接依赖全部可拓扑排序，无悬空引用或循环。每张新卡包含其目标准确design_module、revision_outputs及原验收来源的规范引用。
- 工程与MES通过RES资源目录、QA独立规格、WMS原UOM/实例、MES原执行意图等先行提供者分开；ROU→RES及冻结→QA等原相互等待没有以删除能力解环。
- 首次工程/试制许可WP05-32与专业后验正式许可WP05-42、MES正式采用WP08-18及Sales正式Release采用WP04-37分开。目标主体责任仍包含后验消费者，初次TRIAL不能冒正式生产。
- MATCH14→AP18/66→MATCH08，以及独立correction-close→MATCH09保留真实来源/原义务预算和全腿关闭责任。
- QA12/RMA22真实物理源→INV63/44准确H30效果→QA09/RMA23最终关闭，保真实物理与财务事实先后；没有要求关闭结果反过来生成原物理来源。
- 售后newDemand由WP04-35独立采用设计、WP04-36真实producer和WP04-30消费者完成，不扩成未接受的direct PLAN API。若设计涉及另一仓实现，卡要求新增单仓实现任务及硬绑定，不能藏作外部前置后宣称完成。
- WMS/SPACE完整Target提供者和采用者、停止生命周期及源控制分责；原Owner仍持映射/库存/控制写权。条件门只决定具体实例必需证据，不免除目标的完整实现责任。
- 领取是单协调者快照登记；依赖/准入/活动claim与资源互斥/任务DONE证据及生成漂移均有工具门。工具不宣称分布式锁或外部证据认证。
- PostgreSQL为后续唯一数据库目标。schema租户布局仍先经S02-00，未自动采用全库一租户一schema，未删除TenantId、缩窄合法精度或迁移既有数据。
- 四份memory均同步375项及当前真实状态。变更没有业务代码、数据库迁移或workflow修改；无Actions、业务编译或生产部署执行。

## 验证证据与适用范围

本审查独立执行 `review_final_structure.py`，输出 `review-final-structure.json`：PASS。核对375任务/125主体覆盖、DAG、不可变基线、逐卡准确目标来源、19生成视图逐字一致、28变更Markdown中的8,999本地链接及任务锚点。该工具写在审查scratch，仅以最终文件为输入，不修改仓库。

已独立执行 `git -c core.longpaths=true diff --check`，退出0。

复用并读取本次最终 `tools-final.txt`：指定Python 3.13执行62项工具测试，37.294秒，全部OK；四项输入哈希与当前文件一致。未重复运行同输入全套测试。读取 `plan-validation.json`：375卡、125主体覆盖、358已存在源码入口及320拟新增路径、固定Main/CRM/Platform版本核对PASS；该报告8533链接统计使用其自身范围，与本审查计数规则不同，均不是业务场景数量。

原目录、历史归档、S01选定来源和更早源码评估按未变范围复用原证据；没有把旧Main 5ca评估重新标成312c业务审计。卡片正反例是派工摘要，完整合同/原AC/附加断言/修订场景仍为实施门。后续任务需按届时最新主线细化文件与命令、取得真实外部服务/资金/OEM/AI证据并进行实际Owner采用验收；此规划审查没有执行这些业务验证。
