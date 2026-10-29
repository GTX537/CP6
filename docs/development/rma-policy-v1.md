# 退货政策发布 v1

W227 为 Main WMS RMA Owner 增加实际政策管理入口，服务现有 `/wms/rma-applications` 页面。政策是后续可退判定的业务依据；本接口不批准申请、不占用可退额度，也不写入库存或财务。它不是完整 RMA-C03 的采用证明。

## 范围与发布

`RmaFormalOwnerV1` 复用现有可信仓库/站点选择、当前权限和事务。每个租户、仓库、站点有一个政策头；版本以 `Main.Wms.RmaPolicy.v1` 引用，保存完整不可变正文、发布时刻、实际发布人权限观察引用、发布原因及上一版本引用。更新必须提供准确 `expectedPolicyRef`；首次发布显式为 null。竞争发布由现有 RMA 写锁与 CAS 串行决定，旧头返回 412。

新动作 `wms-rma:policy-publish` 仅登记到动作目录，不自动授予角色。新发布还需要当前 RMA 查询、字段读写和可信仓库范围；仅撤销发布权后，原操作者仍可按当前查询权限恢复已经提交的原结果。撤销读取、仓库范围或身份后不能继续披露原政策正文。

发布者明确填写名称、启用状态、原因代码与名称、允许处理方式、签收后期限和独立审批人要求。期限 null 明确表示政策不限定天数；有期限时为 1–36500 天。没有政策返回 `NOT_CONFIGURED`，停用通过新版本表达；系统不种入任何企业默认政策。允许处理方式沿用 RMA 既有六种意图；允许某处理方式不表示对应效果 Owner 已实现。

## HTTP 与恢复

|入口|请求与结果|
|---|---|
|POST `/api/wms/rma/v1/policies/read`|`scopeOptionRef`、`reference`（null 读取当前）；返回 NOT_CONFIGURED / ENABLED / DISABLED 及正文。准确旧引用读取不可变旧版本。|
|POST `/api/wms/rma/v1/policies/publish`|`scopeOptionRef`、`expectedPolicyRef`、`policy`、`reason`；`Idempotency-Key` 为原命令 UUID。返回已提交版本。|
|POST `/api/wms/rma/v1/policies/commands/query`|原 `scopeOptionRef` 与 `commandId`；返回 FOUND / NOT_OBSERVED，后者不能证明没有效果。|

请求使用既有 `X-Correlation-Id` 和 `Cache-Control: no-store`。未知输入成员在 HTTP 边界拒绝；内部调用采用已校验类型，存储版本直接复用保存的引用，不逐层重新计算摘要或资格。

政策头、版本、实际操作者事实、审计和原命令结果在同一 PostgreSQL 事务保存。提交回执丢失时按原键查询；相同命令和正文返回原版本，不按今天的政策重做，换正文返回绑定冲突。浏览器在发送前将原请求保存到既有私有 IndexedDB，使用独立 `policy:<tenant>:<actor>` 键；查询未观察到结果时保留，只有明确重派操作才重发准确原请求。身份或页面变化后丢弃旧异步显示结果并保留待恢复记录。

## 存储与未完成边界

迁移 `20261030022700_RmaPolicyV1` 增加 `cp6_rma_formal_v1.policy_heads/policy_versions`。历史禁止 UPDATE、DELETE 和 TRUNCATE，当前头以延迟外键指向实际版本；只做前向迁移。验证使用隔离 PostgreSQL，未迁移既有环境。

页面展示当前/历史政策，提供显式发布、停用和原请求恢复；申请仍是业务意图。独立批准必须后续采用实际政策、完整来源、可退额度和决定权限，按政策执行审批人分离。真实签收、发票/交期、source cut、E/A/P 与正式 IN 未由本轮补齐，也不以已发数量、缺失记录或界面选项代替。

本轮验证与范围见 [交付报告](evidence/W227-01/DELIVERY.md)。
