# PLT-TEST-01 目标核对

**目标 DONE（TEST_ONLY 测试支撑）**。覆盖卡 WP02-26 及其前置已 DONE；原24 AC逐项核对通过，必需修订运行场景0条。关闭本工具目标不等于增加一个正式业务功能，不提升 SC23/SC24 或任何 Owner 业务验收。

原静态设计第5节明确：S6 是未来业务 Owner 集成预期，工具不替代其实现；Required-TEST-04 仅允许已具真实适配证据的集成层结论。此次遵守该原口径，不为工具关卡改写或删除未来 Owner 的预期。

主要实际执行复用 [WP02-26 单份报告](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/DELIVERY.md)：108/108 unit/contract、6/6 真实独占 PG18.6、98/98 架构；实际命令/源码/依赖/TRX 见报告。旧 acceptance-final.json 的原 NOT_RUN 和当时局部结果保留；本次核对在 Main 台账单独登记，不追改2026-10-15历史结果。

| 原 AC | 当前证据与范围核对 | 结果 |
|---|---|---|
| TEST01-A1：四Outcome原语义及Status/Delay边界 | 原四类 outcome 与新有限 terminal 均有回归；既有 API 不被新 helper 静默替换；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST01-A2：Success/Delay必经登记内存终端且无网络fallback | 新 helper 仅 in-memory 有限终端，不接受任意网络后备；旧 DI 的 Test/CI 门由原回归核实；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST01-A3：Test/CI精确注册、同引用幂等/异实例拒绝及直接构造差距 | 精确 Test/CI 注册、同实例重复与异实例冲突回归；直接构造与生产引用限制分别核对；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST01-A4：script计数/inner计数/耗尽含义分离 | 脚本调用与内层终端计数、耗尽显式失败；不冒真实网络发送次数；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST01-A5：并发index不冒确定业务顺序 | 每 case clock/random/script 独立；本地序号不推导业务提交顺序；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST01-A6：controlled clock/取消与handler位置明确 | 虚拟时钟、取消及重试在 handler 内外的放置实际回归；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST01-A7：HTTP原重试/Unknown/幂等预期不被测试改写 | 原 HTTP retry/Unknown/幂等预期保持；PG 原回执和注入 ACK 丢失分列，真实 HTTP Owner 另有 W131/W246；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST01-A8：fixture不冒真实事务/共同fence及无生产故障UI | 库不宣称真实业务事务/fence；独占 PG 六项为真实服务器 fixture，无生产故障接口；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST02-A1：record字段/数字转换/sequence准确 | 原 recorder 兼容回归；新 ExactValue 保 CLR/原字节/任意精度，序号不当 commit 顺序；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST02-A2：原direct-chain限制与graph中间span分列 | 原 direct-chain 限制明确保留；新 Activity DAG 含中间 span、扇出、retry、receive/apply；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST02-A3：cp6-only tag检查/禁词搜索范围不冒全量脱敏 | cp6 tag 与禁词断言仅其限定范围；全输出渠道另以 A4 实际 canary 补证；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST02-A4：合成canary覆盖HTTP自动字段等真实输出通道 | CRM W132 真实 Kestrel/HTTP 自动 span、body/query/凭据/baggage/log/exception 合成 canary；最终安全导出无敏感标记且 Owner 原结果不变；[W132 实际 HTTP 导出](../W132-01/DELIVERY.md) | PASS（证据复用及当前范围核对） |
| TEST02-A5：并发listener隔离和有界capture/overflow不假Complete | 并发 listener 隔离及数量/字节/时限界限；overflow/跨 case/遗漏不可 Complete；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST02-A6：Dispose后停止捕获且不串下case | Dispose 后停止捕获；活动 span/重入/cleanup 等失败不串 case、不提前 Complete；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST02-A7：技术success/业务commit/原receipt分列 | 技术 success、PG rollback/commit、原 receipt 三轴分列，不推断实际业务应用；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST02-A8：observer-only异常不改业务、内存record不冒远端/生产证据 | observer 异常不改 terminal 返回但证据 Incomplete；内存快照不冒生产或远端保留；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST03-A1：TEST_ONLY/IsPackable=false/正式七包排除不升业务数 | 98 项架构实测含 IsPackable=false、正式包集合/生产源码排除；TEST_ONLY 不计正式业务功能完成数；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST03-A2：层级证据不相互冒充且SC23/24保原Owner | 原 SC23/SC24 及业务 Owner 引用保留；库、Host、生产采用层明确分列，未提升原247场景；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json)；原设计第5节、[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[现有 WP02-24](../../task-plans/WP02.md#wp02-24) | PASS（证据复用及当前范围核对） |
| TEST03-A3：生产Host无Testing引用/开关须真实证明 | 实际 Main W699/CRM W569 发布 deps 无 Testing；当前生产项目/锁文件/源码无测试 helper 注入。后续 CRM 项目变更仅增加两个 SQL 嵌入资源，未改变包依赖或引入 Testing；[W699 实际发布](../W699-01/DELIVERY.md)、[W569 实际 CRM 发布](../W569-01/DELIVERY.md) | PASS（证据复用及当前范围核对） |
| TEST03-A4：合成数据/隔离/授权与具体范围完整 | 原独占 PG 身份、受限 DML、隔离合成输入和停止条件有实际记录；真实客户数据不必需；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST03-A5：S6关键read/precheckpoint/fence/typed replay预期不删 | 原 S6 read/precheckpoint/fence/typed replay 预期逐条保持。W131/W246 已执行部分与 WP02-24 未完成消费明确分列，不将工具完成当三种 replay 完成；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json)；原设计第5节、[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[现有 WP02-24](../../task-plans/WP02.md#wp02-24) | PASS（证据复用及当前范围核对） |
| TEST03-A6：NotRun/取消/缺前置/cleanup失败不假Pass | 取消、输入/捕获不完整、cleanup 失败不可 PASS/Complete；初次 provision 失败和未知退出码保持；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST03-A7：原case版本/attempt/失败历史不可变 | 冻结原字节和不可覆盖 journal 保 prior failure/attempt 血缘；原目录和首次失败日志仍在；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json) | PASS（证据复用及当前范围核对） |
| TEST03-A8：Required缺证按层关闭且无业务UI/API/副作用 | Required-TEST-01..07 按证据层核对：准确输入/授权/依赖、实际适配范围、完整捕获和清理及独立原审查齐备；无高层证据则不声明高层 PASS；无业务 UI/API/生产副作用；[原矩阵及具体测试](https://github.com/GTX537/CP6.Platform/blob/5ec6e733039bfbb3b52a492a3f96f3604b8c19fb/docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json)；原设计第5节、[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[现有 WP02-24](../../task-plans/WP02.md#wp02-24) | PASS（证据复用及当前范围核对） |

## Required 与证据层核对

| 必需输入 | 本次核对 |
|---|---|
| 01 case/SPEC/Owner/原预期 | 原24条、WP02-26原矩阵及原S6预期保留；SC23/24由原Owner负责，未复制库结果 |
| 02 合成数据/隔离/终端/授权 | 原独占PG身份和受限角色、finite in-memory terminal，用户已授权模拟业务；W132独占CRM入口范围清楚 |
| 03 source/package/schema/toolchain | final-01 execution、verified-gates-final、原依赖/程序集/SDK记录；本次重核远端祖先 |
| 04 集成层真实事务/权限/fence/原receipt | 六项PG只证明fixture Owner；W131/W246真实CRM另列；未执行消费者不晋升，PLT-CTX和WP02-24继续未完成 |
| 05 bounded capture/隐私/完整性 | Capture/Fault真实故障与重入回归，W132真实HTTP自动字段canary；没有全日志脱敏或生产SLO声明 |
| 06 资源与cleanup | 原PG resource collection、smart stop和保留失败历史；cleanup失败覆盖Complete有真实测试 |
| 07 原独立审查/执行原证据 | WP02-26 reviews/spec.md、quality.md及最终TRX；W132任务审查及17项真实检查。本次单线审查不代替旧独立审查 |

## 远端采用与限制

Platform origin/main `f3fbe4be67d40641f7a8a969d74be65a95541819` 包含源码 `5ec6e733039bfbb3b52a492a3f96f3604b8c19fb` 及采用提交 `66480fe24d51c706708be971e7e9d75a684a3bc4`（[PR72](https://github.com/GTX537/CP6.Platform/pull/72)）。之后 Testing/EvidenceV1 和 WP0226 tests 无产品变更，复用输入适用。

CRM origin/main `5f09533b9168ee614a6df1f75629b39635585f86` 包含 W132 源码 `634d127181bfe1d11f51b58df0a03d2c6d144ef3` 和采用提交 `de00251a50e50e9a4750c34efb526d172569a9ab`（[PR156](https://github.com/GTX537/CP6.CRM/pull/156)），W569 源码 `9aa9c8127f7fdd84cec79cf73227533824b03c5e` 和采用提交 `5baf07e5df5d93278adce5c9dec6f76b5b482470`（[PR221](https://github.com/GTX537/CP6.CRM/pull/221)）。Main origin/main `1a1f2021c8975bd3555d579dd7312eb9d67fdfcd` 包含 W699 正常合并 PR1236。

实际 canary 测试为 CRM NativeOperationsTests.Native_final_safe_exports_exclude_request_body_query_credentials_and_exception_content_without_changing_Owner_results，原 W132 17 项唯一相关检查记录在 D:/CP6-local-test/wave132/foundation/final-validation.json。发布排除复用 D:/CP6-test-artifacts/W699-01/local-publish-final/CP6.WebApi.deps.json 和 W569-01/publish-crm/CP6.CRM.Api.deps.json；当前 src 项目/锁文件与生产源码读取核对，不依赖只看 IsPackable 推导所有消费者。

本次无产品新执行，无生产注册/部署，无远端 Actions。PG ACK 丢失是应用注入，不称 wire COMMIT 不确定；旧 recorder 的全局/有损限制仍按旧 API 保留，新 bounded/exact helper 单独 opt-in。无剩余工具目标缺口；实际业务消费者、broker、生产SLO、发布独立资格和远端保留按原目标继续。
