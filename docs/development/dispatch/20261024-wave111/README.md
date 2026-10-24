# W111 原日历和Intake完整图采用

COORD本聊天单人，七天目标ACTIVE，PostgreSQL唯一新增数据库。Main `01b18a9b18a4660dee3c866183b8be677af37d98`、CRM `6c181d249745a9fb1b77aaef3d19da931ab7f4ab`、Handoff `c0af8315db75e64024919b15b43ba56f9b43154e` 已核对。W110 Main522/CRM137/Handoff86已完整finish、准确分支删除、全部席位释放，实际20.78min/75项相关检查，不重复。

## 本轮实际前置与范围

真实配置Owner已保存不可变Bundle、完整发布日历快照和PreparedIntake；W110已将原表单身份绑定到原crm_v1图。本轮把实际原Calendar和Intake及period/holiday子行采用到完整原图，保存原发布事实、身份和源配置。不能用假日历、简化Submission或改写版本1替代真实来源。

配置生效时刻独立于日历发布时刻，允许原合同的任意小数精度、year0000和闰秒；假期同样允许year0000。新增明确PGv3表示保存原文本，旧DateTimeOffset/DateOnly访问对不可表达值明确失败，不静默舍入。旧PGv1/v2保持，新版本仅新库安装；不迁移/切换既有环境。

采用只复制已验证的原快照并建立不可变身份关系，缺失或冲突拒绝。原日历保存真实发布人/规则/审批/时间，Intake保留源Draft及原生完整字段/子行，不把存储采用伪装成配置激活。原Bundle和生命周期仍权威；完整源比较、租户约束、原行锁/不可变关系与调用方回滚共同验证。身份关系不授权H01、消费或回执。

## 任务与实际执行时间估计

|任务|交付|预计执行|
|---|---|---|
|W111-02 CRM|原Calendar/Intake完整采用，精确时间/假期，源比较、事务和相关PG回归|25–45分钟|
|W111-01 Main|前置、台账与四状态同步|5–10分钟|
|W111-HANDOFF及收尾|一次审查、三仓正常PR、准确分支清理/finish|10–15分钟|

合计40–70分钟；Main START `2026-10-24T12:49:55.151581+00:00`，最终按finish墙钟计一次。复用DEV1/DEV2、外部构建缓存和自有PG55446/D:/CP6-local-test/wave111，不新增工作树；无Actions、既有环境迁移或部署。

- [x] 原完整Calendar/Intake图的源事实采用与无损时间/假期。
- [x] 实际源内容/租户/冲突/回滚及局部兼容验证。
- [ ] 一次任务审查、三仓合并、远端采用、分支清理与席位释放。

完整授权/事务宿主采用、ConsentFact/ReceiptProof/消费重放、service/H01最终授权、编号及submit/receipt/page仍待；正式24/375、0/125、247NOT_RUN不变。

## 实际交付与生命周期

CRM [PR138](https://github.com/GTX537/CP6.CRM/pull/138)已采用 `0323e3760c6638b48e5b0990968a574ceeb948aa`，来源 `8e5157562203c2c9986786b148a12854a3b1587d`；[单份交付报告](https://github.com/GTX537/CP6.CRM/blob/0323e3760c6638b48e5b0990968a574ceeb948aa/docs/development/evidence/W111-02/DELIVERY.md)。

显式新库PGv3将原配置Owner的实际发布日历和PreparedIntake采用到完整原CRM图，保留发布人/时间、审批证据、时区规则、原身份和全部源周期/假期。配置生效时间独立保存，支持year0000、任意小数精度及合同允许的闰秒；假期保存原精确日期，旧CLR接口不能表示时明确失败。旧v1/v2入口保留，不迁移或切换既有环境。

实际原Bundle/来源逐项核对，Intake保持原不可变保存视图Draft，不构造配置激活。采用使用调用方ReadCommitted事务、局部savepoint与独立EF tracker，冲突时回滚本次采用，外层最终决定提交。所有映射标量及子行逐一比较，既有冲突拒绝覆盖，重复采用一致。原生复合租户FK、来源身份/原字节关系、内容冻结及同一父行锁保护子表写入；两个竞争方向均观察pg_blocking_pids。原生关系不单独保证全部业务内容，验证读取器拒绝手写了正确身份但错误内容的关系；返回值不授予激活、权限或消费回执。

154项唯一相关本地检查通过、0跳过：23项真实PostgreSQL（10项本轮采用/边界、13项原v1图回归），131项旧Intake/Calendar/模型回归；生成器检查和受影响项目构建通过。 两个并发夹具初次重复跟踪eager-loaded日历，改为一份关联图后双向验证通过；失败保留。未改变生产源码的成功结果直接复用，文档/提交/SHA不重复产品验证。证据`D:/CP6-local-test/wave111/foundation/final-validation.json`。

CRM准确分支已删除、DEV-2官方finish释放；自有PG55446已停止，数据/失败证据/缓存保留。COORD一次完整任务审查，无Actions、保护调整、既有环境迁移或部署。

真实宿主和隔离衔接（原配置保存Serializable，本原图接口明确ReadCommitted）、ConsentFact/ReceiptProof/GraphBindingStatus、消费重放、service身份及H01最终权限、编号与完整submit/receipt/page仍待。正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR87](https://github.com/GTX537/CP6.AI-Handoff/pull/87)已采用 `a3b252cb3d307767bce3ef8349b991a9f8c7a36f`，准确分支删除、固定DEV-2 finish释放。 CRM分支删除/释放已核对；Main文档PR/finish继续，当前START后墙钟19.61min（原估40–70），最终按Main finish计，不叠加各仓。

本文件提交时Main最终合并/席位收尾仍待；最终证明以官方finish及 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave111-final-verification.json` 为准，下轮真实开工时统一核销，不另建记账分支。
