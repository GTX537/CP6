# W183-01 — 实际审批决定可靠投递

COORD 本聊天单人，Main 基线 `27853ba21dcc1284b04e0ecd8e308dfbd00b470d`，复用 DEV1/DEV2。W182 已完整收尾，仅归档一次。实际 Workflow 决定通过现有 C02 服务身份，可靠投递到实际 Tool Owner 接收入口；不会重建原决定或执行业务报废。

## 实际采用

持久领取先保存 epoch/token、原路由和准确注册/Subject 权限版本，网络位于事务之外。先查询原 EventId，只有结构与身份匹配的 NOT_OBSERVED 才投递原 bytes。接收应答丢失、领取提交未知及确认提交未知分别从原键恢复。PENDING_VALIDATION 继续查询；403/404 不当作未观察到，服务不可用保持未知。

确认时重核当前 Scope/服务/对象授权，租约未过期且 epoch/token 匹配才更新；状态与不可变确认记录同事务。遗漏记录、事务内撤权再恢复、旧 worker 迟到不能提交假确认。BLOCKED 等待明确登记版本恢复，已有原消息不能移动到另一个目的地址或身份。

宿主使用明确登记的 C02 confidential client 和实际 token 发行入口，生产 TLS 默认严格验证，关闭重定向和 Cookie，目的路由固定，凭据不写入证据。默认关闭功能，不种入服务或权限。配置与边界见[投递说明](../../approval-decision-delivery-v2.md)。

## 验证与审查

19 项有效本地检查，其中 19 项 PostgreSQL18/HTTP、0 项单元，0 skip；实际来源和源码绑定为 `D:/CP6-local-test/wave183/foundation/final-validation.json`。覆盖真实 Owner 接收后丢失响应、撤销接收写权后的只读恢复、双连接竞争领取、租约接管/迟到确认、404/403/503、待验证查询、两个发送端提交未知点、当前对象撤权、遗漏确认记录及同事务 ABA 撤权回滚。

实际 TLS Kestrel、C02 token 发行、真实 PostgreSQL token 登记和 Owner HTTP 组成一条完整投递路径；故意中断接收后的响应，恢复后只出现一次 POST 和一次 Inbox/事实/应用流。生产 TLS 构造器拒绝测试自签名证书；测试专用 handler 仅固定本次准确证书。当前 Owner 接收回归和实际 Program 审批 HTTP 冒烟覆盖新增注册的影响。无前端改动，不启动无关全仓测试。

作者集中 SPEC/QUALITY 审查完整 diff，核原件/Scope/身份、事务与网络边界、租约 CAS、错误响应和应用状态分别记录。保留实际执行日志，不将测试夹具、传输 ACK 或旧成功结果冒充真实企业资格/业务验收。

## 保留范围与生命周期

实际业务应用、ApplicationReceipt/回执链/摄取、依赖关闭来源、GAP 恢复、管理重放及当前业务页面仍保留。原 ROU/BOM/WO/SCHED、D2/D3/D5/D6/D7 全部出口保持，七天目标 ACTIVE。

只关闭 W182 限定 `WP10-48.wave182_owner_decisions`；新增本轮限定 `WP12-07.wave183_decision_delivery`，完整任务和正式运行验收不提升。

估计实际 60–100 分钟；当前 Main 登记 18.65 分钟，最终 START→finish 计一次，重叠 Handoff 不累加。Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/159，source `fad1abfcabeb1928628d0a046bbeccfe619b3c32` → target `b55f7039e027d46d1e6e1e2f80d36f0fb544ca29`，准确树采用、本地/远端任务分支删除和 DEV2 释放已核对。Main 本提交待正常 PR/采用/finish，最终以 `wave183-final-verification.json` 核销。

固定目录、用户原件及缓存保留；无新增源码树、Actions 触发/取消、既有环境迁移、生产部署或分支保护绕过。
