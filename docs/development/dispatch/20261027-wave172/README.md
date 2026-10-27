# W172：流程草稿首个 Scope 消费 Owner

COORD 单人继续 CP6-7D-20261022。W171 已完整收尾，本轮归档一次并仅关闭 WP02-10.wave171_scope_authorization。固定 DEV-1/DEV-2，无新增工作树。

## 范围、前置与采用边界

按 OA-DEF R1 D-WP1 与 S3-COMMON R1 WFC-1 实现 FLOW 新定义的 disabled head + 首草稿、纯读取、精确 token 保存及原命令恢复。采用 W171 实际 WF_AUTH Scope 门；当前 IAM 会话、动作、资源、字段和显式设计期策略必须在真实事务内判定并末次复核。

正式新草稿使用独立 PostgreSQL authoring 表，保留完整 schema 字符串和稳定 UUID；不重建旧 Published 为历史证据。与 native 定义的 key/FunctionId/FlowCode 互斥由共同锁及双向数据库检查保证，旧入口无法读取或覆盖本批草稿。后续发布须明确把本批冻结版本接到原引擎，本轮不建立可执行投影、不启用流程。已有 native 定义及 W170 下一草稿功能保留原链。

authoring 策略为显式管理入口登记的 DIRECT_AUTHORING_V1：只允许本人实际会话，不代办；保存禁止角色集合和当前角色实际求交，配置版本与原命令审计可追踪。管理登记需现有 pub-role-perm:edit，登记不授予业务权限；默认无策略/无 Scope 时关闭新入口。业务复用已注册 oa-designer:add/edit，资源 workflow-definition 注册五类既有范围及 Name/SchemaJson/FormKey/FunctionId/FlowCode 完整字段域，按原 MAX/MIN 后默认语义求交，未知源关闭准入。

WFC-1 原槽为 tenant/environment/actualActor/operation/resource/commandId；严格重复键检查、封闭请求、原数字词法 canonical digest。历史原结果读使用当前资源/字段投影，撤销新写不自动剥夺已授权历史读，Scope 写门不可用不伪造历史为空；NOT_OBSERVED 不证明未提交。没有保留政策前无自动删除。

设计器接入新草稿列表、新建/加载/保存、精确 token 和原 command 恢复；失败不生成新号盲重试，身份变化清私有状态。完整发布、启用、表单、审批决策/TxD 与既有定义迁移仍待各自采用，不能计完整 WP12-01 或真实业务验收完成。

## 实际执行估计与验证

预计150–240分钟：后端事务与隔离70–110；设计器和恢复45–75；定向 PostgreSQL/HTTP/UI验证、一次 SPEC/QUALITY、台账四状态与两仓收尾35–55。实测官方Main START→finish，重叠 Handoff 不重复计时。

- [x] 新草稿、策略、命令及审计同事务；资源/字段及旧入口隔离。
- [x] 实际设计器采用与未知结果恢复。
- [x] 定向验证和一次集中审查；故障记录保留。
- [ ] 台账四状态、两仓正常合并、远端核对、分支清理及席位释放。

仅隔离 PostgreSQL18，本地相关验证；不迁移既有环境、不运行 Actions、不部署或变更保护。七天原全部出口保留，375卡/125完整目标/247真实验收状态不以限定采用关闭。

## 实际交付

[报告](../../evidence/W172-01/DELIVERY.md)，登记64.09分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
