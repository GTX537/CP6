# W86-01：库位优化历史观察Owner与旧写入口围栏

Issue [477](https://github.com/GTX537/CP6/issues/477)，COORD本聊天单人。Main基线`a32182de95bf392d3c9e4705b84720cc39f62909`；任务`BUG-477-SLOTTING-OWNER`，固定DEV-1，Handoff固定DEV-2。开工23:14:47Z，预计35–55min＋独立前置2min，实际按finish，不重复Handoff或W85收尾重叠只读。[范围与前置](../../dispatch/20261023-wave86/README.md)。W37–W85不重复。

## 当前合同与实际交付

当前手册WMS-SLOT-01、准确ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md、WP10-28/29已核；本窗实读原件§1–7/15AC、共同A1–A7及手册/前置，未声称本轮全文读取其后JSON例。S-G01–05仍UNPROVEN。原实现Txn行数及PIK/RES建议、随机旧Mobile派单/只停Pending不能当R2固定cut/Proposal/Transfer/完整作业族采用。本窗是WP02-03独立当前权限与历史安全观察。

`SLOTTING-OWNER/1`连接当前原会话、独立query、原生权限/业务表围栏和最终重核。本人实际Creator、部门2–4真实slotting-plan UUID、可信租户及有效仓库/Wms仓区授权约束计划头；头只含方案身份、仓库、历史日期/天数与旧状态，不由仓区可见推整个历史快照可披露。过滤后真实计数，默认50/最多200和偏移溢出校验，AnalyzedAt/id稳定分页，203真实PG记录跨页完整读取。

历史不透明建议缺逐行source identity。原JSON与统计只在当前RecommendationsJson字段可读、Stock/Outbound/Mobile各自query有效、各scope5且源全部字段可读、该仓全区许可可证时返回；不把部分当前源集合当完整历史依据。权限不足仍可返回独立有权计划头，SnapshotReadable=false、SOURCE_DISCLOSURE_UNAVAILABLE、统计与原文null，绝不以0/空数组假装已知空。依赖不可达/最终权限失效整次失败。字段ApproverCd/Remarks单独当前MIN，隐藏内容不回传；数据原文不改变。该保守整仓规则仅适用旧不透明快照，未来逐行来源完整后在正式业务任务实现精确交集，不放宽为某角色名称可读。

RecommendationsJson保持原字符串、不浮点反序列化重写；数值词法9999999999999.99999999和1.00000001保留。JSON_ARRAY只说明根为数组而非业务schema/真实来源已验证；坏JSON或非数组为LEGACY_PARSE_ERROR并保原文，数据库null为NOT_RECORDED，不改成[]。StatisticsMethod=LEGACY_ROW_COUNT、Knowledge=UNKNOWN，历史Approved不等MOVED，Cancelled不等完整族关闭，任务号仅原引用。

PG旧Search/Get409 OWNER_READ_VERSION_REQUIRED；Analyze/Approve/Cancel在当前动作/实际可见对象门后503 SLOTTING_OWNER_ADOPTION_UNPROVEN，在序号/方案/任务/库存/状态效果前拒绝。未虚构持久operation，无自动重试。非PG历史原行为保留；新增开发只PostgreSQL。Controller Owner结果不被旧InvalidOperation catch转成400，具名新读与全部响应no-store。

原生迁移增加slotting-plan对象种类、真实同租户非删除UUID校验，保既有全部类型及不可变身份/部门约束；IAM查找/绑定/禁用、类型声明和实际选择器同步。迁移重放与新租户启动只登记422 query，不授角色。公共Owner/配置/迁移在本聊天串行。

页面只读历史查询/详情，服务端分页，原文通过文本插值展示，无v-html/JSON重写或新写操作。身份epoch、当前自身/三个源动作变化、筛选/分页、迟到/乱序/失败/卸载清理旧结果。详情只针对实际选中Id/方案号，核响应版本/身份/原文类型和披露状态；隐藏快照不得携非null统计/原文。明确实际移动待核与旧原文异常，不冒称现场浏览器/全业务验收。

## 实际验证和一次集中审查

19独立.NET用例（15原生PG、3既有非PG库位优化行为、1两租户启动种子）及22Vue用例通过，无跳过；相关构建和vue-tsc通过，去重41项。一次作者完整SPEC/QUALITY已覆盖当前会话/对象/来源/字段、原文精度、真实分页、UUID与直接IAM消费者、原生围栏、旧写零效果、真实HTTP和页面身份/乱序；无剩余阻断。

- `owner-red-20261023T231609Z.json`：3真实PG红灯，缺Owner时仍读取、分析和取消，全部实际无异常。
- `owner-native-20261023T231825Z.json`：编译失败，primary constructor参数不能按另一个实例字段访问，改为类内部判定属性。
- `owner-reviewed-20261023T231930Z.json`：编译失败，Controller结果过滤器未限定命名空间，已补准确类型。两次编译失败均未执行测试/启动PG，证据保留。
- `owner-final-20261023T232011Z.json`：18/19通过、无跳过；剩余tenant/self用例因连接127.0.0.1:55435时Windows报告套接字缓冲区不足而失败，未当业务通过。原夹具确认停止后检查`socket-resource-observation.json`，当时TIME_WAIT91、已建立175、约4GB空闲物理内存，未观察到持续资源耗尽，瞬时根因未证实；未关闭非本任务进程或更改系统配置。仅该用例使用同一已构建产品在新自有PG执行`owner-socket-regression-20261023T232150Z.json`，1/1通过。合并有效证据去重19项，所有实际失败保留。15原生PG覆盖真实来源scope/字段/仓区、坏/空/null原文保留、跨租户和删除、部门2–4/IAM启停、跨租户UUID23514、目录重放不授角色、最终重核失败及外层/预跟踪拒绝、真实Program/login/HTTP与旧写零效果。双连接实际pg_stat_activity观察旧UPDATE等待Lock，读取保同一旧状态，提交后才放行。另3非PG旧分析/批准/取消及1两租户种子。
- `slotting-ui.json`：22/22，含10实际历史页面用例与12IAM用例（新增库位优化真实UUID选择/保存）；`slotting-types-result.json`真实exit0。
- 3个实际自有PG串行55435，ownership/stopped齐、无PID，数据/日志/TRX/红灯保留。无全仓回归、Actions、既有库迁移、镜像或部署；已存在构建警告保留。

## 采用与剩余

正常Main产品PR→远端精确核对→Handoff DELIVERY/合并/finish→同任务Main最后状态PR/finish。最后证明前不提前声明分支删除或席位释放；实际耗时以生命周期和后继登记为准。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持；WP10-28/29、15AC、S-G01–05、固定cut真实事件、Proposal/批准、唯一Transfer派发/全Stage停止、完整库存财务及CRM真实Site/Form Owner/公开受理仍待相应任务。七天目标ACTIVE。W51指定停止data删除被自动审批blocked by policy拒绝，原地保留不重试。

## 实际采用与最后收尾

Main [PR478](https://github.com/GTX537/CP6/pull/478) source `593b54bdf494d7de949475804677dc694dee15e1`采用远端main `4246689a04513633ee4bd0cb5e29623bbbfb9722`，30任务路径逐blob核对；Handoff [PR62](https://github.com/GTX537/CP6.AI-Handoff/pull/62)采用`b20c534cad638b669272fd78fb804a86818dfb23`，准确分支删除、固定树干净detached、finish释放。20个产品路径与已测提交逐blob一致，复用有效结果，最后文档diff/台账生成单独检查。

Main同任务最终状态PR/finish及Issue477最后关闭待完成；当前约11.09min，原估35–55min＋独立前置2min，实际按finish不重复计Handoff。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave86-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续剩余独立Owner及D5/D6真实前置，正式WP10-28/29库位优化执行/Finance仍未采用。
