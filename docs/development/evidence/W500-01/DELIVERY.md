# W500-01 询盘读取防枚举

本轮补 R-029 的内部读取等价性。真实签名、当前 IAM 和业务授权下，合法站点／表单的列表与详情可读。切换为仍有读取权限的两个不匹配站点／表单对后，列表为空。使用同一当前主体读取未知 ID、实际跨租户 ID 和实际同租户越范围 ID，三个详情响应均为 `404 crm.submission_not_found`，`application/problem+json`、no-store，字段错误为空且不可重试。正文去除随机 trace 和 correlation 后相同，均无资源标识、个人信息、ETag、Set-Cookie 或展示授权时间头。

数据库回读覆盖两个租户的 Submission、消费、请求效果、配额和生命周期出站计数。合法本租户记录版本与封印也保持原值。场景使用模拟业务和身份，真实 .NET 8、Kestrel HTTP、隔离 PostgreSQL 18.6。`enumeration-01.trx` 一项通过、0 跳过。原始结果和命令在 `D:/CP6-test-artifacts/W500-01`，自有 PostgreSQL 已核验停止。

公开回执复用 W117 的 12 个实际 404 边界，包括错浏览器、凭据、租户、站点、过期、撤销和匿名化。W120、W121 原授权、跨租与同租户组合，W491 的真实旧封存行期限，以及 W482 的跨对象 Job／下载凭据结果按各自实测范围引用。原错误码与版本优先级继续由 AC-INQ-034 核对。 [W117](../W117-01/DELIVERY.md) · [W120](../W120-01/DELIVERY.md) · [W121](../W121-01/DELIVERY.md) · [W491](../W491-01/DELIVERY.md) · [W482](../W482-01/DELIVERY.md)

CRM #189 采用源 `96297d5162b9f912a4b97e75613d9b9ce18eed30`，核对远端 `58ececc839ea9eb37598c6a37bdbf98813ff4e1b` 及相同已验证源码树。一次集中审查覆盖已授权正向入口、零持久副作用和三类同形拒绝。CRM 本地与远端任务分支已清理，DEV-2 干净且 W500 席位已释放。主仓的采用与收尾见本窗口 PR。 [CRM #189](https://github.com/GTX537/CP6.CRM/pull/189)

采纳 AC-INQ-029，原询盘 49 PASS / 11 NOT_RUN。继续核夹具一致性及原赢家事件事实。Actions 运行和取消均为 0，部署 0。
