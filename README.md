# CP6

CP6 源于日本纸箱包装企业 Crown Package 的核心系统刷新项目（基幹システム刷新 PJ）。项目正在把原 mcframe7 ERP Add-on 重构为独立、可部署、可扩展的制造业平台，重点服务纸箱包装行业。

截至 **2026-08**，CP6 已不再只是“进销存 + MES”原型。当前代码覆盖 **ERP / MES / WMS / 财务 / 采购 / OA 工作流 / 权限与多租户平台 / Space 空间数字底座**，并提供 Web、Windows 桌面端和 Android 移动端。仓库当前包含 18 个解决方案项目、197 个 API Controller，以及 160+ 个前端路由组件。

> 这里的“已实现”表示仓库中已有对应代码、API、页面和测试，不等同于某个客户环境已经完成生产验收。WMS R2 的上线状态必须以候选制品、环境门禁和现场试点证据为准。

## 当前能力

| 领域 | 当前代码状态 | 主要能力 |
| --- | --- | --- |
| ERP / 販売管理 | 已实现，持续深化 | 見積、製品、受注、信用控制、欠品/未出荷、納期/OTD、取引先、為替、原価与价格相关主数据 |
| MES / 生産管理 | 已实现 | 製造指図、製造実績、工程/设备、品质与不良、OEE、计划达成、排程板、制造控制塔 |
| WMS / 在庫物流 | 已实现；R2 生产化门禁已落库 | 入出库、库存、盘点、补货、上架、越库、QC、效期、序列号/LPN、条码、标签、设备、任务、分析，以及纸卷/油墨/残材/托盘等纸器业场景 |
| 财务 / FIN | 已实现 | 总账、凭证、会计期间、应收应付、收付款、银行对账、固定资产、预算、成本、试算表与财务报表 |
| 采购 / PUR | 已实现 | PR、RFQ、PO、收货、供应商价格、三单匹配、差异对账与外注加工 |
| OA / Workflow | 已实现 | 动态表单、流程设计与运行、待办/草稿、会签/或签、加签、退回、委派、超时、子流程、服务任务、连接器与工作日历 |
| PUB / 系统平台 | 已实现 | 多角色 RBAC、菜单/按钮权限、数据范围、字段权限、部门组织、附件、采番、代码生成、多语言、审计、SSO、2FA、多租户与平台管理 |
| MRP / 计划中台 | 已实现基础闭环 | MRP 运算、物料计划策略，以及计划到采购申请的接缝 |
| Space 空间数字底座 | 已落地并持续建设 | 2D/3D 建模与浏览、版本化设计、CAD/Excel 导入与匹配、校验/发布/恢复、WMS 运行态叠加、人员设备、诊断推荐、调度、规划仿真、AI 提案审查与原子应用 |
| 原生 WMS 客户端 | 已实现 | WPF 调度/打印客户端、.NET MAUI Android 作业端、共享类型化 API 与客户端核心库 |

核心业务链已经从原来的 `販売 → 生産 → 在庫物流` 扩展为：

```text
ERP 受注/计划
    → MES 生产执行
    → WMS 入出库与现场作业
    → 财务应收、应付、成本与报表

采购 PR/RFQ/PO → WMS 收货/QC → 财务 AP
OA/PUB/Platform 为各业务域提供审批、权限、组织、多租户与审计
Space 提供仓库空间建模、运行态可视化、诊断与规划能力
```

## 技术栈

| 层 | 技术 |
| --- | --- |
| 后端 | ASP.NET Core 8、EF Core 8、Dapper、SQL Server、JWT、OpenID Connect、SignalR |
| Web 前端 | Vue 3.5、TypeScript 6、Vite 8、Element Plus、Pinia、Vue Router、vue-i18n |
| 空间与可视化 | Three.js、Konva、Vue Flow、PDF.js、Open XML |
| 基础设施 | Redis、RabbitMQ、Kafka、S3 兼容对象存储、Prometheus 健康与指标端点 |
| 原生客户端 | .NET 8 WPF、.NET 10 MAUI Android、共享 `CP6.Client.Api` / `CP6.Client.Core` |
| 测试与门禁 | xUnit、EF Core InMemory/SQLite、Vitest、Playwright、OpenAPI/客户端契约与 R2 发布门禁 |
| 部署 | 开发用 Docker Compose / Kubernetes；生产用 `deploy/production` 下的受控 Compose 与 Kubernetes 模板 |

## 仓库结构

```text
CP6.Entity/                  共享实体、DTO 与业务数据模型
CP6.Core/                    核心服务、EF Core、权限、工作流与跨域基础设施
CP6.WebApi/                  REST API、SignalR Hub、后台任务、种子与可观测性
CP6.Tests/                   主应用单元/集成/安全与回归测试

CP6.Space.Domain/            Space 领域模型
CP6.Space.Contracts/         Space 公共契约
CP6.Space.Application/       Space 应用层
CP6.Space.Infrastructure/    Space 持久化与外部适配
CP6.Space.Client/            Space 客户端契约
CP6.Space.UnitTests/         Space 单元测试
CP6.Space.IntegrationTests/  Space 集成测试

cp6.web/                     Vue Web 管理端、业务页面与 2D/3D 空间界面
CP6.Client.Api/              原生客户端类型化 API
CP6.Client.Core/             原生客户端共享传输、认证、离线恢复与实时通信
CP6.Desktop/                 Windows WPF 调度与打印客户端
CP6.Mobile/                  Android MAUI 现场作业客户端
CP6.Client.Tests/            客户端核心测试

deploy/production/           生产部署模板、SQL 与受控运行资产
scripts/                     构建、发布、部署、备份、试点与证据门禁脚本
sdk/                         对外 SDK 与生成产物
docs/                        架构、模块设计、验收、操作与学习文档
tools/                       Space OpenAPI、标准仓、CAD 等工具
```

