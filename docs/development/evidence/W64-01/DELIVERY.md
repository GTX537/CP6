# W64-01 / Issue433 字段审计当前Owner与安全历史

COORD本聊天单人，2026-10-23T11:37:31.163542+00:00。基线Main `b23e5e111d1f1f77936a76a74ff6acfb707bd067`，任务`BUG-433-FIELD-AUDIT-OWNER`，分支`codex/bug-433-field-audit-owner`，固定DEV-1。[Issue433](https://github.com/GTX537/CP6/issues/433)。本报告为一份产品交付；合并/分支与席位最终证明见本轮后续收尾段和PR。

## 问题与行为

原record接口仅凭sys-field-audit:query返回同tenant任意实体的原Changes JSON，绕过当前业务对象/字段权限。实际生产Program/登录原会话/PG/HTTP测试在修复前期望403却得到200，失败记录保留；修复后相同用例403。

新增FieldAuditReadOwnerV1，复用既有Machine OperationsOwnerV1原会话/tenant/当前query/真实对象范围及原生共同门，并在门内和提交前再次核当前日志query登记/角色权。首个显式映射Main.FieldAudit.Machine.Status.v1仅允许存活Machine的Status和ActiveFlg，且校验原值合法形状；无权/删除/外tenant/不存在对象统一404。未知实体/缺Owner/非PG详情503 DETAIL_UNAVAILABLE，不返回原文fallback。

只返回服务端生成changes数组、READABLE/RESTRICTED/UNREADABLE及projectionVersion；未知字段名称和值、备注及actor PII不进入详情。Changes原件不修改；原摘要/原证据与安全视图不混同。列表保留既有本tenant安全摘要及actor列，不宣称已完成全局PII政策；解析失败changeCount=null并标UNREADABLE，原空数组仍是0。

时间线按ChangedAt/Id原生顺序、同tenant同对象afterId定位，单页最多100；游标不含权限，逐页重新授权。记录原DateTime语义未迁移UTC；未把当前UI“浏览历史”变成恢复命令。当前Web/API在同一提交更新：详情changes由原JSON字符串改为安全数组、去除详情actor字段、增加游标和版本，旧未经Owner保护的详情合同不再开放。旧原文服务器响应在新页面也关闭，不保留解析后隐藏的替代通路。

页面区分不可用、不可解析、部分受限；支持下一页，拒绝过期身份/乱序结果，撤权失败清掉已读内容。四条五语词条通过既有seed登记，无生产配置或已有数据库迁移。

## 实际验证与审查

- `D:\CP6-local-test\wave64\foundation/field-audit-red-20261023T112835Z.json`：真实HTTP原缺口1失败，Expected Forbidden/Actual OK；不是编译桩。
- `D:\CP6-local-test\wave64\foundation/field-audit-first-20261023T113253Z.json`：27通过0失败0跳过；6项新PG/HTTP接缝、12项解析边界、8项原控制器相关检查，筛选同时命中1项既有Caller_transaction_keeps_prior_work_after_late_field_audit_failure并实际通过。相关Core/API/Tests构建成功，已有NU1902/NU1903/EF1002/nullable/xUnit警告保留，未顺手升级依赖。
- `D:\CP6-local-test\wave64\foundation/field-audit-ui-fixture-correction.json`：6项前端行为通过；首次field-audit-ui.json因测试替身未提供表格row插槽6失败，补ElTable/ElTableColumn替身后通过，产品代码未因该夹具错误修改，原失败保留。
- 覆盖实际登录HTTP只有日志权无业务权、受限字段与原件不变、两道当前撤权、服务直调无日志权、外tenant/自有范围/删除/不存在/错Context、同时间103行分页与跨对象/tenant游标、损坏及未知字段；前端身份清空/迟到/乱序/撤权/安全版式兼容。稳定页顺序原生PG验证通过。
- 一次作者完整SPEC/QUALITY审查：当前原会话与角色/tenant/业务字段映射、未知策略关闭、原生共同门复用、错误不泄原值、PII最小化、游标边界、原日志不改、UI过期响应、依赖及配置范围。无阻断项。原W47 Machine Owner共同门/可信部门映射源码未变，相关既有证据保留真实来源，不重复全套回归。
- 不做全仓回归、远程Actions、SQL Server新增验收、性能/浏览器E2E或生产验收；本地HTTP夹具不代表生产环境。没有新schema/迁移/依赖/权限授予、跨仓产品改动。

两个本轮隔离PG簇均stopped.txt存在、无postmaster.pid，数据/日志保留。原生产与用户数据库未改；无后台监听服务需保留。

## 剩余范围与收尾

这只关闭Issue433原文旁路并交付首个非PII安全历史映射。其他实体详情需明确Owner/字段/PII/删除历史政策才能开放；列表完整资源/PII策略、强制原子写入覆盖、导出、hold、引用图、保留清理仍待对应窗口。完整WP02-13、24/375整卡、125目标0交付、247 NOT_RUN不提升；七天目标ACTIVE继续。

原预算60–90min，实际最终按同任务start到finish计算，子任务不重复累计。候选审查通过，待正常PR采用及Handoff/台账最后收尾；不提前声称分支删除或席位释放。W51准确data删除曾被自动审批拒绝，原因仅blocked by policy；停止保留，无重试绕过。
