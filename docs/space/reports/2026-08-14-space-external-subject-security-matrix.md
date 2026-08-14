# Space Studio WP6 外部主体安全矩阵报告

日期：2026-08-14

任务分支：`codex/space-external-subject-matrix`

范围：Design V1 控制面外部主体 fail-closed 边界及仓库自动化；不替代独立渗透测试或双仓现场验收。

## 1. 边界

- 所有带 `SpaceDesignV1Contract` 的控制面 Controller 默认只允许内部主体。
- 拒绝发生在 MVC 授权阶段，早于功能权限属性、模型绑定、文件上传体读取、Controller 和服务数据访问。
- 外部主体即使被错误授予 `space:model:*`、`space:source:*` 或发布权限，也稳定返回 `SPACE_EXTERNAL_SUBJECT_DENIED` 和 `use-published-portal` 恢复动作。
- 每次控制面拒绝追加脱敏审计事件，记录组织上下文、Controller/Action 和稳定原因码；审计写入异常不能让外部主体穿透边界。
- 唯一例外是 `SpaceExternalPortalController`；该入口继续只消费 Published DTO，并由既有组织成员、组合 Grant、字段策略和对象范围校验保护。
- 例外使用显式 `AllowSpaceExternalSubject` 标记；反射守卫要求仓库中只能出现这一处 Controller 级例外，禁止 Action 级临时开洞。

## 2. 自动化矩阵

聚焦测试对 Customer、Supplier、3PL 逐一验证以下控制面：

| 控制面 | 代表操作 | 预期 |
|---|---|---|
| Draft | 读取场景 | 403，Controller 不执行 |
| Source | 列出来源 | 403，Controller 不执行 |
| Upload | 创建来源 | 403，模型绑定前拒绝 |
| Lease | 查询编辑租约 | 403，不能猜测 Lease |
| Validate | 启动校验 | 403，零任务写入 |
| Publish Preview | 读取发布预览 | 403 |
| Publish | 创建 Publish Attempt | 403，零 WMS 写入 |
| AI | 读取 Generation Run | 403 |

此外验证内部主体继续进入控制面、三类外部主体继续进入 Published-only 门户、门户例外集合不可扩张，以及审计写入失败时仍保持 fail-closed。聚焦结果：30/30 通过。

既有自动化继续覆盖：外部门户猜测 Site/Organization 返回统一 Not Found、运行态 Site/Version/Location 身份链失败关闭、组合 Grant 不发生笛卡尔越权、过期成员/授权/策略即时失效、SQL Tenant FK 与查询过滤器隔离。

完整门禁结果：

- 权限/OpenAPI/主体边界聚焦：111/111 通过。
- `CP6.Tests`：2,913 passed / 19 environment-gated skipped。
- `CP6.Space.IntegrationTests`：305 passed / 104 SQL/environment-gated skipped。
- 完整 Release solution：0 warning / 0 error。

## 3. 尚未替代的 GA 证据

- 配置真实身份提供方与三类外部测试账号执行 HTTP 负向矩阵。
- 在生产等价 SQL Server 上运行跨租户、过期授权和并发权限变更用例。
- 安全角色复核日志、告警、渗透测试和数据外发结果。
- 双仓 Pilot、真实 WMS 恢复及五方签字。

结论：仓库侧外部主体自动化边界已闭环；真实环境身份、网络与独立安全验收仍是核心 GA 门禁。
