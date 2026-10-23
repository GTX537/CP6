# W64 / 七天一期公共基础：字段审计详情当前Owner门及首个设备安全历史映射

本聊天COORD单人，真实开工2026-10-23T11:20:34.497204+00:00。Main基线 `b23e5e111d1f1f77936a76a74ff6acfb707bd067`；CRM `c7662eb196e72ddc8cab6d6c05490b6a310ca172`、Platform `a89e90eb4f078f37a6f8610882079073185fbcf8` 不写入。W63 Main432/CRM108/Handoff39全部采用，准确分支删除、固定树干净detached、席位释放，实际20.18min、前置约4min。W37–W63不重复，七天目标ACTIVE。

## 核对与准入

最新AGENTS/POLICY哈希未变，容量无告警；Main/Handoff无开放重复PR。已完整读取SYS-AUD-01归并正文和c9943c2bdead2009原文、当前三个日志控制器、字段审计写入、前端、原测试及已有Machine原会话Owner。

[Issue433](https://github.com/GTX537/CP6/issues/433)记录P0：日志query单独放出原Changes，未核业务对象/字段；解析失败同时冒充无变化。本窗优先修此现存安全缺口，属于WP02-13独立读取前置；直接前置WP02-10原操作仅部分采用，本窗不做新强制写入/清理，不关闭完整WP02-13准入或验收。

## 实施边界

- 所有record请求不再返回原Changes fallback。未知entity历史映射/Owner缺失以503 DETAIL_UNAVAILABLE关闭详情，保留既有列表安全摘要。
- 首个版本Main.FieldAudit.Machine.Status.v1仅映射Machine.Status和ActiveFlg；这些是现有Machine query可读的非PII状态值。其他字段名称/值均不输出，显式RESTRICTED；不自动扩展到用户名、备注、业务价格、删除对象或其他实体。
- 使用现有OperationsOwnerV1真实PostgreSQL原会话/租户/当前Machine query/可信部门范围共同门，额外在同一事务核sys-field-audit:query的当前登记和角色授权。用原UUID定位当前存活Machine；无权、删除、外租户及不存在统一不可见，不借日志菜单扩大业务范围。不接受payload Actor或角色/tenant。
- 严格解析原PascalCase及历史lowercase三字段结构；重复/未知成员、非法形状、不符合映射的状态值标UNREADABLE。列表解析失败changeCount=null，不变造零；原日志不修改、不重算原证据。返回服务端生成安全changes数组和projectionVersion，不返回原JSON、原PII或可还原原值的下载链接。
- 有界时间线按ChangedAt/Id排序、基于本tenant本对象原行的afterId游标，每页最多100；无效游标不扩大查询。no-store。前端展示不可用/不可读/受限，支持下一页，身份变化清空数据，过期与乱序响应不再填回页面。
- 无新SQL表、迁移、依赖或权限授予；保留现有OperLog/SecurityLog/字段原件、技术清理与保留政策。原日志写入覆盖、强制审计、PII政策、导出、hold及其他Owner映射仍待后续窗口。

## 耗时与验证

预计本对话实际60–90min：服务端Owner/解析/分页25–35，页面及相关测试15–25，集中SPEC/QUALITY和合并收尾20–30；按真实finish记时间，子任务不重复累计。使用固定DEV-1 `codex/bug-433-field-audit-owner`、DEV-2 `codex/w64-handoff`，不新增树。

先以实际PG/HTTP复现日志query缺业务query红灯；再定向核当前撤权、tenant/对象/删除隔离、白名单值/未知字段/损坏数据、分页同时间排序、原件未变、缺Owner关闭。复用现有Machine Owner定向门的已采用机制，测试本次新增接缝，不重复全仓回归。PG新隔离簇端口55435、只本地构建/测试；相关前端行为测试，一次作者全diff SPEC/QUALITY。

一份交付报告；Main同一任务分支保存代码/测试/总台账/四状态，Handoff固定提交交换START/交付，正常PR合并、核远端原提交及文件、停止PG保留数据，准确分支删除及finish。完整24/375整卡、125目标0交付、247运行NOT_RUN不提升。无Actions、既有库迁移或部署。W51准确data删除自动审批拒绝原因仅blocked by policy，继续停止保留，无重试绕过。

## W64验证完成

27项.NET相关用例与6项前端行为用例通过，无跳过；实际PostgreSQL/HTTP红灯已转绿，相关Main构建通过；[单份产品报告](../../evidence/W64-01/DELIVERY.md)。原问题实际HTTP红灯保留。两个隔离PG停止、数据保留；进入Main/Handoff采用与准确分支/席位收尾。七天目标ACTIVE。

## 实际采用与最后收尾

Main [PR434](https://github.com/GTX537/CP6/pull/434) source `183750bc45f39ce81499a11411914d3cf46de3aa`采用远端main `a8a31bccdb57fb3a9a87da5aadb99be7b1673653`，全部22路径exact核对通过。Handoff [PR40](https://github.com/GTX537/CP6.AI-Handoff/pull/40)采用 `1e877baa5c3ff0523db882c50b89fbd87528e1d7`，准确任务分支删除、固定树干净detached、finish释放。产品12路径与已测试提交逐blob相同，未重复运行成功测试；最后文档diff单独检查，台账生成核对。

Main沿同一任务分支最后状态PR及finish待收尾；Issue433在远端和生命周期最终核对后关闭。当前登记实际约27.46min，预算60–90min，最终以finish为准；前置只读核对约6min，子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave64-final-verification.json`。无Actions、迁移或部署。七天持续目标ACTIVE，继续真实Owner前置和D6公共支撑。
