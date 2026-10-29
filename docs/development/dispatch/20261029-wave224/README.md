# W224 正式退货申请入口

COORD单人；预计90–150分钟，固定DEV-1/DEV-2复用。

Implement formal RMA application intent lifecycle using trusted scope and signed immutable source selections, actual fulfilment snapshots, exact request ranges, current authority and original-operation recovery.

Application creation/submission grants no authorization, stock or financial effect. Source coverage and entitlement remain explicitly PARTIAL/UNKNOWN; no fabricated policy, zero quantities or independent approvals. UI adoption and full authorization/receipt remain subsequent direct work.

本轮实施正式申请意图：当前可信范围、服务端签发不可变来源选择、准确数量范围、创建/编辑/提交/撤回及原操作恢复。复用现有来源与数据保护机制；权益缺口保持UNKNOWN，无库存/财务效果。相关PostgreSQL/HTTP验证和一次集中审查，正常PR/分支清理/finish。页面采用与完整授权/收货继续后续直接任务，不关闭D2或完整WMS-RMA验收。

实现发现旧发货快照仅有客户代码，不能反推历史客户UUID。本轮扩展同一发货事务的显式 `captureReturnApplicationSource`，捕获实际客户身份版本；不回填旧快照。相应串行编辑范围包含发货请求、履约快照和 OutboundService 入口。正式合同与限定范围见[申请意图说明](../../rma-formal-application-v1.md)。

真实事务验证发现 W206 草稿仓库尚未激活，不能拿来实际发货。本轮支持受信任服务端对已有业务仓库配置确切租户/仓库/站点坐标；保留草稿禁用与当前权限检查，不用测试 SQL 伪造激活。串行范围包含该入口的宿主配置注册。

## 交付快照

[报告](../../evidence/W224-01/DELIVERY.md)。登记46.76分钟；Handoff已释放、Main待正常PR/finish。实际HTTP、精确区间和事务恢复验证通过，单人一次集中审查及定向修正复查。
