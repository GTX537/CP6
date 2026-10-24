# W123 原生询盘保留维护的真实服务权限前置

COORD本聊天单人；七天持续目标ACTIVE。W122 Main534/CRM148/Handoff98已远端采用、准确分支删除和官方finish，实际27.01分钟，不重复。当前主线Main8d5df5d6265c8e4fe33ec62edbfe1d51d5ccbde1、CRMe06bdedcb0cd31c17b15fb1064573f9075b83800、Handoff fa331f5750c6155d2d5a74908a8b3c9708aa562f。AGENTS/POLICY、官方status、最新台账和实际源码核对后登记。

## 范围与合同

采用冻结R3V15 FINAL-MAIN第11节及原zip CONTRACTS/authorization-matrix.json：privacy:retention-worker仅要求真实服务主体/组织及crm-privacy:retention-run；不借用H01、人工session、主管、部门或PII字段权限。site/form仅作为签名请求关联，不由请求参数授予组织以外权利。保留实际服务身份来源、角色全集、注册版本/epoch、签名信任、nonce和最多五分钟有效期；Main认证根与CRM原七锁/最终当前检查保留。维护来源必须实际已发布，旧来源/用途/租户/信任和撤权均拒绝。

本轮Main新增从真实IAM读取精确维护动作的签发路径，CRM采用同源冻结purpose扩展，独立服务捕获及持锁维护权限检查，明确不把签名或当前身份证据当作具体行的处置授权。人类API拒绝维护用途，公开接收/H01证明不能转换为维护权限。原生维护身份仍需健康组织与其真实注册部门身份依赖，但不对待处理行套主管/部门谓词。

冻结最终稿明确撤回同意立即记录但不自动缩短行保留截止；较早草稿措辞不采用。真正匿名化必须后继由Owner在同一行事务验证持久截止、当前合法hold、完整图/历史/原结果/凭据、租户lease与持久工作记录，再执行原子处置。导出/下载、人类撤回、完整保留worker及原生Outbox发布仍未交付。本轮不安装或修改既有环境、不授予真实角色权限、不决定法律期限。

## 任务和实际执行估计

|任务|预计实际执行|
|---|---|
|W123-01 Main实际维护权限来源与签名|40–60分钟|
|W123-02 CRM真实当前服务捕获/持锁用途检查|35–50分钟|
|相关PostgreSQL/HTTP验证、一次集中审查、三仓收尾|35–50分钟|

合计110–160分钟，从Main START `2026-10-24T22:20:34.889793+00:00` 计一次，不叠加跨仓。复用DEV-1/DEV-2，PG55448仅自有隔离测试。无数据库schema/包变更预期；上下文schema只扩展冻结已存在purpose，旧用途及事件bundle不重标。无Actions、保护变更、生产部署；原失败记录和缓存保留。

- [x] Main真实维护权限来源与目的绑定。
- [x] CRM原事务当前来源/用途/撤权隔离。
- [x] 实际跨仓签名与PG定向验证及集中审查。
- [ ] 三仓远端采用、准确分支删除、官方finish。

24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY不变。WP03-03与WP02-15完整任务未完成。

## 实际交付

采用冻结R3V15保留维护目的与真实crm-privacy:retention-run动作。Main仅对显式已发布的实际服务身份签发，原认证根内读取当前IAM动作原字节和注册身份依赖；CRM在线获取新鲜nonce，完整服务及角色来源、持久信任和双层签名在原七锁/Serializable事务内核验，最终当前/撤权检查保留。维护用途不借用人类session/H01/公开接收，也不套主管或业务行部门/PII字段谓词；具体行处置授权仍由后继Owner确认。两仓上下文schema一致扩展purpose，原签名domain、事件bundle及旧用途保留。

CRM [PR149](https://github.com/GTX537/CP6.CRM/pull/149)采用 `9bc151f3f8f9962d7842e5566e6b61872b74908d`，源 `4a3d3094a216ff709ff437d80f880a23dfb53402`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/9bc151f3f8f9962d7842e5566e6b61872b74908d/docs/development/evidence/W123-02/DELIVERY.md)。29项唯一相关本地成功检查：Main13、CRM16；真实Main TLS控制器/服务认证/PG来源经实际CRM捕获、持久信任和当前事务，原H01提交回归、完整角色/当前动作、用途/租户/nonce/epoch/期限、撤权锁/恢复互斥和跨事务误用通过，必需0跳过。 逐例证据D:/CP6-local-test/wave123/foundation/final-validation.json。首次夹具共享菜单、测试内部访问/断言与真实跨仓基线组装问题已修正，原失败保留，成功输入未变直接引用。联通使用Main实际服务来源，独立宿主夹具身份保留不授予维护权限。完整保留worker/业务正式验收未运行。

Main实际签发路径须独立正常PR和远端核实，不能由CRM合并推导。本轮无数据库schema/包/工作流变化；只在原服务上下文schema增加冻结已有用途。自有PG55448已停止，固定缓存和用户资料保留。无Actions、既有环境安装迁移、真实角色授予或部署。

冻结最终稿明确同意撤回本身不缩短行截止；行持久deadline/当前lawful hold、tenant lease、单行完整图/历史/原结果/凭据原子匿名化、导出下载、撤回Owner与原生Outbox实际发布继续后继。24/375、0/125、247NOT_RUN不变，D6PARTIALLY_ADOPTED，七天ACTIVE。
Handoff [PR99](https://github.com/GTX537/CP6.AI-Handoff/pull/99)采用 `566e6e56acc5814b7252e1dcd1f1829befbb253f`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟27.14分钟，原估110–160分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。

本提交时Main最终合并和席位收尾待；以官方finish与final_proof作最终证明，下轮统一核销，不额外创建记账分支。
