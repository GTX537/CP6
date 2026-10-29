# 出库分配采用当前库位控制 v1

W218；PostgreSQL；基于 [库位本域控制](location-operational-control-v1.md) 和 [独立库存采用](stock-location-control-adoption-v1.md)。覆盖实际 OutboundService.AllocateAsync、Program HTTP 分配入口和既有存储父单据驱动路由，不宣称完整 OUT/Stock Claim 或正式 RoutingPolicy 已完成。

## 当前候选与新预留

候选先保留现有产品、仓库、数量、所有权、质量、召回、盘点冻结及当前 WMS 范围过滤，再要求存在当前有效、非初始化隔离的 native Location，且 IsBlocked=false。合格候选之间继续按原 FEFO 顺序选择，仓库之间继续按存储父单据和路由的原顺序选择。单个被封锁或缺控制候选不妨碍合法替代。

所有许可候选仓库都找不到可用库存时，若存在满足其他条件但缺少当前库位控制的候选，返回 LOCATION_CONTROL_UNAVAILABLE（409）；否则若存在因封锁被排除的候选，返回 LOCATION_OPERATION_BLOCKED（409）。未知优先于已知封锁，不能把它写成普通零可用缺料。真正不存在满足其他数量/质量等条件的候选时，保留原有缺料或不足处理。范围外库存不参与控制原因判断，不泄露其存在或封锁信息。

选中库位在实际 RSV 前再次核对，并登记同事务提交前检查。复用 OperationsAccessV1.GuardNewLocationUse，W217 独立新命令也使用该定义；未创建另一份控制权威。既有业务/主数据 PostgreSQL 锁串行化外部封锁，最终读取捕获同一事务参与者的控制变化。

## 原子性和已存预留

候选失败、后续明细失败或最终检查失败时，整个分配事务回滚：Stock 预留、StockTransaction、序号、分配明细/单据状态、继承映射及同单创建的缺料均不留部分效果，通知只在成功提交后执行。

分配先提交、封锁后生效时，保留已发生的事实；后续普通新分配检查新控制。CancelOrderAsync 仍只按实际存储的预留校核并释放，重复取消不多释放。本轮未扩展为统一禁止已有业务事实处理，也没有以本域布尔封锁证明完整旧义务、Claim 或全部 Owner 保护。

## 路由与交付边界

内部路由必须保持原 Owner 事务、实际当前父单据和准确明细身份；客户端不能传任意客户/产品/fallback 替代。独立路由预览/编辑的正式采用门禁保持，功能开关开关两种实际 Program 路径分别验证。非 PostgreSQL 历史分支不新增功能或验收要求。

相关证据见 [交付报告](evidence/W218-01/DELIVERY.md)。无数据库迁移、依赖或前端改动。完整 MASTER qualification/08E、Source/Root/Claim、Transfer/REP/RMA 和原七天剩余出口继续保留。
