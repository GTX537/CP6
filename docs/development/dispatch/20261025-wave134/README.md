# W134 原生自动受理审计来源修复

COORD本聊天单人，原七天目标D6/D7定向修复。唯一活动BUG为[CRM #157](https://github.com/GTX537/CP6.CRM/issues/157)。W133三仓完整合并、准确分支清理和官方finish已验证，实际103.14分钟含真实60分钟等待；本轮只按其最终proof核销，不重复实施。

|任务|预计本聊天实际执行时间|
|---|---|
|实际HTTP/PG确认原Lead与同事务审计不一致，先红灯|5–8分钟|
|根因最小修复，原步骤、宽合法输入与原请求重放复测|5–10分钟|
|一次任务级审查、正常三仓合并、准确分支清理和席位释放|10–17分钟|

合计20–35分钟；Main START 2026-10-25T10:26:45.875721+00:00至最终finish计一次，不叠加跨仓。固定DEV-1/Main W134-01，DEV-2/Handoff W134-HANDOFF→CRM BUG-157-INQUIRY-AUDIT（交付别名W134-02）→Handoff。CRM独立分支codex/bug-157-inquiry-audit，从当时最新已确认main创建；不新增物理工作树，不派代理。

前置：四仓远端、AGENTS、台账及固定席位已核对；规则字节未变，复用完整阅读。CRM当前来源f0ff45691b9c27d91dd5c53b9f67d00761b475f4：WriteGraph创建Website Lead，已接受存储映射Website=10/Manual=20；传入Effects.Changes却硬编码20。Effects保存原字节，问题在原调用方，不在转换器或消息envelope。以真实原生HTTP提交、原Lead数据库行及同一租户/资源的审计作直接证据，不写静态镜像断言。

先在原产品代码运行失败用例，再只修正该错误值。核对自动受理、合法宽文本输入、NeedsReview无Lead，以及原请求重放后原审计/事件和效果数量不变。使用独立PostgreSQL；相关编译/测试和一次作者SPEC/QUALITY，保留红灯。旧原图、授权、事务、幂等键、事件和历史不可变审计不改写，无数据迁移。没有包/公共合同/构建配置/Actions/生产变更。

正常PR采用、远端源码/树核对和原步骤复验后关闭Issue，不以PR自动关闭代替验证。Main同步台账/交付/四状态，Handoff保留本轮START和DELIVERY；准确任务分支删除、finish释放固定席位，数据/失败/cache保留。

下一窗口继续原D6 NUM/FILE/PRIV冻结路径的准确适用与真实政策/日历来源核定；D2新对象可信归属/REP/RMA共同门、D3计划写协议、D5实际宿主与企业运输、D7最终集成保留。完整阶段、375正式卡、125目标和247运行验收不因本BUG修复提升；七天ACTIVE。

官方管理器要求任务ID与分支完全对应，因此产品采用BUG-157-INQUIRY-AUDIT/codex/bug-157-inquiry-audit，满足BUG分支规则。原START中的W134-02保留为本轮交付别名；没有领取第二个产品任务。

## 交付与继续范围

原实现两个自动受理场景均Expected10/Actual20；修复后相同3项真实HTTP/PG检查通过，必需0跳过，含宽合法输入、待审核无Lead、原请求重放的原回执/审计/事件/数量和异体冲突。 CRM/Handoff已采用、准确清理分支并finish，Issue已关闭；Main收尾继续。[交付](../../evidence/W134-01/DELIVERY.md)。继续原D6 NUM/FILE/PRIV在冻结公开提交/H01路径的准确适用与版本边界、真实政策/日历来源核定；D2新对象可信归属及REP/RMA共同门、D3计划写协议、D5实际宿主/企业运输和D7最终集成保留。已有日历原生发布与显式Owner批准入口须按实际源码复用，不重复构造；企业资料与真实运输不由夹具代替。
