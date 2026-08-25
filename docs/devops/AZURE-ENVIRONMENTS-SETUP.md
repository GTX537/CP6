# Azure DevOps Lab Environments 设置

本页只建立 CP6 学习用的逻辑 Environment。部署仍由本机 self-hosted Agent 执行，
因此创建时选择 `None`，不注册 Azure VM、Kubernetes 或其他付费资源。

## 当前外部状态

仓库已经具备与以下 Environment 对应的本机 Docker 环境合同：

```text
cp6-dev
cp6-uat
cp6-prod-lab
```

2026-08-11 已通过用户提供的 Azure DevOps `Pipelines → Environments` 列表截图确认三个逻辑
Environment 均已创建，当时均为 `Never deployed`。2026-08-25，`CP6 DEV CD` Run #95 已向
`cp6-dev` 写入首次成功部署历史；`cp6-uat` 与 `cp6-prod-lab` 仍未部署。

截图只证明名称和当时的部署状态，未展示 Resource 详情、Pipeline permissions 或 Approvals and checks。
DEV deployment job 已完成外部验收：只有 `CP6 DEV CD` 获得 `cp6-dev`、`CP6-Deploy` 和
`cp6-dev-secrets` 的定向权限；Environment 已配置 Exclusive lock，未启用全局 Open access。

## 创建步骤

进入 Azure DevOps 项目后，依次执行：

1. 打开 `Pipelines` → `Environments`。
2. 选择 `Create environment`。
3. Name 输入 `cp6-dev`。
4. Description 输入 `CP6 local Docker DEV - automatic integration deployment`。
5. Resource 选择 `None`，然后创建。
6. 对 `cp6-uat` 和 `cp6-prod-lab` 重复以上步骤。

建议描述：

| Environment | Description |
| --- | --- |
| `cp6-dev` | `CP6 local Docker DEV - automatic integration deployment` |
| `cp6-uat` | `CP6 local Docker UAT - manual acceptance gate` |
| `cp6-prod-lab` | `CP6 production rehearsal lab - not real production` |

## 本阶段权限设置

- 三个 Environment 暂不添加 VM/Kubernetes resource。
- 创建独立 Release Pipeline 前，不授予任意 Pipeline 使用权。
- Release Pipeline 创建后，在每个 Environment 的 Security/Pipeline permissions 中只授权该 Pipeline。
- `cp6-dev` 不配置人工审批。
- `cp6-uat` 和 `cp6-prod-lab` 的审批、Branch control 与 Exclusive lock 在 CD 任务中配置。
- 单人学习阶段可以允许本人批准 PROD-LAB；真实生产必须改为另一位批准人并禁止自批。

## 验收清单

- [x] Azure Environment 列表出现 `cp6-dev`。（2026-08-11 截图验证）
- [x] Azure Environment 列表出现 `cp6-uat`。（2026-08-11 截图验证）
- [x] Azure Environment 列表出现 `cp6-prod-lab`。（2026-08-11 截图验证）
- [ ] 三者 Resource 均为 `None` 或空列表。
- [x] 列表中没有 `cp6-prod`，避免把本机实验环境误标为真实生产。（2026-08-11 截图验证）
- [x] Environment 本身没有录入 SQL 密码、JWT、GHCR token 或个人 Windows 凭据；DEV 所需 SQL 凭据仅保存在锁定且定向授权的 `cp6-dev-secrets` Variable Group。
- [x] `cp6-dev` 只授权给 `CP6 DEV CD`，并配置 Exclusive lock；Run #95 写入成功部署历史。

环境创建本身不等于部署完成。只有后续 deployment job 写入部署历史并通过
live、ready、release identity 和迁移门禁后，才能声明某个候选已部署。
