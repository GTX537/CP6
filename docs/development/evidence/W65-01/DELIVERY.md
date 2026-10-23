# W65-01 / Issue436 PostgreSQL Pub采番串行推进

COORD本聊天单人，2026-10-23T12:08:36.078824+00:00。Main基线`4b129632448409a6bcd713b295cf87b7a5b972e3`；固定DEV-1任务`BUG-436-PUB-SEQ-ATOMIC`、分支`codex/bug-436-pub-seq-atomic`。[Issue436](https://github.com/GTX537/CP6/issues/436)。本报告是产品交付唯一报告；最终采用和生命周期追加在本报告/PR。

## 缺陷、实现和边界

原SeqService优先Local跟踪值，在两个上下文已加载CurrentValue=0时两次返回PO0001。真实PostgreSQL同库两个Context红灯已证实，修复后第二个返回PO0002。

现有Program的ISeqService注册直接采用修复，无新注册或未接线候选类。只在PostgreSQL路径以原TenantId+BizKey域锁定当前规则：FOR UPDATE、AsNoTracking重新读取、计算原period/格式/next，再只更新CurrentPeriod/CurrentValue/ModifyDate。不用SaveChanges替调用方落下无关待写。原bpchar大小写/尾空格合同通过参数绑定及明确bpchar类型保留；未引入SQL Server方言。

自有ReadCommitted事务成功提交才返回；调用方已有事务时建立本次保存点、保留调用方最终提交权，数据库异常回滚本次保存点，先前工作可继续。原接口没有operation身份，事务内返回值提交前仍为暂定值；回滚可恢复计数器，不能宣称已经满足外露号不回收或UNKNOWN恢复。ISeqService注释已明确此边界。

同规则脏跟踪行拒绝SEQUENCE_STATE_DIRTY，不覆盖调用者未保存意图。干净旧行从真实规则刷新并恢复Unchanged状态，后续同上下文采番不再误判为脏。外租户跟踪行不参与当前租户分配。

保留原DateTime.Now/显式时间、文化/日期格式、日/月/年/其他周期和最小补零宽度；格式失败在更新前RULE_INVALID，负计数器拒绝、Int32耗尽SEQUENCE_EXHAUSTED不回绕，跨周期按原规则重置。宽度4允许10000等旧合法号码，不虚构新容量限制。序列化只保护当前规则的推进，不承诺规则修改、跨周期相同格式、时钟回退或历史重置后的全生命周期显示号唯一。

原SeqController、Common/DocNumber、WMS/MES/FIN各自编号、采购调用者和CodeGen未改。采购PR/PO/RFQ/GR/TWM及PrGeneration和泛型BaseCrud的现有ISeqService入口继续消费同实现；没有替它们编造业务operation或新Owner事务。无schema/迁移/依赖变化。

## 验证和集中审查

- `D:\CP6-local-test\wave65\foundation/seq-red-20261023T120034Z.json`：原生PG复现1失败，Expected PO0002 / Actual PO0001，记录保留。
- `D:\CP6-local-test\wave65\foundation/seq-first-20261023T120454Z.json`：12项中11通过、1失败。发现刷新Current/OriginalValues仍保留Modified标记，导致同上下文二次分配SEQUENCE_STATE_DIRTY；这是产品缺陷，已修复，并保留失败。
- `D:\CP6-local-test\wave65\foundation/seq-tracking-fix-20261023T120618Z.json`：8项受影响原生PG用例全部通过，0失败0跳过。上轮4项原SeqServiceTests仍适用于未变的非PG历史路径，真实复用，不宣称再次运行。合计12项独立用例成功。
- 原生用例覆盖两预载Context；12并发独立连接及原native大小写/尾空格规则；同BizKey不同tenant及外tenant跟踪；缺规则；日/月/年/其他周期；当前规则编辑替代旧跟踪配置；调用方commit/rollback；不提交待写业务、脏/已删跟踪拒绝；非法日期格式/负计数/Int32边界；真实CHECK约束失败后的保存点回滚、保留先前工作并继续提交。测试仅在新隔离PG夹具中临时加约束并finally删除，不改现有数据库。
- 相关Core/API/Tests构建通过；保留已有NU1902/NU1903/nullable等警告，无无关升级。一次作者完整SPEC/QUALITY检查SQL参数/唯一域/租户范围、锁和事务持有期、格式/溢出、跟踪/保存点、调用者兼容及全任务diff；发现的跟踪状态问题已定向复验。无剩余阻断项。
- 本地服务及实际迁移schema验证，不是HTTP/真实业务Owner或生产验收。无需页面、全仓回归、其他allocator或SQL Server新增验证。三个本轮隔离PG均停止、无PID，数据/日志保留；无Actions、迁移既有库、授予或部署。

## 剩余范围和收尾

本窗只关闭Issue436的旧计数重复分配及相连的持久化边界。原命令receipt/同键恢复、业务Owner接入、规则CAS/历史冲突/删除停用、单writer切换、外露号不可回收和灾后恢复仍需后续实施；完整WP02-16 PLANNED/NOT_CHECKED、WP02-10完整前置、24/375整卡、125目标0交付及247 NOT_RUN不提升。

原估50–80min，实际以同任务start到finish计算，子任务不重复累计。候选通过，待Main/Handoff正常采用与最终台账、准确分支删除和finish；不提前宣布席位释放。七天目标ACTIVE继续。W51准确data删除曾被自动审批拒绝，原因blocked by policy；停止保留，无重试绕过。

## 实际采用与最后收尾

Main [PR437](https://github.com/GTX537/CP6/pull/437) source `d721ee65118084b798d66184c3e2ae00e209fdaf`采用远端main `9d40f65cfc332e1543b4c0d762dcf7807042c99b`，全部13路径exact核对通过。Handoff [PR41](https://github.com/GTX537/CP6.AI-Handoff/pull/41)采用 `8e4e41a9b84701059644463e0ec6fe28a6c8d743`，准确任务分支删除、固定树干净detached、finish释放。三个产品路径与已测提交逐blob相同，未重复成功验证；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue436在最终远端和生命周期核对后关闭。当前约35.39min，原估50–80min，最终以finish为准；前置只读核对约4min，子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave65-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续真实Owner前置和D6公共支撑。
