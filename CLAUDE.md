# CP6 Agent 入口

仓库范围的开工、验证、PR 及席位规则见 [AGENTS.md](AGENTS.md)。当前采用单卡串行，使用已登记的固定工作区。

## 部署相关入口

| 任务 | 入口与行为 |
| --- | --- |
| 转存 DEV 运行包 | [Azure CI 制品桥](azure-pipelines.yml)仅手动运行，从成功的 GitHub 工作流下载对应源码 SHA 的制品，校验后转存。 |
| 在本地 DEV 发布 | [DEV CD](azure-pipelines-dev.yml)支持手动运行，也监听指定上游的 `main` 运行完成。自动部署受 `CP6_DEV_AUTO_DEPLOY_ENABLED` 控制，实际环境、锁和操作命令见 [DEV 操作手册](docs/devops/DEV-AUTOMATIC-DEPLOYMENT.md)。 |
| 管理演示站与 Tunnel | `cp6.uk` 的演示环境见 [demo 手册](deploy/demo/README.md)。部署、Tunnel 切换和数据库恢复须取得用户的明确授权。 |
| 生成或推广生产候选 | 遵循 [DevOps 入口](docs/devops/README.md)、[发布权威决策](docs/devops/adr/ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md)和 [R2 主规范](docs/client/r2/README.md)。 |
