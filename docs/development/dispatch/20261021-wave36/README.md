# 第三十六轮：订单取消库存／制造级联Owner

COORD本聊天顺序执行，采用writing-plans/executing-plans与项目精简规则；基线`f54402d0703e3b15fa67036358b1ce383930cad9`。开工2026-10-21T12:07:14.632076+00:00；24小时检查窗，代理预计150–270分钟，复杂故障余量8小时。DEV-1产品、DEV-2资料，零新树/clone/完整副本/代理。

|任务|代理执行估计|
|---|---|
|取消合同、关联对象/字段、权限与事务核对|20–35分钟|
|取消Owner、库存/制造效果及最终撤权/部分失败|60–120分钟|
|原生PG/实际HTTP及必要前端定向验证|35–65分钟|
|一次集中审查、PR/台账/Handoff及固定席位收尾|25–40分钟|

- [x] `OrderService.CancelAsync`接当前原会话和独立`erp-order:cancel`，复用W33部门映射及W35共同权限根，冻结输入，核原生键/全订单对象；无Owner/旧事务/来源失败拒绝。
- [x] 核`OrderCancelBridgeHook`、`OutboundService.CancelOrderAsync`、`WorkOrderService.CancelAsync`及`StockMovementService`实际效果。保留NeedsDecision/PartiallyCancelled和可取消状态合同；每个子操作失败不留下半笔库存释放，不把权限失败降为best-effort成功。明确关联冲突、并发插入/状态变化和既取消对象的处理。
- [x] 最终身份/权限检查直到COMMIT；库存UNRSV/明细/制造/订单头处于受控事务，失败的子操作完整回滚，外部通知/财务桥只能在确认提交后。提交确认丢失返回UNKNOWN，不自动重放。
- [x] 新增`CP6.Tests/Erp/OrderCancelPostgreSqlTests.cs`，先实际复现服务权限绕过与取消一致性缺陷，再验证真实PG全链路、范围/撤权/并发/故障/部分结果；复用现有夹具，只跑相关测试与必要HTTP/前端。保留原失败。
- [ ] 更新一份DELIVERY/Owner合同、roadmap和四状态；集中作者SPEC/QUALITY、普通PR/远端采用；停止自有进程、回收自建data，原任务分支finish及固定席位释放。

验证命令沿用`run_wave36.py <stage> <相关FullyQualifiedName过滤> --pg`，以TRX真实执行及0跳过核对；现有取消状态机相关测试按改动范围纳入。不为每个步骤重复审查或建分支。用户已选本聊天单人执行，无额外执行方式确认。

其他查询/计算/追踪、持久业务operation、正式POWER EGG/CRM及真实DEV/UAT验收不冒充完成。PG55435仅本轮自有夹具；不迁移既有数据库、不新增真实授权、不触发Actions或部署生产。

实现、最终119项相关.NET/18前端/typecheck及一次作者审查已完成；单份[DELIVERY](../../evidence/WP02-03/order-cancel-owner/DELIVERY.md)。原失败/夹具错误均保留，最终0失败/跳过。6处本轮自有PG生成data回收689069947逻辑字节（0.642GiB）；清理时段D盘实际空闲增加692285440字节（0.645GiB）。正常PR、Handoff和原分支收尾待执行。
