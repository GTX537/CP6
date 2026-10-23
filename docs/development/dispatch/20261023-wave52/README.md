# W52 / 七天一期 D5：CRM 完整身份基线采集与 PostgreSQL 原始证据

COORD本聊天单人，2026-10-23T01:38:44.627454+00:00。Main `4f3f874fb18183b8bc1a432e981518e377fbabb8`，CRM `48be253dfe3c4b451a9be7eaf9b7ef5f1199086a`。W51 CRM PR96、Handoff27、Main418已采用，准确任务分支删除，固定工作区干净detached、全部席位finish。登记开工到收尾25.51min，前置核对另约8.36min；14项相关PG/.NET验证，保留失败/修复记录。W37–W51不重复，七天目标ACTIVE。

## 前置与本窗范围

已核最新AGENTS/POLICY、台账、WP02-06/09直接前置、INT-ID完整基线要求、Main W21/W22实际原始HTTP/collector交付及当前源码、CRM W51实际PG会话/撤权实现。容量无告警，仍只复用两个开发席位。Main/WP02-05完整任务尚未交付；使用已验证的限定原始目录来源，不提升完整卡。

CRM消费固定 `cp6-identity-baseline-v1`：首份完整目录、每项原始bytes快照、二次完整目录必须同源同边界同集合，保留精确原始bytes/hash以及SOURCE_SNAPSHOT来源，使用配置可信source向量，不从响应自举信任。不伪造收到的CloudEvent/messageId/RevokedAtOrigin。

PG版本化原始基线历史在显式调用方事务内原子保存，约束不可改写/不半套提交；保存来源、边界、目录摘要、原始每项bytes，重复内容保持原证据。撤权快照可同事务写入W51真实撤权表及会话失效；缺少原始来源时不能凭空造事件。新基线只是可追溯输入，不设Ready、不产生Allow、不覆盖旧Published/完整投影。来源配置改变需明确重新准入，不能默默换源。

本窗不宣称完整CRM Published/PreCheckpoint/all-writer最终锁、0.10.3 Inbox采用或业务Owner共同事务已完成；不将新组件接成生产授权旁路。后续按D5继续这些前置。保留历史SQL实现，不改已有环境/数据库，不触发Actions或生产部署。

## 实际预算与顺序

预计实际90–150min：CRM严格原始协议消费及PG证据35–55；验证故障/回滚/同事务撤权25–45；实际Main HTTP→CRM PG跨进程验证与一次作者SPEC/QUALITY、三仓交付30–50。按实际记录修正，不等待日历。

DEV-1 Main `codex/w52-coord`负责台账和必要跨仓集成测试；DEV-2 Handoff `codex/w52-handoff` START可靠保存后finish deferred，bindCRM `codex/w52-01`实现/合并/finish，随后bind回Handoff resume原分支交付/合并/finish，Main最后交付收尾。集成席位不作实现，不创建额外树。

允许范围：CRM Infrastructure/Identity/Baselines、PostgreSql、Contracts定向快照验证、必要项目资源、相关PG测试、docs/状态；Main身份目录跨仓集成测试及docs/台账/生成页/四状态；Handoff任务消息。独占CRM身份/PGschema，Main测试PG55435、CRM隔离PG55437。验证本地相关行为，保留原始失败，单份CRM交付报告引用实际Main兼容证据；完整125目标/247验收不提升。

W51精确测试data目录删除曾被自动审批拒绝，工具仅给出blocked by policy；其76,150,437bytes已停止并保留，不重试绕过。不会影响本窗席位与独立新夹具。

## 实际前置回归：Issue419

真实Main原始目录安装被最新Owner/Outbox固定函数阻断，两个本地运行复现，原目录安全检查失败关闭。按BUG规则先保存W52总控分支并finish deferred复用DEV-1，从当前最新main建立独立BUG419分支，最小修复/原生回归/采用后释放，再resume本总控分支继续CRM实际联通；CRM DEV-2保留原任务，不新开席位。新增成本预计15–30min。临时仅异常类型/栈诊断已移除，失败日志保留，不在此窗口降低检查。
