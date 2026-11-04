# W300-01 — 库位规则真实审批撤回交付

W300：库位规则实际Owner可信审批撤回、原提案共同控制版本/应用竞争、独立业务委派与BLOCKED明确接续、原命令和真实页面恢复完成本地验收；旧提案入口、原件和APPROVED_PENDING未激活边界保留。

## 行为与合同

- 复用原`cp6_location_rule_v1.proposals.revision/token`，PostgreSQL bigint以int8send无损编码为8字节审批控制版本，原token ETag继续有效；不另建平行控制头。新撤回原子推进原提案、写原提案journal、可信事实、Outbox和独立原操作journal。故障遗漏事实/Outbox时整体回滚。
- 独立review-withdraw/review-delegate动作；当前字段只读即可执行撤回，隐藏字段/跨租户/错误提交拒绝。委派属于准确业务送审，TASK委派不替代业务grant；保真实实际人、委托人、精确grant版本和最终权限。权限撤销后仍可按当前读取权限查询本人原结果；UNKNOWN按原键恢复，明确close阻止原键重用。
- 原Apply与撤回共用真实控制。先APPLIED时新撤回返回原APPLIED冲突，不能改写审批；先撤回时迟到批准只产生STALE。审批成功仍只是APPROVED_PENDING，不能激活规则或改变库存。原RETURNED编辑/再送审和旧提案撤回的合法范围保留。
- 旧提案入口撤回不自动生成审批事实；对已WITHDRAWN而无可信事实的准确原提交，当前人须新明确提交，真实增加revision和事实。原事实BLOCKED时另发新命令显式接续，保原字节/原日志；新grant不替旧事实追溯换身份。已经接续的旧事实不能重新应用。
- 支持受理前业务撤回，保持canonical原提交；未启动时明确BLOCKED。运输先查询原命令，412仅刷新Workflow版本，丢答保持原body/实际人；RETURNED合法，未知状态拒绝。审批系统WITHDRAWN、TERMINAL_CONFLICT与本地提案WITHDRAWN分开显示。
- 原库位规则审批详情增加撤回/业务委派/再授权与原操作恢复。缓存仅保存环境/EvidenceMode/租户/实际人隔离的操作身份；不保存原因明文，存储失败不发送，切换身份不显示迟到私有结果。准确轮次、409已应用冲突和原入口已撤回但尚未同步有明确提示。
- 三项PostgreSQL前向迁移仅在隔离本地PG18执行。测试恢复真实前驱transition函数后前向采用，保既有提案revision/token、原件、原送审字节、APPLIED与待审批结果；GET不造历史，原子约束缺失立即不可用。没有改动既有环境。

## 实际验证与审查

14项独立PostgreSQL18场景通过，含Main认证HTTP、真实Chrome和3项旧库位审批/提案竞争回归；18项受影响Vue/API行为测试、vue-tsc通过。相同未变输入直接复用，最终回归覆盖存储回执读取的内部契约精简。

单一索引`D:/CP6-local-test/wave300/foundation/final-validation.json`记录每项实际JSON/TRX来源与失败保留。首轮HTTP404是红灯；遗漏事实/Outbox曾留下成功日志，新增独立撤回日志延迟约束后通过。前端首轮断言误把合法授权表单算作撤回表单，改为实际撤回入口检查。浏览器两次登录后回登录页由模拟菜单误指不存在路径造成，改为现有库位菜单后复测。所有失败保留，不以重跑次数或测试数量替代业务范围。

真实Chrome证据`D:/CP6-local-test/wave300/permission-consumption-rule-withdraw-review-final-20261104T031712Z/location-rule-withdrawal-browser/result.json`：Main Program真实登录、Vue、PG18、业务Owner授权/撤回；在真实成功提交后只丢弃响应，刷新查询同一原操作。数据库核对原BLOCKED事实与新接续事实、实际Actor/Requester/grant、原提案WITHDRAWN及无库存变动。已查看恢复截图。模拟范围为租户、用户、库位规则、政策、角色、日历/字典和服务注册；内部Owner未模拟。业务验收与生产部署分别记录。

COORD单人一次集中SPEC/QUALITY审查：真实提案revision/token共享控制、旧合法入口兼容、独立动作/当前字段/租户/实际人与grant最终检查、不可变事实/Outbox/两份原日志原子性、前向迁移、迟到决定与原结果恢复、实际页面业务状态。审查补齐HTTP409已应用结果的恢复适配；运输回执在外部响应边界验证一次，存储后读取直接信任内部不可变合同。无未处置的本窗阻断项。

## 剩余与交付

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标0闭环及247正式NOT_RUN保持真实范围。WP12-19待W301逐项整卡核销，本窗不提前关闭原卡；规则激活/库存效果继续属于原业务任务。

本轮CP6-24H-20261104仍ACTIVE，目标约24有效小时，本交付采样累计10676秒；W300起点8818秒，最终工时以finish后证明为准。下一W301核销原审批终止卡，再推进隐藏字段受控处置及后续原范围；累计14400秒重新校准。不按短窗口冒称三天完成。

提交报告时Main/Handoff远端采用、准确分支清理和官方finish尚待完成。最终分支处置、工作区干净及席位释放证明：`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave300-final-verification.json`。没有触发/取消Actions、变更workflow/依赖或生产部署。
