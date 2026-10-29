# W217-01 独立库存写入采用当前库位控制

COORD 单人；PostgreSQL。基线 `c97b98ac9cac046dacfd965853fea7d2fecc6098`。本报告写入时 Main 待正常 PR/finish；不能把候选验证写成远端采用完成。

## 实际改动

普通独立 Apply/Move 的兼容服务和 v1/v2 原操作入口，在原权限/事务范围内检查当前 native Location.IsBlocked；MOVE 检查两端，提交前再次复核。缺当前有效库位返回 LOCATION_CONTROL_UNAVAILABLE，封锁返回 LOCATION_OPERATION_BLOCKED，均为409。客户端 RelatedType/RelatedNo 文本不能豁免。

原业务/主数据 PostgreSQL 锁保持至提交，真实封锁与库存效果有确定先后。失败回滚数量、两腿流水、序号、部门创建/继承、审计、原操作与 outbox。已提交原键继续按当前读取权恢复，不重复执行或因后来封锁丢失原结果。

UNRSV 仅减少原预留，保留既有数量/授权约束；内部 Parent Owner 的盘点批准事实路径继续按其自身权限与准确单据处置。未把本次检查扩散为全部收货/发货/旧义务 Writer 的统一禁用，也不声称这些路径已取得完整 MASTER 保护。

实际边界见 [采用合同](../../stock-location-control-adoption-v1.md)。无新数据库迁移、依赖或前端改动。旧缺库位引用不再作为普通新使用的可用证明；需真实主数据补齐，库存命令不会自动造库位。

## 验证与审查

69 distinct actual PostgreSQL/Program HTTP PASS;0skip;frontend unchanged。

- 8 项新 PostgreSQL/Program HTTP 行为：所有普通新使用类型、兼容/v1/v2 MOVE 两端、多封锁/解除残留、创建映射回滚、提交前控制变化全回滚、真实并发先后、原键恢复与读取撤权、纯释放、已授权盘点事实路径、当前租户/区域和缺库位、实际HTTP409及恢复。一个用例可以覆盖数个相关边界，不按断言数累计成果。
- 原有库存原结果、创建/部门继承、保管/估值精度、负库存策略、盘点及区域报表相关回归。两次执行去重69项，最终均有通过证据，0跳过。
- `stock-control-20261029T020500Z.json`：62项，55通过/7失败。失败为新 native 夹具与旧重复仓库冲突（4项）、将封锁原记录误计为库存原记录（2项）、部门范围辅助函数同时收窄主数据范围而未绑定（1项）。
- `fixture-regression-20261029T021120Z.json`：只修复测试夹具后，受影响及关联19项全部通过；包括3处共享夹具导致的区域测试调整。运行时代码未变，复用首轮未受影响通过结果。失败日志原样保留。
- 证据根 `D:/CP6-local-test/wave217/foundation/`，`final-validation.json` 绑定实际运行时/测试源码 blob。构建/测试进程结束后才改后端文件。前端无改动，未另跑前端或无关全仓回归。
- 本任务一次作者 SPEC/QUALITY 完整 diff 审查；覆盖新命令与原结果的区分、两端及最终控制、权限/租户范围、纯释放和事实路径边界。工作流事件/依赖沿用已核 W118 相同内容；每次 push/PR 前核对候选与目标，无自动 Actions。未触发、未取消远程运行。

## 台账及生命周期

W216仅归档一次，其 WP10-01/WP10-10 限定生命周期按最终证明关闭。W217登记在 WP10-01/WP10-03 的限定采用字段，不关闭完整任务或正式验收。七天目标 ACTIVE；D1/D4 按原边界完成，其他阶段及最终集成保持未完成，65–70%仅为低置信工程估算。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/193)，source `3ce6cff8fbc1f09214b51c77ce56529ddcd303f4` → communication target `cc7778f907e089345686a39b5ffb5ca220ffaf61`；精确采用已核对，准确分支已删除、DEV-2已finish释放。Main候选在固定DEV-1保存，待正常PR、远端采用核对、定向分支清理和finish。根目录用户文件及既有缓存保留，无新工作树。

预计45–70分钟；截至本报告Main登记耗时18.8分钟，含等待、不叠加Handoff；最终START至finish耗时见外部 `wave217-final-verification.json`。该证明在实际finish后生成，下一真实任务准入时只核销本轮限定生命周期。

## 剩余范围

仅证明普通独立 native Stock 新使用采用本域聚合控制。尚不证明正式 Stock Root/Claim/准确来源能力、完整占用/旧义务、所有效果 Owner 保护、MASTER激活/08E、Transfer T01/T02/T04、REP/RMA 或真实企业环境验收。D3正式工单/排程、D5真实宿主/传输、D6实际政策登记及D7最终跨仓集成仍保留；未部署或修改分支保护。
