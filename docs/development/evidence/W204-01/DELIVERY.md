# W204-01 库存身份独立 Owner 与准确产品目录

COORD 单人。本轮对应 D2、WP04-33/WP10-03/WP10-10 的直接前置。W203 已完成，仅在本轮归档一次；未重复实现或重新清理它的分支。

## 交付行为

新增独立库存身份页面、API 与 PostgreSQL 原件。PARTY 明确区分自有法律主体与真实 ERP 客户；PURPOSE 保留版本化原始用途；PRODUCT 组合准确产品、单位绑定/基本单位/精度、已确认工程映射、货权和用途。KNOWN 指向真实冻结工程基线，NOT_APPLICABLE 必须有明确依据和实际 ERP/PLM 的独立确认动作。

内容变化形成 DRAFT，同内容 SAVE 不换版本但保存原操作。独立确认、停用和历史版本分开；当前来源变化、停用、字段/对象权限或确认人资格撤销阻止新选择，不能由历史原件获得额外权限。自有资料是经过系统内独立确认的配置，不宣称外部登记机关或资产会计已核验。

菜单与动作不自动授权。本人及部门2/3/4/全部范围、新建准确绑定、同事务最终授权、并发版本比较、未知提交查询/关闭、浏览器 Web Locks 和最小 ID 恢复均沿用实际 Main 权威。数据库校验来源原件实际存在、版本不可变、head 单调推进及回执同事务保存。未压缩原生冻结基线合法大原件上限。

KNOWN 读取/确认仍需满足原 PLM 冻结基线、项目成员及评审原件权限；不会自动为仓库用户增加项目成员。正式业务的最小选项投影及消费权威留待后续 Transfer 采用。

目录输出原有 ProductOption 结构，但明确 `REQUIRES_FINAL_OWNER_GATES`；不产生实际 Stock 变动，也不把本入口称为完整 Transfer T02 或 T04。

## 验证与审查

14 PG18/Program HTTP; 60 Vue;types/build PASS;0skip。实际证据：`D:/CP6-local-test/wave204/foundation/final-validation.json`，包含逐项真实运行来源、相关源码/测试 blob、失败保留和准确复用说明。一次作者集中 SPEC/QUALITY 审查保存于 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave204-main-review.json`。

覆盖 SELF/CUSTOMER/用途、独立确认、产品 N/A 与真实 KNOWN 基线、比例单位按基本数量精度表达合法的 0.5 换算结果、精确来源与防伪/UNKNOWN、客户字段隐藏、当前撤权、租户/环境模式、部门2/3/4、CAS/关闭竞争、丢失提交回执、数据库保护、嵌套来源最终撤权回滚、实际 Program HTTP/CSRF，以及页面准确选择、无默认量产/技术状态、未知操作恢复、互斥与身份变化清理。失败日志保留，只有后续实际通过的相应结果才替代失败；不以用例数量作为业务验收目标。

普通 Actions 未触发，工作流/依赖内容未变；沿用已确认的手动入口策略。本轮无需要取消的归属运行。既有环境、生产部署和真实企业业务验收均未执行。

## 实际用时与收尾快照

预计90–170分钟。写入本报告时，自官方 Main START 已过 55.31 分钟；最终 START→finish 由 `wave204-final-verification.json` 记录，不叠加 Handoff 时间。

Handoff：https://github.com/GTX537/CP6.AI-Handoff/pull/180；任务来源 `0e522f72860c6d96dd4fdec85204b6a929e574d5` 已由目标 `fc0ce33cbb62f15fb76d11850662cb2abbc2cc26` 采用，分支已准确删除，DEV-2 已通过官方 finish 释放。

Main：本报告是自身合并前的真实快照，当前处于正常 PR/merge/finish 收尾窗口；不在未发生前写为已释放。合并后由同轮外部审计核对远端 main、准确分支删除、DEV-1 释放、四仓2+1配额、停止本轮进程及缓存保留。下一轮只归档本轮有限范围一次。

## 剩余出口

正式 Transfer 可信上下文与 MASTER/产品选项采用、T04 草稿预算的正式 Owner/共同事务采用、Stock 与实际位置/预留/数量效果继续实施；N/A 不是缺字段兜底，SELF 不是 TenantId 替身，原始用途不是当前生产许可。本轮没有关闭完整 WP04-33/WP10-03/WP10-10。

原七天目标保持 ACTIVE：D1/D4 DONE，D2/D3/D5/D6/D7 PARTIAL；125目标、247正式验收及原任务状态未改写。不能把阶段数量、局部测试通过、日历或本轮数当成整体完成百分比。
