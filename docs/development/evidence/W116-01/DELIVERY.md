# W116-01 Main服务来源与跨仓当前检查

Main新增默认关闭的C02服务注册发布：稳定UUID/实际客户端/部门/整数角色、持久epoch与递增注册修订；相同快照/Outbox事务、停用/移除优先投递。旧实例不能复活新来源；实际Main签名入口在认证根锁内核对已提交的当前注册。独立新合同bundle1.3.0与CRM字节一致，保留1.1/1.2原始合同及接收边界，源登记升级需另行执行。

CRM [PR142](https://github.com/GTX537/CP6.CRM/pull/142)已采用 `b406807437bdb7d29f3b39415175237457d9429b`，来源 `9187a4dcb526ac2bfcad5647dc494d3290128d53`；[单份报告](https://github.com/GTX537/CP6.CRM/blob/b406807437bdb7d29f3b39415175237457d9429b/docs/development/evidence/W116-02/DELIVERY.md)。服务事实进入原生依赖图，独立只读锁函数保留七锁、actual xid、fence/state/publication行锁、期限及撤权。没有伪造用户/浏览器会话。检查CanAuthorize=false，真实签名权限与H01业务Owner最终门禁仍待组合。

231项唯一相关本地成功检查：Main118、CRM持锁39/来源基线发布26/兼容单元48；原始Main PostgreSQL服务快照实际跨仓接收、发布与持锁。必需范围无跳过；另1项历史整数角色导出检查未执行且不计通过；误选整类探索批次中止，不宣称全量通过。 联合证据D:/CP6-local-test/wave116/foundation/final-validation.json；实际失败、修复和复用范围完整保留。自有PG已停止、数据/失败日志/缓存保留。无Actions、分支保护调整、既有环境迁移或部署。

CRM准确分支删除、DEV-2已官方finish。完整公开提交/H01、持久命令重放与消费、编号和原图全事务继续；正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR92](https://github.com/GTX537/CP6.AI-Handoff/pull/92)已采用 `d43a26d8f43a463bad58d60d72dc4bb92a59fcf7`，准确分支删除、DEV-2 finish释放。 Main最终合并/finish尚待；当前START后墙钟46.40分钟，原估110–170分钟，最终按Main finish计，不叠加各仓。前期只读调查不包含在此登记计时内。

[配置/退役/升级和显式安装说明](../../identity-service-source-v1.md)。Main无DDL/迁移/依赖变化，默认关闭；新来源不是跨库即时撤权或业务权限。完整同轮审查覆盖源与合同、实际签名入口、单调修订、防旧实例、原始字节和验证适用范围。
