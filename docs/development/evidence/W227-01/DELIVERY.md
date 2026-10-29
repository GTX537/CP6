# W227 退货政策发布与恢复

RMA实际页面已采用退货政策发布、停用、不可变历史与原命令恢复；政策由具备动作权限的人明确填写，不生成默认企业政策。

按可信租户/仓库/站点保存政策头和版本，复用现有RMA当前权限、字段/范围检查及事务锁。政策、发布人事实、审计与原命令同一PostgreSQL事务；旧头CAS拒绝，未知提交按原键恢复。新动作不自动授予角色。发布政策不批准退货，不写库存/财务；完整来源、可退额度、独立决定和正式IN仍待采用。

[合同与限定范围](../../rma-policy-v1.md)。

验证：Initial related build failed with CS0118 at the new stored-policy deserializer; qualified the existing RmaFormalStoreV1.Json options. Corrected affected PostgreSQL/HTTP run:8 PASS,0 failures, including two real connections competing for one policy head, current rights/scope/tenant checks, immutable history, transaction rollback and lost-commit recovery. Vue policy/application tests19 PASS and vue-tsc PASS. Initial failure retained. Unchanged W224-W226 application/source evidence reused; no full-suite rerun, Actions, real-browser or target-environment acceptance.

审查：One concentrated full task diff review covers external input validation, trusted internal/stored contracts, current permissions/scope, policy head CAS under existing RMA transaction locks, immutable versions and original results, no implicit role/policy defaults, private pending requests and stale identity completion. Policy registration is an explicit RMA Owner extension, not an alternative approval or full RMA-C03 adoption. No additional qualification/hash wrappers or swallowed failures.

本地证据：D:/CP6-local-test/wave227/foundation/ 下 related-20261029T081306Z.json, corrected-related-20261029T082234Z.json, web-related-20261029T081508Z.json, web-typecheck-20261029T081518Z.json及对应log/TRX。初次编译失败记录保留；修正后实际8项PG/HTTP和19项Vue通过。Vue使用模拟API，数据库与HTTP使用隔离PostgreSQL；不作为真实浏览器、既有环境迁移或完整业务验收。

预计60–100分钟，截至快照登记30.48分钟。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/203)已合并，准确分支删除且DEV-2释放；Main待正常PR、远端采用和finish。最终生命周期见wave227-final-verification.json，固定目录/用户资料/缓存保留。

七天目标：D1/D4已关闭，D2/D3/D5/D6部分采用，D7最终集成未完成。28.6%只表示2/7阶段关闭；65–70%工程比例缺少可靠分母已撤回。原35–55小时与后来剩余40–60小时不能继续作为当前完工承诺；缺少对当前剩余实现及真实外部输入的有效排期，总剩余工时保持未估定。

下一步：Adopt the real policy and proven current sources in independent return authorization, then formal inbound; preserve all original seven-day exits.
