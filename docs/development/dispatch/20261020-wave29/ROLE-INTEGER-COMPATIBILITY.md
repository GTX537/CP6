# W29-353：完整整数角色身份兼容

既有W29-01/02和协调PR354/356/357已正常集成、分支清理、固定席位释放；68.3分钟核对结果保留。本持续目标不能把合法int域中的0/负数受控拒绝视作角色合同全部完成，因此在同一24小时窗继续#353，预计本代理再1–3小时；不重复已完成实现和验证。

## 已确认合同与方案

- Sys_Role的(TenantId,int RoleId)保留全域，不换Owner、不映射角色号，不凭role:1或名字admin授平台权。现有v1 user/permission/token事件限制正数，Main writer、IAM附角色校验和字段决策也有限制。
- 原CRM可执行Spec §9.4明确版本化事件与生产者/消费者责任。新增user.changed.v2、permission.changed.v2、token.revoked.v2（schema2.0.0），包含规范十进制Int32完整边界；现有v1 Schema/例子字节不变。组合bundle添加v2并提升版本、固定新hash，保留严格未知major拒绝和数据/元数据/PII约束。
- 原有正整数用户/角色继续发v1；首次需要非正数角色的aggregate升v2，以后保留v2，aggregate ID、partition、单调版本与Owner不变。Topic仍为既有传输通道；旧消费者对未知v2明确拒绝，不能截掉非正数角色后继续授权。必须先采用CRM兼容消费者，再采用Main producer；这里只合源码和验证，不自动部署/启用真实订阅。
- Platform通用bundle/CloudEvent实现已支持major解析和精确SchemaVersion匹配；先以现有依赖验证，不预设必须改包或发包。权威事件Schema在Main维护，CRM保存完全相同字节；只复制必要合同资产，不复制仓库。
- Main新IAM用户分配/角色生命周期、权限编辑和相关字段决策遵守已存在完整int身份；实际存在/启用/版本/租户校验保留。SSO和前端检查按真实调用链覆盖，避免把0当null。
- CRM精确接入validator、reconciliation目录、projection/session失效、published compiler、authorization依赖和撤销原始来源，保留旧v1行为。验证正数不变、新域成功、溢出/非规范/未知major拒绝、旧消费者拒绝和事务失败不部分保存。

## 执行与配额

COORD单聊天串行，不派代理。DEV-1 Main codex/w29-353从cf186bdb0fa77402a64626513b8c16a1f85fc3a5领取；DEV-2先发布真实Handoff START，精确推送后deferred释放同一分支，再bind到CRM，从其最新main领取同任务分支。CRM完成、merge/finish后DEV-2再bind回Handoff并resume原通信分支，集中补交付、合入和释放。始终四仓合计2开发+1集成，无额外物理目录。

Main范围：contracts/events/platform，Core/Services/CrmIdentity及受影响Sys授权/IAM，相关Core/Persistence身份baseline，实际HTTP/PG/合同/字段测试，必要用户页面验证及协调文档。CRM范围：同字节platform合同、Infrastructure/Identity及其相关unit/消费测试和单份交付；不进入业务实体写入，不启动冻结方向，不借本任务宣称CRM完整Owner采用。

验证仅本地PostgreSQL和相称纯消费者/浏览器测试；CRM现有SQL Server集成夹具不是新增目标，不增加双库门禁。优先复用原生Main PG producer证据，向CRM实际validator/compiler消费传递必要事件文件；数据库Owner实际接入不在本合同兼容修复内，也不冒称完整CRM数据库验收。每完整任务一次集中作者SPEC/QUALITY，必要修正局部复查。

完成需Main及CRM候选验证、正常PR采用、跨仓相同合同核对、#353按真实闭环关闭、Handoff和台账同步、相关进程停止、任务分支处置和固定席位释放。Actions、既有环境迁移、真实租户审批及生产部署仍未授权。
