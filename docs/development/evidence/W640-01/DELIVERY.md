# W640-01 旧 OA 超时扫描的原生提交授权

2026-11-15 UTC。Main 基线 `2cae251e90fecc2cd6d49e0297c8c71a51624d25`，实际受测产品及测试源码 `427c38c46119d5957598270a15fcea889c98ca13`。隔离 PostgreSQL 18.6、Main Kestrel 与本机连接器使用模拟租户、预算、账号和旧流程发布记录。 [缺陷 #1164](https://github.com/GTX537/CP6/issues/1164) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012) · [W638 正对照](D:/CP6-test-artifacts/W638-01/DELIVERY.md)

## 事务与原身份

`WfTimeoutScanWorker` 沿用现有逐租户 scope。PostgreSQL 的 `WfTimeoutService` 对每条候选待办开启原生事务，取得 Main 共同根后重读同租户待办、实例和固定的已发布版本。任务、处理标志、历史、原业务回调与通知 Outbox 一起提交。最后一次保存后按当前墙钟核对租户期限，扫描时刻只用于选出到期任务。旧系统审计身份、原状态处理和 V2 分流沿用已有合同。

原生自动同意和驳回复用事务内办理。错误边可把同步 WebAPI 节点停泊为现有 `DeferredWebApiStart`，让同一个已提交作业持原实例、令牌、发起人和幂等键进入现有的派发及完成入口。

## 实际结果

- 基线两项均命中预期缺陷。租户到期后，旧扫描仍提交原待办和预算驳回及终态通知。旧错误边直接向本机模拟连接器发出一次调用，当时尚未生成持久作业。首轮编译的夹具表达式参数错误也已保留。 [实际基线](D:/CP6-test-artifacts/W640-01/baseline-native/results.trx) · [最初编译](D:/CP6-test-artifacts/W640-01/baseline/build.log)
- 升级和驳回各通过两种真实签名暂停顺序。暂停先提交时，扫描等待管理事务的 Main 根，随后写集保持一致。业务先提交时，权限写事务等待超时事务的根，随后只读到一次完整业务效果。恢复后同一原待办仅在必要时处理一次，重复扫描的全写集保持一致。业务先行的升级在同一 FlowKey 有后续驳回版本时仍按原发布版本 1 派给新接收者。 [签名锁链与完整写集](D:/CP6-test-artifacts/W640-01/verified/permission-consumption-verified)
- 签名管理设置租户期限后，扫描在 `Wf_Notification` 插入处进入真实锁等待，墙钟越过期限才释放。处理数为 0，原任务、流程、预算、通知和历史均逐行一致。W618 的真实凭证回归继续拒绝系统自动审批充当第二个人工审批人，原真实审批人可以完成。V2 独立租户 scope 与当前服务权限的原实库用例通过。 [过期证据](D:/CP6-test-artifacts/W640-01/verified/permission-consumption-verified/legacy-timeout-expiry.json) · [主要 TRX](D:/CP6-test-artifacts/W640-01/verified/results.trx)
- 错误边提交阶段的本机请求为 0，原任务办结标志与一个 `DeferredWebApiStart` 一同保存。现有服务作业扫描领取后向本机发送一次 HTTP，请求键等于原作业 ID，真实执行器仍接收原发起人和执行 actor。原作业尝试一次并成功，令牌完成，预算回调提交，重复扫描的全写集一致。第一轮派发夹具从 JSON 读取的本地 `DateTime` 被 UTC 原生参数校验拒绝，定向复测改为直接读取同一原作业的 PostgreSQL UTC 值。 [定向 TRX](D:/CP6-test-artifacts/W640-01/connector-final/results.trx) · [作业形成及派发](D:/CP6-test-artifacts/W640-01/connector-final/permission-consumption-connector-final)

主要定向执行中 14 项通过，唯一的夹具日期失败经定向复测通过，合计 15 个不同用例通过，零跳过。补充三个旧 SQLite 错误边用例在建库时缺少 `Latin1_General_100_BIN2`，用 W638 的已绑定原程序集实跑得到相同失败，均未进入产品行为。两版相关模型及测试夹具源码相同。历史非 PG 的七项 InMemory 超时测试已包含在主要定向结果。 [SQLite 当前结果](D:/CP6-test-artifacts/W640-01/historical-error-edge/results.trx) · [SQLite 原程序集结果](D:/CP6-test-artifacts/W640-01/historical-reference/results.trx)

## 本地发布检查

Debug PostgreSQL 本地发布包含 310 个逐文件 SHA-256，七份运行程序集与受测输入逐项一致。复用实跑数据重启隔离 PostgreSQL `57140` 和发布宿主 `57148`。实际活性 `200`、匿名送审 `401`、签名预算读取 `200` 并返回原 `Rejected` 版本和原驳回理由。所用进程已停止。本项涵盖旧超时扫描，提交后独立父流程恢复由 W641 验收。Actions 与环境部署均为零。 [本地发布证据](D:/CP6-test-artifacts/W640-01/publish-verified-smoke.json) · [文件清单](D:/CP6-test-artifacts/W640-01/publish-sha256.json)
