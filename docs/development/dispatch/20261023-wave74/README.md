# W74 / 七天一期D6：表单配置不可变版本身份

COORD本聊天单人，2026-10-23T18:34:25.303602+00:00。Main `27c954f46a48f62020d7a3ad623b008f5e1239a3`，CRM `dc8784583860b78635a89924ad2576ba24a21e78`，Handoff `88566612aac8f2493a1456e8bfcd351b5c6ab257`。W73 CRM113/Main454/Handoff49均采用，27项独立PG通过，三条准确分支删除、固定树干净detached、全部席位释放，Actions0、自有PG停止保留；实际19.43min＋前置4min。W37–W73不重复。

## 前置、范围与实现约束

最新AGENTS/POLICY哈希未变，CRM AGENTS已复读，status/capacity无告警，无重复W74任务/PR，CRM仅Issue109开放。复核R3 V15 canonical BundleIdentity：formVersion、intakeConfigVersion、businessCalendarVersion、bundleVersion均为integer>=1，无上限；旧SQL Server设计int不适用于新增PG合同。W73已有真实Site/Form/PreparedIntake身份图，但没有可持久绑定的表单版本。

本窗新增显式schema crm_inquiry_form_revisions_v1和FormRevisions，保存调用方提供的可信tenant、RevisionId、实际FormId、PreparedIntakeId及精确正整数版本。实际租户FK绑定W73父对象；写读都重读准备关系，校验确属该Form并复用内容/日历/摘要验证。返回准备阶段版本记录，不宣称完整BundleIdentity/ConfigurationView或当前生效表单。

复用ExactJsonNumber，原数字token完整保存；数字身份采用规范Coefficient+非负任意大Exponent，不展开指数，不转int/bigint/固定范围numeric。同一tenant/form下1、1.0、10e-1是同一版本，原记录不被等值拼写覆盖。规范键可TOAST，摘要只作非唯一查询加速，最终逐字比较完整规范键；哈希碰撞不能把不同合法版本错误合并或拒绝。明确不自动递增巨大版本、不据字典序选最新。

每次新插入由数据库trigger更新真实每表单Gate行并串行核对版本唯一性；Gate避免仅advisory锁在陈旧Serializable快照/混合隔离事务下漏掉版本。ReadCommitted等待后检查新快照；Serializable遇原生40001交由Owner重建事务，不暗中重试。原生直接INSERT也经过版本guard。参考官方[函数快照语义](https://www.postgresql.org/docs/18/xfunc-volatility.html)及[事务隔离](https://www.postgresql.org/docs/18/transaction-iso.html)，最终以本地混合隔离竞争实测为证。

Save使用调用方RC/Serializable同连接事务及保存点，不commit；重放返回原记录，失败/取消回滚本次Gate和Revision写入并保留外层此前工作。读取重解析原token、核完整规范键和实际内容关系，不把数据库结构FK宣称为所有语义一致性。不可变历史阻止UPDATE/DELETE/TRUNCATE；Gate是内部并发状态，不是授权或业务版本。runtime无DDL/删除权限，管理员可信，既有namespace漂移拒绝而不修复。

相关PG验证覆盖合法域（超bigint、超numeric指数、超索引页长度且不膨胀）、等值版本/冲突/跨租户、同Form内容归属、原生FK与直接版本重复、RC与Serializable/混合隔离竞争、取消、完整原结果共同事务/回滚、原文损坏和执行结构漂移。一次作者完整SPEC/QUALITY；无前端改动不跑UI/全仓。

正式Intake/Calendar版本来源、完整Bundle保存和审核/生效选择、真实Owner当前身份/对象/字段最终共同门、audit/outbox、Preview/Activate/publicAttempt、隐私保留及Program/HTTP仍是后续窗口。W60旧日历ContractVersion不伪装为任意范围正式版本，本窗不改其已接受历史范围。CanAuthorize/CanActivate仍false；Issue109冻结原件未改。

## 耗时、席位与出口

预计实际60–90min：实现25–35，相关PG验证20–30，审查/三仓收尾15–25。独立前置约2min；W73等待内只读合同核对不重复计时。Main登记start `2026-10-23T18:33:13.191971+00:00`。七天持续目标ACTIVE，保持原目标不重建。

DEV-1 Main协调，DEV-2 Handoff START保存并finish deferred后bind CRM，产品采用/finish后bind回Handoff收尾。PG55442无监听，使用新自有库，结束停止保留。正常PR、准确远端采用、定向分支删除/finish；不新增工作树/代理，不运行Actions、既有库迁移或部署。24/375整卡、125目标0交付、247运行NOT_RUN不提升。W51指定停止data删除自动审批曾以blocked by policy拒绝，保留不重试。

## W74实际交付

CRM [PR114](https://github.com/GTX537/CP6.CRM/pull/114)已采用，source `c04300a5b1c68c4aad78e4a45a29dd3f1b0af8b9`，远端main `67f8b28fd9b08b0be67060716e4729e0ba547c93`，10条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/67f8b28fd9b08b0be67060716e4729e0ba547c93/docs/development/evidence/W74-01/DELIVERY.md)。29项独立相关本地PG用例通过（25新增+4既有），无跳过，相关API及必要依赖构建成功；真实Owner业务验收NOT_RUN，无Actions。

新增原生不可变表单版本，真实tenant/Form/PreparedIntake关系重读验证。保存任意合法正整数版本的原token和完整规范数值键，不展开巨大指数、不降为int/bigint/有界numeric；摘要仅非唯一查询加速，完整键最终判等。同号等值拼写幂等不覆盖原文，异内容冲突，新版本保留旧内容历史。

原生trigger更新实际每表单Gate后核对版本，四种RC/Serializable混合竞争观察真实transactionid等待，陈旧Serializable返回40001由Owner重建事务。Save参加调用方保存点，不commit/隐藏重试；取消、后续原生SQL错误、原结果共同commit/rollback/重放均通过。最终24+5=29独立PG通过，未重复累计初始两项；一次完整作者SPEC/QUALITY无剩余阻断，自有PG正常停止且数据/红灯/TRX保留。CRM准确任务分支删除、固定树干净detached、finish释放。

这是表单版本前置，正式Intake/Calendar版本来源、完整Bundle/ConfigurationView、真实Owner最终共同门、audit/outbox、Preview/Activate/publicAttempt、隐私保留及Program/HTTP仍待接。CanAuthorize/CanActivate仍false；W60旧日历ContractVersion未冒充正式任意范围版本。Issue109保持OPEN。无新包、既有环境迁移、Actions或部署；24/375整卡、125目标0交付、247运行NOT_RUN保持，七天目标ACTIVE。W51指定停止data删除被自动审批拒绝原因为blocked by policy，保留不重试。

Handoff [PR50](https://github.com/GTX537/CP6.AI-Handoff/pull/50)已采用 `c101b055844f9ef65632eb8726d0634b9db7cc66`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约15.11min，原预算60–90min，最终以finish为准、子任务不重复累计；前置只读核对约2min。
