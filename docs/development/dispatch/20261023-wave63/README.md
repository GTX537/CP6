# W63 / 七天一期 D6：原配置内容规范化与摘要

COORD本聊天单人，2026-10-23T10:58:50.396195+00:00。Main `bbba661856c0459af5261706924d76877f34dfeb`，CRM `6bb3b43e6d674148af470b4b44aa0b46917f1f47`，Handoff `99db307c36bd5d5e9394e68bb1e84849a42566db`。W62 CRM107/Handoff38/Main431全部采用；一项真实跨仓场景通过，无跳过，实际36.05min、前置估计6min。准确分支删除、固定树干净detached、全部席位释放、Actions均0。W37–W62不重复，持续目标ACTIVE。

## 准入和范围

最新AGENTS/POLICY哈希不变，status/capacity无告警，无重复W63任务PR。本窗继续WP03-01独立保存前置，不能替代尚未满足的真实Owner/字段映射/最终门。基于W61不可变ConfigurationDraft，补专用于该原合同的规范化JSON内容视图与SHA256摘要；保留原SourceJson/SourceSha256，原始读取和日历绑定语义不变。

原规范要求UTF-8、NFC字符串、Unicode码点键排序、无无意义空白/重复键、数字不保留等值替代写法。原UI ready样例的contentHash `e9f372003551cc384002b46cea72101c8891955736ce1e09174c4d1fa6b7f297` 已由独立Python标准JSON排序/NFC计算复核一致（1519bytes）。后续测试直接引用冻结原样例，不能从被测实现反向生成期望值。

已递归核ConfigurationDraft及引用定义：所有数字为原约束有界整数/固定常量，最大数值5256000；长小数/指数token先用已采用ExactJsonNumber精确归约，不能经double/decimal舍入。不将该事实推广到Bundle版本、其他DTO或任意JSON。规范化组件仅接收已严格捕获的原草稿，未知/重复/非法Unicode和告知hash仍由原边界拒绝；只输出这个固定schema的整数十进制写法。

NFC仅用于摘要内容视图，原privacyNotice等字节和已声明noticeHash保持，不重新计算引用字段、不把规范化JSON当原始业务输入。数组原顺序、完整policy与时间字符串保留（字符串NFC），不擅自排序period/holiday、截断时间或添默认。明确版本化该专用内容格式；它不是权限投影签名codec，不改原生成器/冻结合同/既有签名实现。结果不授权、不激活，不写Bundle/命令槽，也不生成rendered form/preview/attempt。

## 实际耗时、验证和收尾

预计35–55min：实现15–20；原样例及数字/Unicode/独立原值边界10–15；一次作者SPEC/QUALITY与三仓收尾10–20。仅本地相关单元测试和必要构建，不启动PG、不运行全仓回归/Actions。测试原已知hash、顺序/空白/转义和等值数字不改变摘要、NFC与原输入分别保存、控制字符/非BMP/分隔符UTF8、数组顺序及内容变化改变摘要、拒绝非法输入。

DEV-1总台账/四状态，DEV-2先保存Handoff START并finish deferred，再bind CRM；产品采用/finish后回Handoff并最终Main收尾。固定2+1席位复用，本聊天单人。无既有库迁移、发包或部署。完整24/375整卡、125目标0交付、247 NOT_RUN不提升。W51准确data删除的自动审批拒绝（blocked by policy）继续停止保留，无重试或绕过。

## W63实际交付

CRM [PR108](https://github.com/GTX537/CP6.CRM/pull/108)已采用，source `6c3feacec7a69243a25f6f5a78039922157990ad`，远端main `c7662eb196e72ddc8cab6d6c05490b6a310ca172`，7任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/c7662eb196e72ddc8cab6d6c05490b6a310ca172/docs/development/evidence/W63-01/DELIVERY.md)。12项相关本地用例通过、无跳过，API/Worker及必要依赖构建成功；原内容hash复现，完整Owner/表单验收NOT_RUN，无Actions。

新增原ConfigurationDraft专用规范化内容视图及ContentHash，版本crm-inquiry-configuration-content-v1。NFC/UTF8、固定ASCII键顺序、紧凑JSON和原有界整数的精确归约，复现冻结原UI样例摘要；长小数/指数/负零不经浮点舍入，完整policy和数组顺序进入摘要。

保留原SourceJson/SourceSha256、原业务值与noticeHash；NFC视图不回写业务值、不重算引用摘要，也不替代原草稿。时间字符串保留year0/全部小数精度。组件不是通用JSON或原权限投影签名codec，原生成合同/验签/日历/依赖/SQL均不变。结果不授权、不激活，不写Bundle/命令槽，不发preview/attempt。

一次作者完整SPEC/QUALITY通过；12项新相关用例当前成功，初始桩红灯保留。W61/W59未变证据按真实来源引用，不重复运行；本轮没有数据库或监听进程、Actions、迁移、发包或部署。CRM准确分支删除、固定树干净detached、finish释放。

完整24/375整卡、125目标0交付、247运行NOT_RUN不提升。七天目标ACTIVE，继续真实Owner前置和D6完整保存/公开表单必要基础。W51准确data删除此前自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试绕过。

Handoff [PR39](https://github.com/GTX537/CP6.AI-Handoff/pull/39)已采用 `57384c98558196d1ffab1c21829bb5e1fe75a847`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约17.81min，原预算35–55min，最终以finish为准、子任务不重复累计；前置只读核对约4min。
