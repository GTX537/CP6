# CP6 本机 Docker 发布实验环境

这里提供三个相互隔离的本机发布实验环境：

| 环境 | Compose project | Web | API | RabbitMQ 管理页 | SQL database |
| --- | --- | ---: | ---: | ---: | --- |
| DEV | `cp6-dev` | 18080 | 19991 | 16072 | `CP6_DEV` |
| UAT | `cp6-uat` | 28080 | 29991 | 26072 | `CP6_UAT` |
| PROD-LAB | `cp6-prod-lab` | 38080 | 39991 | 36072 | `CP6_PROD_LAB` |

PROD-LAB 只用于学习发布、审批和回滚，不是真实生产环境。它使用
`ProductionLab` ASP.NET Core environment，不能用来绕过或替代 CP6 的
Production 配置验证。

## 安全边界

- SQL Server 继续使用宿主机 `KOUSQLSERVER`，数据库和登录账号按环境隔离。
- `db-init` 只接收对应环境的 migrator 凭据；API 只接收 runtime 凭据。
- Redis、RabbitMQ、Kafka、Docker network 和 volumes 由 Compose project 隔离。
- RabbitMQ/JWT 实验密钥保存在当前 Windows 用户可解密的 DPAPI 文件中。
- Compose 运行前把三个最小权限 env 文件写到系统临时目录，命令结束后删除。
- 明文 Secret 不进入 Git、构建产物或部署日志。容器 inspect 仍能看到注入的环境变量，
  因此该实现只属于本机 Lab；云环境后续迁移到受管 Secret 服务。

## 第一次初始化

在仓库根目录运行：

```powershell
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment dev -Action Initialize
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment uat -Action Initialize
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment prod-lab -Action Initialize
```

初始化会检查已有 SQL DPAPI note、发现 `KOUSQLSERVER` TCP 端口，并创建：

```text
%USERPROFILE%\Documents\CP6-Secrets\docker-lab-secrets.dpapi.clixml
```

## 构建一次

先启动 Docker Desktop，然后只构建一组本机 Lab 镜像：

```powershell
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment dev -Action Build
```

DEV、UAT、PROD-LAB 默认都使用这两个镜像：

```text
cp6-api:lab-local
cp6-web:lab-local
```

## 部署与推广

```powershell
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment dev -Action Deploy
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment uat -Action Deploy
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment prod-lab -Action Deploy
```

每次部署都按以下顺序执行：

1. 启动并等待 Redis、RabbitMQ、Kafka 健康；
2. 使用 migrator 账号运行一次 `db-init`；
3. 迁移成功后使用 runtime 账号启动 API；
4. API 健康后启动 Web；
5. 验证 live、ready、API release identity 和 Web release identity。

常用操作：

```powershell
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment dev -Action Status
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment dev -Action Logs
.\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment dev -Action Stop
```

`Stop` 只停止容器，不删除 volumes。当前工具故意不提供清库或删除 volume 的快捷命令。

## 配置验证

不启动 Docker daemon 也能验证三个 Compose 合同：

```powershell
.\scripts\test-cp6-lab-environment-contract.ps1
```

若宿主机 SQL TCP 端口不能自动发现，可显式传入：

```powershell
.\scripts\Invoke-Cp6LabEnvironment.ps1 `
  -Environment dev `
  -Action Deploy `
  -SqlPort 50286
```
