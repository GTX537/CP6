# W112 原询盘图的同意事实、回执证明与绑定完整性

COORD本聊天单人，七天目标ACTIVE，PostgreSQL唯一新增数据库。Main `39c6015c844518184d735e9c352eaf8f4ad87e48`、CRM `0323e3760c6638b48e5b0990968a574ceeb948aa`、Handoff目标 `a3b252cb3d307767bce3ef8349b991a9f8c7a36f` 已核对；W111 Main523/CRM138/Handoff87全部finish、准确分支删除和席位释放，实际23.18min/154项相关检查，不重复。

## 原合同与前置

已读取原R3 V15归档的规范正文、crm-inq-target-schema.sql及实际fixture。要求原PublicSubmission增加原站点/表单/Bundle/IssuedAttempt/Consent/回执到期和GraphBindingStatus，原PublicAttempt增加原签发/Bundle/ReceiptProof/指纹密钥版本与BindingGeneration；ConsentFact和ReceiptProof必须精确关联原Submission，不允许一张简化Submission替代完整原图。

W106真实GET已有不可变IssuedAttempt；W107原Attempt校验/标准化与限流；W110/W111已接入原表单和原日历/Intake图，无损版本/时间基础存在。本轮新增明确PGv4原图扩展及同意/回执存储和验证读取，采用PostgreSQL延迟约束与原行锁处理循环关系；新库显式安装，不迁移既有版本。

原任意精度版本、原始身份、源政策与真实浏览器绑定保持；半份图、交叉Submission/租户/Receipt、超出原到期或不可证明来源拒绝。绑定完成与同意/回执一致性在事务提交时核对；原始候选/历史状态不能冒充新业务完整图。撤销、墓碑、隐私裁剪不得恢复凭据；实际读取核对原图、hash、browser、DBclock及PII/撤销状态，不凭关系存在授权。

该窗口交付实际存储与验证边界，不伪造H01当前权限、编号回执、正式业务outbox或已消费成功。完整原Owner消费/重放、当前service/H01权限、真实宿主事务隔离衔接及HTTP/page仍接续实施；公开POST不因存储模型就宣称完成。正式24/375、0/125、247NOT_RUN不变。

## 任务和实际执行时间估计

|任务|交付|预计执行|
|---|---|---|
|W112-02 CRM|原图PGv4扩展、Consent/Receipt事实及验证读取，完整性/撤销/回滚/租户相关PG验证|40–65分钟|
|W112-01 Main|前置、台账/生成页及四状态同步|8–12分钟|
|W112-HANDOFF和收尾|一次审查、三仓正常PR、准确分支处置和finish|12–23分钟|

合计60–100分钟，Main START `2026-10-24T13:17:02.682349+00:00`；最终按finish墙钟计一次。复用DEV1/DEV2/原构建缓存，自有PG55446、D:/CP6-local-test/wave112；不新增工作树，不运行Actions，不改保护/既有环境/生产。

- [x] 原完整图的版本化扩展及ConsentFact/ReceiptProof原绑定。
- [x] 相关实际PG完整性、拒绝/撤销、事务/并发和局部兼容验证。
- [ ] 一次任务审查、三仓合并/远端采用、准确分支清理及席位释放。

## 实际交付与生命周期

CRM [PR139](https://github.com/GTX537/CP6.CRM/pull/139)已采用 `3f12f94215c1b7beec182312959ee4e39c77bd4f`，来源 `6e361a8a8e12e41227548fdc6cc5a6562be6640c`；[单份交付报告](https://github.com/GTX537/CP6.CRM/blob/3f12f94215c1b7beec182312959ee4e39c77bd4f/docs/development/evidence/W112-02/DELIVERY.md)。

显式新库PGv4保留原完整Submission/PublicAttempt图，增加ConsentFact/ReceiptProof和版本化来源绑定；原任意精度版本、冻结政策/条款、真实浏览器和到期时间校验。支持同事务原0→10暂存，延迟约束拒绝半图、交叉租户/Receipt及来源伪造；独立savepoint保留调用方原事务。读取与撤销锁定原Submission，两个顺序均实际观察pg_blocking_pids；DBclock/凭据/browser/PII校验及原五字段严格合同验证后才返回回执，撤销/墓碑/匿名化不能恢复。保留期按原FixedMinutes/CalendarMonths源政策，无损保留100ns。

82项唯一相关本地检查通过、0跳过：32项真实PostgreSQL（19项本轮事实/边界、13项原v1图回归），50项旧模型/回执codec回归。一次集中审查发现回执输出合同风险，修复后定向复验5项；其他有效结果复用，失败记录保留。 证据D:/CP6-local-test/wave112/foundation/final-validation.json。新定义拒绝旧试验v4库后，只在同一自有实例新建隔离数据库，全部失败/数据/日志/缓存保留；自有55446已停止。

CRM准确分支删除、DEV-2官方finish释放。无Actions、保护调整、既有环境迁移或生产部署。该交付仅存储/验证组件；原业务消费与重放、实际service/H01权限、编号/业务outbox、宿主Serializable与本原图ReadCommitted衔接和HTTP提交/回执/页面仍待。没有8天真实等待到期或正式验收。正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR88](https://github.com/GTX537/CP6.AI-Handoff/pull/88)已采用 `bceba80f0bcc2c5370c50b6069a981426dcfb27d`，准确分支删除、固定DEV-2 finish释放。 CRM分支删除/释放已核对；Main文档PR/finish继续，当前START后墙钟25.94min（原估60–100），最终按Main finish计，不叠加各仓。

本文件提交时Main最终合并/席位收尾仍待；最终证明以官方finish与final_proof为准，下轮真实开工统一核销，不另建记账分支。
