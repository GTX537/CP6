# CP6 WMS R2 生产就绪主规范

## 1. 文档地位

本目录是 CP6 WMS R2 的唯一规范源。旧入口
`docs/client/R2-RELEASE-INPUTS.md` 与 `docs/client/R2-ROLLOUT.md` 仅保留导航，
不得继续维护平行规则。发生冲突时，以本目录、Schema 2
`release-manifest.json` 以及仓库中的可执行门禁为准。

首个候选版本为 `v1.0.0`，之后使用语义化版本。实施顺序固定为：

`规范统一 → P0 代码加固 → 签名制品 → Compose 试点环境 → R2A MOVE → R2B 序列/LPN → Kubernetes 多仓推广`

## 2. 已锁定架构决策

- 试点使用 `deploy/production/compose/compose.yaml`；多仓正式环境使用
  `deploy/production/kubernetes/`。根目录 `docker-compose.yml` 和 `k8s/`
  仅供开发，禁止作为生产输入。
- SQL Server、Redis、消息服务和 S3 兼容存储均为外部生产服务。
- API/Web 镜像只能用 `repository@sha256:digest` 部署。
- 候选流水线只由指向当前 `main` 提交的受保护 `vX.Y.Z` Tag 触发。
- 原生客户端签名由受控 Windows 自托管 runner 执行；部署由受保护
  GitHub Environment 内的环境自托管 runner 执行。
- 证据存入启用服务端加密、版本控制和 Object Lock 的 S3 兼容存储。
- 仓库开关通过 OA/WF `WMS_FEATURE_FLAG_CHANGE` 双人审批，不允许直接切换。
- 数据库只前向迁移；R2B 为单仓、逐产品、不可逆、失败后前滚修复。

## 3. 规范地图

| 规范 | 负责内容 |
| --- | --- |
| [01 生产输入与 RACI](./01-production-inputs-raci.md) | 环境输入、Owner、日期、密钥引用、证据和审批 |
| [02 签名与候选制品](./02-signing-candidate-artifacts.md) | Tag、签名、镜像、SBOM、清单和归档 |
| [03 Compose/Kubernetes 部署](./03-compose-kubernetes-deployment.md) | 拓扑、初始化、TLS、探针、回滚和部署证据 |
| [04 R2A MOVE 试点](./04-r2a-move-pilot.md) | 设备、负载、恢复、每日对账和退出条件 |
| [05 R2B 序列/LPN](./05-r2b-serial-lpn.md) | 预检、转换、永久锁定、差异和前滚修复 |
| [06 运营与逐仓推广](./06-operations-rollout.md) | Go/No-Go、停用阈值、恢复演练和波次推广 |

## 4. 实现与执行边界

仓库已经提供审批状态机、生产配置门禁、候选/部署流水线、生产部署模板、
制品/运行身份核对、R2A 负载工具和运营证据门禁。证书、域名、生产账号、
真实设备、仓库代码和连续两周现场数据属于环境输入，必须按 01 规范填写，
不得写入 Git，也不得用模拟值标记为完成。

迁移名称不得写死。候选阶段从实际迁移程序集生成
`release-manifest.json.Database.LatestMigration`；部署阶段通过
`GET /health/release` 返回的 `__EFMigrationsHistory` 最新记录动态比对。

## 5. 阶段完成定义

| 阶段 | 完成定义 | 主要证据 |
| --- | --- | --- |
| 代码就绪 | 自动化测试、模型检查和供应链门禁通过 | source gate 报告 |
| 候选就绪 | 签名、哈希、SBOM、漏洞报告、镜像摘要均写入 Schema 2 清单 | release manifest 与对象存储 URI |
| 环境就绪 | 初始化先于 API、运行摘要/迁移/远程制品完全匹配 | deployment evidence |
| R2A 退出 | 连续 14 天、≥1000 MOVE、十台设备、零关键差异、恢复达标 | R2A pilot exit evidence |
| R2B 退出 | 每产品转换与每库存桶完全对账，LPN/重试场景通过 | R2B preflight/转换/对账证据 |
| 仓库推广 | 本仓全部门禁独立重跑且 Go/No-Go 批准 | warehouse rollout record |
