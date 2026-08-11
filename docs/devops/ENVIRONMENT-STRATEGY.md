# CP6 环境策略

## 目标

环境不是同一台机器上的不同文件夹，而是权限、配置、数据、审批和证据边界。CP6 的环境策略要保证低风险环境可以快速迭代，生产环境只能消费已经验证的不可变候选。

## 环境分层

| 环境 | 主要用途 | 部署方式 | 数据 | 审批 | 成功证据 |
| --- | --- | --- | --- | --- | --- |
| Local | 开发与聚焦测试 | 手工；根 Compose 可用 | 本地/合成 | 无 | 本地测试输出 |
| CI | 干净验证提交 | Azure/GitHub Agent | 测试服务 | 代码门禁 | 测试与构建结果 |
| DEV | 自动集成与部署验证 | 生产 Compose 边界 | 脱敏/受控非生产 | CI 绿后自动 | 健康、身份、Smoke、部署记录 |
| UAT | 业务验收与发布候选确认 | 同一候选 digest | 脱敏或批准的验收数据 | 业务 Owner | 验收记录与缺陷结论 |
| PROD | 用户正式使用 | Compose 试点；未来 Kubernetes | 生产数据 | 人工 Approval/Checks | 不可变部署证据与审计 |

## 运行拓扑

### DEV / 试点

优先复用 `deploy/production/compose/compose.yaml` 的边界：只包含一次性 `db-init`、API 和 Web。SQL Server、Redis、RabbitMQ/Kafka 等消息服务及 S3 兼容存储使用外部服务。

根目录 `docker-compose.yml` 可用于本地开发，但不能升级为生产输入。

### PROD / 多仓

试点稳定后再使用 `deploy/production/kubernetes/`。多副本、PDB、资源 requests/limits、拓扑分散、TLS、探针、Redis backplane 和对象存储边界继续以 R2 规范为准。

## Azure Environment 设计

建议创建三个逻辑环境：

```text
cp6-dev
cp6-uat
cp6-prod-lab
```

每个环境都应：

- 只授权指定 Pipeline；
- 保存 deployment job 历史；
- 使用独立变量组/密钥引用和部署身份；
- 记录环境 Owner 与紧急联系人；
- 禁止把明文 Secret、连接串、私钥或机器路径提交到 Git。

`cp6-prod-lab` 在学习阶段还应配置：

- 指定个人/组审批，并限制自批；
- Branch control，只允许受保护的发布来源；
- Business hours/维护窗口；
- Exclusive lock，避免并发生产部署；
- 超时、拒绝和应急绕过的审计规则。

## Agent 与网络边界

当前 Azure CI 在 `Default` self-hosted pool 上运行。YAML 没有绑定 Agent 名称，因此项目文档不把聊天中的机器名当成仓库事实。

CI Agent 只需要源码、包源和测试依赖访问。它不应拥有 PROD Secret、生产数据库写权限或生产主机管理权限。

部署建议使用以下任一受控模式，并在实现任务中固定一种：

1. Azure Environment 的专用 VM/Kubernetes 资源；
2. 专用部署 Agent，通过受限网络访问目标；
3. 受限 SSH/Service Connection，由 deployment job 调用服务器端受控脚本。

无论选择哪种模式，都不得把开发者日常账户或通用 CI Agent 直接当作生产管理员。

## 配置与 Secret

- 非敏感默认值进入受审查配置；环境差异通过 Azure Variable Group/Environment variables 管理。
- Secret 只由 Vault/Key Vault/受保护变量或 Secure File 提供，并在运行时临时渲染。
- 渲染文件只存在于临时目录，任务结束后删除；日志、Artifact 和证据不得包含明文。
- ACR/GHCR、服务器、数据库和对象存储使用独立最小权限身份。
- Service Connection 名称可以写入 YAML，凭据不能写入 YAML。

## 数据库策略

所有环境均遵守：

1. `db-init` 使用与 API 相同的镜像 digest；
2. 初始化成功后才启动/更新 API；
3. API 运行模式跳过启动时迁移；
4. 数据库迁移只前向执行；
5. 应用回退前证明旧应用兼容新 Schema；
6. 生产演练必须基于备份恢复副本，不用合成成功代替。

## 健康与发布身份

部署成功不能只检查容器“正在运行”。每个环境至少验证：

| 端点 | 验证内容 |
| --- | --- |
| API `/health/live` | 进程存活 |
| API `/health/ready` | 必需依赖可用 |
| API `/health/release` | 版本、Git SHA、镜像 digest、最新迁移 |
| Web `/release.json` | Web 版本与 Git SHA |

`/health/release` 与 `/release.json` 必须 `no-store`，不得暴露 Secret、连接串、内部主机或异常详情。

## 推广规则

```text
Registry candidate digest
        |
        +--> DEV 验证
                 |
                 +--> UAT 验收
                          |
                          +--> PROD 审批与部署
```

环境推广只改变“这个环境指向哪个已存在 digest”，不重新 Build。若配置或二进制变化，创建新候选和新证据链。

## 相关文档

- [CI/CD 架构](./CI-CD-ARCHITECTURE.md)
- [发布流程](./RELEASE-PROCESS.md)
- [Azure Pipelines 演进计划](./AZURE-PIPELINES-PLAN.md)
- [R2 生产输入与 RACI](../client/r2/01-production-inputs-raci.md)