主应用仍是模块化单体：`CP6.WebApi → CP6.Core → CP6.Entity`。Space 使用独立的 Domain / Contracts / Application / Infrastructure 分层，再由 WebApi 组合。Web、WPF 和 Android 客户端都通过 HTTP / SignalR 与 WebApi 通信。

## 快速开始

### 前置环境

- Web/API 开发：.NET 8 SDK、Node.js `^20.19.0` 或 `>=22.12.0`、Docker Desktop。
- 构建整个 `CP6.slnx` 或 Android 客户端：.NET 10 SDK；Android 还需要 MAUI workload。
- 根目录 `docker-compose.yml` 和 `k8s/` **仅用于开发**，不能作为 R2 生产部署输入。

### 1. 准备本地基础设施

```powershell
Copy-Item .env.example .env
# 编辑 .env，替换 SQL Server、RabbitMQ 和 JWT 占位值；不要提交 .env

docker compose up -d cp6-db cp6-redis cp6-mq cp6-kafka

# 本机运行 API 时，连接 Docker 中的 SQL Server。
# 请把 <MSSQL_SA_PASSWORD> 替换为 .env 中的同名值。
$env:ConnectionStrings__DefaultConnection = 'Server=localhost,1433;Database=CP6DB;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True;MultipleActiveResultSets=True'
```

### 2. 启动 API

```powershell
dotnet restore CP6.WebApi/CP6.WebApi.csproj
dotnet run --project CP6.WebApi/CP6.WebApi.csproj
```

- API / Swagger：<http://localhost:5177/swagger>
- 健康检查：<http://localhost:5177/health/live>、<http://localhost:5177/health/ready>

### 3. 启动 Web 前端

```powershell
Set-Location cp6.web
npm ci
npm run dev
```

- Web：<http://localhost:5173>
- Vite 默认把 `/api` 和 `/hubs` 代理到 `http://localhost:5177`；可通过 `VITE_API_TARGET` 覆盖。

## 常用验证

```powershell
# 主应用与各独立层测试
dotnet test CP6.Tests/CP6.Tests.csproj
dotnet test CP6.Space.UnitTests/CP6.Space.UnitTests.csproj
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj
dotnet test CP6.Client.Tests/CP6.Client.Tests.csproj

# Web 类型、单测与生产构建
Set-Location cp6.web
npm run type-check
npm test
npm run build

# 浏览器端到端测试
npm run e2e
```

生产候选、部署和现场试点不要直接套用上面的开发命令；请从 [WMS R2 生产就绪主规范](docs/client/r2/README.md) 开始，并使用仓库内的受控脚本与证据门禁。

## 文档入口

| 文档 | 用途 |
| --- | --- |
| [DevOps 与 CI/CD](docs/devops/README.md) | Azure DevOps 当前状态、CI/CD 架构、Registry 决策、发布流程和环境演进计划 |
| [CRM 产品需求与公开工程契约](docs/crm/README.md) | 私有 Frozen 产品、已批准 CRM V1 PRD 与 Accepted R00 的脱敏四仓合同；公开同步已为 Complete、M0 仍为 No-Go，不代表业务功能已实现 |
| [WMS R2 生产就绪主规范](docs/client/r2/README.md) | 候选制品、签名、部署、R2A/R2B 试点、退出标准与现场证据的唯一规范源 |
| [原生 WMS 客户端](docs/client/README.md) | Web、WPF、Android 客户端边界、共享契约与生产行为 |
| [Space 设计总纲](docs/space/README.md) | 空间数字底座的边界、架构、建模、发布、WMS 集成与阶段范围 |
| [Space 需求与 Epic](docs/space/requirements/03-epic-and-spec-backlog.md) | 当前 Space 产品范围和开发拆分 |
| [Space 验收资产](docs/space/acceptance/README.md) | 可执行验收基线、数据包和验证入口 |
| [财务模块](docs/finance/README.md) | 财务领域设计与实现说明 |
| [采购模块](docs/procurement/README.md) | 采购闭环设计与实现说明 |
| [审批 / OA 引擎](docs/approval/README.md) | 工作流、表单、审批路由与业务接缝 |
| [PUB 公共平台](docs/pub/README.md) | 权限、组织和公共能力设计说明 |
| [项目结构参考](docs/PROJECT_STRUCTURE.md) | 较细的目录与代码导航；若版本号或统计口径冲突，以项目文件和本 README 为准 |

## 当前发布边界

WMS R2 仓库已经提供生产配置门禁、候选/部署流水线、签名制品与清单约束、Compose/Kubernetes 生产模板、数据库前向迁移、运行身份核对、负载工具和运营证据门禁。证书、域名、生产账号、真实设备、仓库代码以及连续现场数据属于部署环境输入，不能用模拟值宣称完成。

首个候选版本目标为 `v1.0.0`。R2A 的退出条件包括连续 14 天、至少 1,000 个 MOVE、10 台设备、零关键差异和恢复指标达标；R2B 还要求序列号/LPN 转换与库存桶逐项对账。详细定义以 [R2 主规范](docs/client/r2/README.md) 为准。
