# W221 退货申请实际页面入口

COORD单人；预计30–45分钟，固定DEV-1/DEV-2复用。

Usable return application page: current authorized native shipment picker, exact quantity text entry, create/submit/read and durable original-operation recovery across reloads and identity changes.

Native application intent only; formal OriginalFulfilment, entitlement and independent authorization remain incomplete. No stock or finance effects.

直接采用W220 API与共享HTTP身份、CSRF和原操作机制。新增当前来源选择查询和页面，数量保持字符串；本地只保存按租户/用户分隔的原操作ID，不保存业务正文。相关页面与新增查询验证、一次集中审查，复用W220未变的业务写入证据。正常PR合并、准确分支清理和finish释放。

## 交付快照

[报告](../../evidence/W221-01/DELIVERY.md)。当前登记26.02分钟；Handoff已释放、Main待正常PR/finish。
