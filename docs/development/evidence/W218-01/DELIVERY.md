# W218-01 出库分配采用当前库位控制

COORD 单人；PostgreSQL。基线 `a8c44554307f5d3fe173c765a17ab9eab7cadd63`。本报告写入时 Main 待正常 PR/finish；候选验证不等于远端采用完成。

## 实际改动

实际 OutboundService.AllocateAsync 的候选选择在既有数量、质量、召回、盘点、租户及 WMS 范围过滤之后，排除缺当前有效库位及已封锁的库存。保留 FEFO 和存储父单据驱动的原仓库顺序，存在合法同仓/跨仓替代时继续选择。全部无替代时，缺控制返回 LOCATION_CONTROL_UNAVAILABLE，封锁返回 LOCATION_OPERATION_BLOCKED，均为409，不把控制失败伪装成零可用缺料。

选中库位在实际 RSV 之前及提交前复核。W217 的独立库存检查提取到同一个 OperationsAccessV1.GuardNewLocationUse；权限、租户、原结果恢复和纯 UNRSV 语义保持。既有 PostgreSQL 主数据/业务锁串行化实际外部封锁；同事务最终检查捕获后续控制变化。

分配中后续明细或最终校验失败时，整个事务回滚，包括之前的库存预留、单据/明细、流水、序号、继承映射及缺料。分配先成功、封锁随后生效时，旧预留仍可按实际单据取消，重复取消不多释放。保留已授权业务事实路径；未把所有父 Owner 写入一律禁用。

见 [采用合同](../../outbound-location-control-adoption-v1.md)。无数据库迁移、依赖或前端改动。独立路由的正式预览/编辑门禁保留；本轮使用现有实际内部路由能力，不宣称正式 RoutingPolicy 完成。

## 验证与审查

25 distinct actual PostgreSQL/Program HTTP PASS;0skip;frontend unchanged。

- 8 项新实际 PostgreSQL/Program HTTP 用例覆盖同仓/跨仓合法替代、全部封锁与未知库位、不制造缺料、最终变化全回滚、后续行失败撤销前序缺料和预留、真实并发封锁、旧预留取消、当前租户/区域以及路由开关两种实际 HTTP 入口。
- 相关回归覆盖 W217 独立库存控制、原结果恢复、纯释放与盘点事实；实际出库确认/分配/拣货/发货、部分缺料及取消、存储父单据路由、库存部门继承、召回与实际缺料 Owner。
- `outbound-control-20261029T023628Z.json`：25项，15通过、10失败。10项均在准备数据时违反既有 `IX_T_Location_LocationCd` 唯一约束（不同仓库用了相同库位编码），未运行到目标行为。
- `fixture-regression-20261029T024521Z.json`：仅将两个受影响夹具改为不同的 L/L2 编码，并修正对应选择断言；10项全部通过。运行时代码未变，复用首轮另外15项通过证据。保留初次失败日志；最终25项去重、0跳过，不按断言或重跑次数计数。
- 证据根 `D:/CP6-local-test/wave218/foundation/`，`final-validation.json` 绑定实际运行时/测试源码。实际测试进程终止后才修改后端文件。无前端改动，不追加前端或全仓回归。
- 一次完整作者 SPEC/QUALITY 审查：当前候选范围、控制失败优先级、合法替代、提交前复核、整单回滚与既有取消语义。工作流/依赖与已核 W118 内容相同，每次 push/PR 前核对候选和目标。未触发、未取消 Actions；未部署。

## 台账及生命周期

W217仅归档一次，其 WP10-01/WP10-03 限定生命周期按实际最终证明关闭。W218只登记 WP10-01/WP10-19 的限定采用，不关闭完整正式任务或正式验收。

七天目标保持 ACTIVE，D1/D4 按原边界已关闭。此前65–70%缺少逐项权重和验收分母，不再作为实际完成率；2/7仅为完整阶段闭环率，不代表其他阶段的部分工作没有价值，也不能据此折算总工作量。下一步按七天原始出口逐项核验，区分本期必要交付和全产品正式验收，保留原目标，不扩张或缩小范围。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/194)，source `252d5292f4a47f5ade54eddc2374ee5ffe39b91a` → communication target `2724f6c96e474b05532c6b9a3d3b2e1e6e74eab9`；精确采用已核对，准确分支已删除、DEV-2已finish释放。Main候选在固定DEV-1保存，待正常PR、远端采用核对、定向分支清理和finish。根用户文件及缓存保留，无新增工作树。

预计45–70分钟；截至本报告Main登记耗时24.8分钟，含等待/进度汇报，不叠加Handoff；最终START至finish耗时见外部 `wave218-final-verification.json`。证明在实际finish后生成，下一真实准入只核销本轮限定生命周期。

## 未完成范围

本轮证明 native 出库新预留采用当前库位控制，尚未证明正式 Source/Root/Claim、完整占用/旧义务、全部效果 Owner、MASTER激活/08E、Transfer T01/T02/T04 或 REP/RMA 正式验收。原七天 D2/D3/D5/D6/D7 出口仍须按实际证据核验，不以此局部采用直接勾选完成；没有企业环境或生产验收声明。
