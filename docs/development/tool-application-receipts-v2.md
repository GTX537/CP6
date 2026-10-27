# Tool 业务应用回执 v2

实际审批决定被 Owner 接收后，当前人类办理人可显式核对原申请的应用条件。批准、传输 ACK 与物理报废分别记录。本窗口缺少 WORK/BORROW/STOCK 的可信关闭参与者，因此不能生成 APPLIED 或改变实物报废状态；页面会显示准确阻塞原因。

## 人类入口与原操作

- `GET /api/wms/specials/v1/tools/{id}/application-context`：当前精确对象、Remarks 字段及查询权限下读取决定、最新回执、当前处置义务和 ETag。
- `POST /api/wms/specials/v1/tools/{id}/approval-applications`：当前 dispose 权限；提交 `operationId`、`receiptStreamId`、必填且可空 `expectedReceiptId`、原说明，携带读取到的 `If-Match`。调用方不能提交应用状态或冒充系统执行人。
- `GET /api/wms/tool-application/v2/operations/{operationId}` 和 `POST .../{operationId}/close`：原办理人及当前读取权限下查询或封闭未知原操作。原提交已完成时不重新执行，已撤销写权仍可按合法查询权限恢复；原身份、对象、输入不匹配不能重用。

资格检查绑定真实决定、冻结 Subject、适用政策与当前授权，重新核对人员分离及当前义务。申请已不适用记录 STALE；拒绝、撤回、人员分离或缺少关闭来源记录精确 BLOCKED。BLOCKED 可以在新操作中追加检查结果；STALE/CONFLICT 为终态，不覆盖历史记录。

## 同一事务与不可变原件

PostgreSQL 迁移 `20261028190000_ToolApplicationReceiptsV2` 增加独立 `cp6_tool_application_v2` schema。回执保留原决定摘要、完整 Subject、动作、办理人、当前版本、审计引用及上一回执，序号从 1 连续递增。按照原 `oa.app/2.0` 合同计算摘要，UTC 毫秒时间与原 Unicode 保持；内部 HTTP 返回保留该规范形式。

回执、审计、待回报 Outbox 和 Main 原命令日志在同一 Owner 事务提交。数据库约束核对流、原事实、前驱与序号，并在提交时确认 Outbox 和准确原命令结果俱在；UPDATE/DELETE/TRUNCATE 不允许改写原件。当前 Scope、对象与政策在提交边界重核。遗漏必需记录、权限 ABA 或过期 ETag 均回滚。提交应答丢失使用原 operationId 查询。

待回报 Outbox 在本窗口只证明已持久保存；实际回执传输及审批端摄取尚未交付。不得把它写为已发送或已经业务应用。

## 服务查询

沿用已登记的 C02 confidential client 与准确 Subject 查询授权，复用 Owner 事实接收路由下的应用流：

- `GET /internal/wms/approval-application-streams/{streamId}/result` 返回 `FOUND` 加完整原回执，或原 `NOT_APPLIED`。原事实 ACK 保持不可变。
- 同一流的 `/receipts?afterRevision=0&limit=20&cursor=...` 返回固定最大序号快照；limit 为 1–100。签名游标绑定环境、租户、服务主体、流、起点和最大序号，15 分钟过期，后续新增回执不混入原分页。游标无效/不匹配/过期返回 `409 CURSOR_EXPIRED`，重新从已持久进度查询。
- 每页重新检查当前服务与精确 Subject 权限。已撤销接收写权可以保留合法只读查询；撤销查询权则拒绝。未知流返回 `NOT_OBSERVED`，不得据此虚构回执。

服务宿主复用已配置 Data Protection。多副本的密钥持久化与共享应随真实宿主资格核定；本地测试不证明企业密钥配置合格。

## 页面与范围

版模实例页新增“审批结果与业务处理”，按当前权限显式读取和提交。浏览器只持久保存恢复所需原键，未确认时禁止重复提交；UNKNOWN、错误流/决定、伪造 APPLIED 等结果不清除恢复记录。身份变化忽略旧响应，权限拒绝清除已展示数据。

完整实物报废、义务关闭来源、回执消费者、GAP/管理重放及真实企业运行资格保持待交付。原七天 D2/D3/D5/D6/D7 出口不因本窗口关闭。无需新增依赖、自动 Actions、既有环境迁移或部署。
