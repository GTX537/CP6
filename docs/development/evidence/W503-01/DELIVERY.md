# W503-01 H01 下游效果和授权边界

## AC-INQ-031 原生业务效果

真实公开自动受理和人工放行均创建一条 `New` Lead，`ConvertedOpportunityId` 为空。待审核和人工拒绝没有创建 Lead。原键重放恢复原结果，同键异体返回冲突。逐次回读租户内 `Opportunity`、阶段历史、ERP 链接、集成处理及外部销售记录，实际均为空。Outbox 只出现该分支允许的原接收、放行、拒绝和 Lead 创建消息，重放和冲突后消息标识及主题保持。现行 H01 的商务输出边界因此止于 Lead。Main 报价和订单的独立物理表未在本轮直接回读。 [W117 原公开和 Main 接线](../W117-01/DELIVERY.md) · [W121 原审核](../W121-01/DELIVERY.md)

## AC-INQ-032 当前授权和 Owner 边界

FX20-S01 的真实内部审核在电话值为空且恰缺电话授权时拒绝。FX21 七种真实匿名提交分别移除 email 授权、破坏内层签名、延长内层寿命、持久撤销签名键、改签名主管部门、在空值时移除 phone 授权、在合法非空时移除 company 授权。各分支均返回通用 `403 crm.permission_denied`，字段错误和重试时间为空，无 Cookie 或 ETag。数据库中原 Attempt 仍在，配额、消费、命令、业务、审计效果及 Outbox 均无新增。外层签名、当前主体和字段范围由真实入口检验。

沿用已实测的 Main IAM 签发／撤权、配置预览和生效边界以及四处置隐私 Owner。原 CRM-LEAD、SYS-IAM、SYS-PRIV 各自保持其实际业务和事务权限。正式 SYS-PRIV／SYS-FILE 的共同服务采用仍由 WP03-04 核验。 [W117](../W117-01/DELIVERY.md) · [W255](../W255-01/DELIVERY.md) · [W482](../W482-01/DELIVERY.md) · [W496](../W496-01/DELIVERY.md)

## 验证与采用

`signed-fences-02.trx` 共 13 项通过，0 跳过。本地 .NET 8、Kestrel 与隔离 PostgreSQL 18，使用模拟业务和签名身份。首轮编译的 xUnit2031 断言诊断已修复，日志保留，实际运行和资源停止记录在 `D:/CP6-test-artifacts/W503-01`。

CRM #192 合入源 `b74540b9dee324a33b42b6a5ce9b0ea497f5c26f`，核对远端 `2419f61303a3261f77bcf7724757143cb04c173b` 和相同已验证源码树。一次集中审查覆盖两份测试、真实权限和业务零写范围。CRM 本地及远端分支已定向清理，DEV-2 的 W503 已释放。采纳 AC-INQ-031/032，原询盘 54 PASS / 6 NOT_RUN。Actions 运行与取消均为 0，部署 0。继续三维限流、原键恢复和政策参数。 [CRM #192](https://github.com/GTX537/CP6.CRM/pull/192)
