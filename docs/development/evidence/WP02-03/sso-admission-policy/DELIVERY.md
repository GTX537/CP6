# W27-02 / #337：SSO 首次身份准入政策与配置原操作恢复

2026-10-19；COORD 单聊天。基线 `098264a08ee25e3f044d202e0e5ade3c98b6dc3a`，原工作目录分支 `codex/wave27-02-sso-admission-policy-20261019`。本窗 Ready to merge: Yes，作者集中 SPEC/QUALITY 及局部复查无剩余阻断。#337 待远端集成核对后关闭。完整 WP02-03 和真实业务验收未提升。

## 已实现

沿原 `sys-sso-config:query/edit` 与 Main IAM Owner，在 PostgreSQL 配置边界新增 `SsoIdentityAdmissionPolicyV1`：稳定 policyRef、每次批准新 version、精确 Authority/issuer/client、EmailClaim 与 verified-email 语义、邮箱身份权威的显式确认、初绑/JIT 开关及批准的当前启用同租户角色。无策略时不批准首次身份写；无配置投影的 AutoProvision 默认关闭。已有历史配置保留真实值，不能把旧 true 当作批准。

PUT 保留原字段/空密钥保密文合同，新增 expectedConfigVersion、expectedPolicyRef/version、operationId、reason、preserve/approve/revoke。配置版本采用现有防 ABA 的认证配置版本；租户其他认证政策变更可保守地产生 412。所有读取和写入在现有 PostgreSQL 认证根内复核当前租户、原认证证据、会话、账号和有效权限；中间件之前的检查不能代替等待根之后的检查。批准者与回执属当前原身份；受限会话、过期令牌和代入身份不能生成归于目标用户的批准记录，需要原身份登录办理。

改变 Authority/client/EmailClaim 必须明确重批或撤销，不能把旧批准静默挪给新 IdP。启用新 JIT 或更换非空默认角色须当前批准及当前启用同租户角色；撤销/关闭可用于失效策略的恢复。普通 PostgreSQL Upsert 不能旁路配置 envelope。原非 PostgreSQL 历史路径仅保留，不新增实现或双库验收。

配置、政策、强制 Sys_SecurityLog 和原操作回执在同一 Main PostgreSQL 事务提交。任何审计失败回滚配置/政策/回执，取消或提交未知不声称成功。通用 OperLog 不序列化该入口的密钥输入；投影只含 hasClientSecret。原请求比对值以 DataProtection 保护摘要，不保存明文密钥或可供离线猜测的裸摘要。

私有前向迁移 `20261019152000_SsoIdentityAdmissionPolicyV1` 建立准入政策和不可变原操作表；实际策略 DML 推进 tenant 认证 revision，使在途旧 SSO 政策失效。现有 config/user/tenant writer 已有根门，本窗补齐实际 role、membership、action、menu DML。菜单 IdentityChange fanout 先取同根再取已有表锁，保留当前权限关联发现和 Outbox 合同；真实并发回归验证两种顺序。

`GET operations/{id}` 只按当前 tenant/原 actor 回查；不存在为 UNKNOWN。`POST operations/{id}/close` 能先持久封闭未到达原操作，迟到同 ID PUT 被拒绝；已提交只返回真实回执，不撤销成功变更。关闭也与强审计同事务。结果未知不得换 ID 重发。回执不可随意清理，否则迟到请求会丧失关闭屏障。

实际 Vue 管理页显示当前批准，approve 必须重新显式确认 issuer 邮箱权威和原因；角色列表只列启用项。配置 CAS 使用加载时版本，保存成功提示重新登录，不拿已失效的会话盲目重读。失败后只提供查询/关闭原操作，重载和跨标签页保留原编号，密钥草稿清空且从不写入本地存储。Web Locks 串行同 tenant/actor 写入；等待期间身份 epoch 变化拒绝旧草稿，避免借用新身份发送。没有可靠存储/锁则不发出写入。

## 验证及复用

原始 JSON/TRX/log 在 `D:/CP6-local-test/wave27/foundation`，所有 PostgreSQL 为自有隔离 PG18，实际 Program/HTTP；浏览器为本机 Chrome 的实际 Vue/Element Plus 页面加受控 HTTP 夹具，不冒企业 IdP 或真实 DEV。

| 证据 | 结果与范围 |
|---|---|
| sso-config-red-20261019T150111Z | 2 个真实失败：过期版本仍 200、审计失败仍成功提交 |
| sso-config-green-20261019T151355Z | 8 个旧配置/密钥合同通过；2 个 PG 因新迁移表名拼写失败，修正并保留失败记录 |
| sso-config-cas-audit-20261019T151705Z | 原 2 个 PG/HTTP 失败路径通过 |
| sso-config-boundaries-20261019T151925Z | 41 通过、0失败/跳过：审批/CAS/回执/关闭/权限竞争/实际 DML、受影响原认证状态、敏感操作日志 |
| sso-config-policy-review-20261019T152316Z | 15 通过、0失败/跳过：政策引用 CAS、配置+政策+审计+回执回滚和原菜单 fanout 并发/隔离级别回归 |
| sso-config-final-review-20261019T152950Z | 最终 4 通过：政策单独变化也使旧会话失效、等待后撤权、CAS、强审计回滚；其余输入未改证据直接复用 |
| sso-config-components-reviewed | Vitest 4.1.11 的 9 个行为/组件用例通过，含未勾选不批准、未知结果跨挂载、双调用只发原操作、身份变更拒旧草稿 |
| sso-browser-module-fixture / sso-browser-reviewed | 两个不同 Chrome 场景有效通过：未知保存跨页/重载只查询关闭原操作；实际表单显式勾选、原版本和一次保存。后者是选择器修正后的局部复查 |
| sso-typecheck-reviewed | `vue-tsc --build` 退出 0；测试 mock 的可空访问已修正 |

浏览器最初的直接 node_modules 模块夹具没有挂载，改为由 Vite 正常解析的 e2e 模块；首次 Element Plus 隐藏原生 checkbox 的选择器失败，改为用户实际可点击的 label 后通过。期间 Python 输出编码失败保留，原执行结果 JSON/log 可核。页面恢复后截图已人工查看。未将失败执行相加为用例目标，也未重跑已有效的旧结果。原 NU190x/编译静态警告保留，不宣称漏洞扫描或全仓回归通过。

集中作者审查覆盖权限与认证前后顺序、CAS/ABA、批准归属及角色、密钥边界、事务/不可变回执/关闭竞争、实际 writer 和菜单锁序、跨页身份与恢复。审查补齐 expectedPolicyRef、等待锁时的前端身份检查及政策单独变化回归；只对修改部分复查，未使用额外代理。

## 采用和剩余范围

新迁移仅在本窗自有隔离 PG 安装；应用采用需先迁移。真实 tenant 的 issuer/角色/批准 Owner 仍 UNPROVEN，本窗没有替其填值或批准。新增首次邮箱初绑/JIT 实际消费仍由下一窗 W27-03 完成；本窗不声称新身份已能创建。

自有 PG 根均已停止、无 postmaster.pid，实际 HTTP 由夹具释放；Web58427 的 PID36996已核归属后停止。复用原唯一工作目录、node_modules junction、wave18/.NET/NuGet缓存，无新工作树、代理、依赖安装、数据库副本或清理旧材料。没有 Actions 取消项、触发配置变更或运行；既有环境/真实DEV/企业IdP/生产发布未执行。14包/125目标/375任务/247运行NOT_RUN保持原范围。

正常合并并回读后继续 W27-03 及本轮收尾，持续 goal 不结束。
