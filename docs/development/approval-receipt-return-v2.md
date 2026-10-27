# 业务应用回执返回审批端

W185 属于原七天目标的直接前置。任务交付与最终主线采用证据见本轮 DELIVERY；本地验证不作为企业运行验收。

## 原始事实与当前投影

Tool Owner 已提交的 `ApplicationReceipt` 和 Outbox 保持不可变。Workflow 接收端使用原审批 Submission、Decision、Subject 及登记的应用单元核对回执；当前首个提供方是 `Main.Wms.ToolDisposition.v1`，实际原件来自其已提交回执。接收事务只读 Owner 原件，不获取 Owner 业务行锁，不执行报废或其他库存效果。

`cp6_approval_receipt_v2` 独立保存原始请求、规范字节、摘要、当前服务授权审计、流及连续历史。当前接收状态由持久 Inbox 与连续历史确定：

|结果|HTTP|含义|
|---|---|---|
|NOT_OBSERVED|200|当前合法服务在本 Scope 内实际查询后未观察到原事件|
|PENDING_GAP|202|原回执已持久，但缺少连续前驱；不移动当前业务结果投影|
|RECORDED|200|回执进入已验证的连续历史；响应同时给出当前最高连续版本|

固定接口为 `POST /internal/oa/app/v2/application-receipts`、`GET /internal/oa/app/v2/application-receipt-ingestions/{receiptEventId}` 及当前投影 `GET /internal/oa/app/v2/application-receipt-projections/{receiptStreamId}`。响应保留原规范摘要和 UTC 毫秒时间。输入不接受调用方指定租户、强制应用或替代查询地址。

授权 fence 后锁定 Submission、流投影、Inbox。流自然键、版本和前驱必须连续；收到第 2 条而缺第 1 条时保存 GapWork，补到第 1 条后消费已存第 2 条。只有 BLOCKED 允许追加后继；APPLIED、STALE、CONFLICT 是终态。事件或版本冲突保留原记录并保存冲突证据。确认丢失按原事件查回，撤销写权后仍须通过当前读权才能恢复。

## 显式登记与可靠回报

HTTP 认证复用登记的 C02 服务令牌与 `ApprovalIntake` 租户映射；还必须存在独立 `cp6_approval_receipt_v2.registrations` 和精确 `subject_grants`。已有提交或审批决定投递权限不会自动授予回执权限。迁移不种入真实服务、角色或业务授权。

`ToolApplicationDelivery` 为独立默认关闭的发送配置，字段沿用已有固定 Origin、Version 和租户服务凭据登记。凭据只由宿主秘密来源提供，不能进入请求证据或日志。发送方保存租约、递增 epoch、实际尝试与确认；每次网络交换都在数据库事务之外。先查原事件，仅明确的 200 NOT_OBSERVED 才原键 POST；202 PENDING_GAP 只查询等待补链，503 或丢失响应保持 UNKNOWN。旧租约不能覆盖新尝试结果。

缺链 worker 使用独立默认关闭的 `ApprovalReceiptRecovery` 配置和 Workflow 服务身份查询 Owner 固定 `receipts` 路由。还必须为该服务显式登记回执端 `can_recover` 及精确 Subject 恢复权限，同时保有 Owner 回执链的当前读权；已有审批投递权本身不授予恢复权。

每页验证 Scope、流、Subject、Decision、原摘要、固定 snapshotToken、序号、前驱和合法状态转移。原响应和权限证据持久保存，随后用小事务提交连续前缀；网络不进入事务。游标失效从已验证位置重开，崩溃保留前缀，过期租约不能回写。首次验证快照后，把 GapWork 目标扩展至该快照最高版本，单轮三页预算耗尽时仍排队剩余版本，不因最早缺口已补齐而提前结束。冲突保存原响应并隔离，显式配置版本更新后才能重试；原投影不被覆盖。

真实 WORK/BORROW/STOCK 关闭来源仍未就绪，当前业务应用保持准确 BLOCKED。此回报链不把审批通过、运输确认或回执入账解释成物理报废成功；原 D2/D3/D5/D6/D7 出口保持。
