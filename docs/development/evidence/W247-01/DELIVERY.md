# W247 原D6运行链路模拟生产验收与出口核销

W247公开询盘原日历/时区、换版Attempt、NFC重放和实际后台到持久模拟消费者的生产业务验收通过；原D6限定出口核销。

CRM [PR163](https://github.com/GTX537/CP6.CRM/pull/163)源`4c77cdd34d29811fad35f479e00e04ff076e607f`→远端main`c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f`完整树采用。[产品报告：执行场景、失败记录、复现及模拟范围](https://github.com/GTX537/CP6.CRM/blob/4c77cdd34d29811fad35f479e00e04ff076e607f/docs/development/evidence/W247-02/DELIVERY.md)。实际环境是隔离本地PostgreSQL18、.NET8.0.30、Windows10.0.26200原生HTTP宿主；生产业务验收允许模拟输入，生产部署独立未执行。

## 原限定出口与实际证据

对照[原七天计划](../../planning/20261022-seven-day-delivery.md)、[W129原出口](../W129-01/DELIVERY.md)与[W135冻结适用核定](../W135-01/DELIVERY.md)，不把完整WP03或发布资格扩成原阶段前置。用户2026-10-29及本聊天再次明确：生产验收也可模拟业务数据/外部服务，因此旧报告中等待企业资料的条件不再阻塞。

|原D6出口|实际采用与验收范围|
|---|---|
|不可变配置Bundle、日历与生效选择|W105/114原配置页面、Save/Preview/Activate、DB时钟和原图已采用；本轮实际HTTP发布上海/纽约日历，保存规则指纹/合法0000节日，核对纽约DST两种转换。先发Attempt再激活UTC新版本，新GET用新Bundle，原Attempt保留原Bundle/期限且旧日历不改。|
|浏览器绑定、提交/受理与原回执|[W117](../W117-01/DELIVERY.md)、[W118](../W118-01/DELIVERY.md)原生HTTP、实际公开页面/BFF、完整原图/同事务/未知恢复；本轮201标量入口拒绝无效果，合法组合字符及补充平面文字实际解密为NFC，等价重放回原字节且不重复配额/Submission/Lead。|
|必要审计、同意与保留|W114/117原图、[W124](../W124-01/DELIVERY.md)实际60分钟保留边界、[W127](../W127-01/DELIVERY.md)当前同意撤回；[W135](../W135-01/DELIVERY.md)按闭合原合同核定LeadNo可未分配、没有附件输入，本期不另造NUM/FILE调用。完整提供者/隐私导出范围保留。|
|原消息、恢复与下游效果|[W133](../W133-01/DELIVERY.md)真实原生Outbox/原ID恢复，[W134](../W134-01/DELIVERY.md)Website审计；[W234](../W234-01/DELIVERY.md)四路原消息→持久模拟消费者及回滚/重启/重复/冲突/丢ACK；本轮实际后台服务自动投递、消费者Inbox和效果同事务且重复无新效果。|
|宿主、规范化与传播适用|[W246](../W246-01/DELIVERY.md)原D5实际宿主采用已闭合；本轮在实际.NET宿主执行上述NFC/规则及后台传播。1秒轮询本次保存到持久接收3396ms、消费3510ms，仅本地单次观察。原冻结正文没有约定p95/p99生产SLO，不新造指标资格；完整生产性能和运行身份仍独立。|

**H01合同范围明确保留**：原INQ V15正文第325行及当前有效schema的calendar.timeZone恒为Asia/Shanghai。上海自动转Lead通过；纽约日历用于NeedsReview且DST通过，不声称纽约自动转Lead可用。最初夹具误用ISO星期7及纽约H01导致失败；诊断到原const后按合同纠正，失败保留、不删除、不算通过。任意时区H01要另行修改合同版本，不能靠允许模拟绕过。

W105/114/117/118/124/127/133/134/135/143/234/246最终proof为PASS，相关Main/CRM采用提交均核对为当前主线祖先，记录wave247-exit-audit.json。CRM src/contracts/依赖自W143保持相同；本轮只有测试/文档，已有适用成功输入复用，不把旧结果称本次运行。通过本轮实际补项及上述映射，原D6在正常合并finish后DONE。

## 验证、耗时和收尾

6 distinct effective checks PASS, zero skipped: 3 new simulated production-acceptance scenarios and 3 existing submission regressions. Initial acceptance-20261101T191623Z contributes 4 unchanged successful cases; acceptance-corrected-20261101T191859Z contributes 2 corrected calendar scenarios. Initial fixture failures and diagnostic run preserved. Actual PG18 / .NET8.0.30 / Windows10.0.26200 native HTTP; no Actions or deployment.

One complete author SPEC/QUALITY review: original D6/W129/W135 applicability, exact H01 calendar constant, actual HTTP/calendar/NFC/original binding/DB effects, hosted dispatch versus simulated consumer, failure records and correct reuse, documentation links and unchanged formal task/target/acceptance states.

PG55477确认自有身份且无客户端后smart停止，HTTP进程退出；原数据/失败日志/TRX/缓存保留。工作流/依赖未变，复用相同手动触发配置分析，Actions运行/取消/触发变更均无。未迁移或替换既有环境，未部署生产。

预估实际35–65分钟；登记至此16.05分钟，最终Main START至finish计一次，不叠加跨仓席位。原D1/D3/D4/D5/D6限定出口闭合，D2/D7继续。5/7只是阶段数，不是工程完成百分比；不以此外推工时。H01仍按冻结合同仅Asia/Shanghai，完整WP03/隐私导出及运行/发布资格独立未完成；ops身份Unproven与正式ready不提升，生产部署未执行。

CRM已合并清理准确分支并finish；Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/223)已完整采用、准确分支清理并释放DEV-2。Main待正常PR与finish，提交前快照由同轮wave247-final-verification.json核销。固定工作目录和用户文件保留。

下一步：核对原D2补货/退货剩余出口和直接前置，实施实际缺项；缺失业务资料及外部服务直接模拟。随后完成D7本期相关跨仓/页面最终集成和总收尾，不扩展全部125目标。
