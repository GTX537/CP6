# 01 生产输入与 RACI

## 1. 填写规则

每项输入必须填写 Owner、Target Date、Secret Reference、Evidence URI、
Approver 和 Approved At。`Secret Reference` 只允许填写密钥库路径或受控
runner 变量名；密码、私钥、连接串和令牌禁止进入 Git。`Evidence URI`
应为启用 Object Lock 的 `s3://` 对象 URI。未适用字段填写 `N/A` 并由审批人
确认，不得留空后宣称 Ready。

状态只使用 `Pending / Ready for approval / Approved / Rejected / Expired`。
Target Date 和 Approved At 使用 ISO 8601。

## 2. 生产输入登记表

| 类别 | 输入 | Owner | Target Date | Secret Reference | Evidence URI | Approver | Approved At | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Windows | 代码签名证书、Publisher、私钥可用性与备份 | TBD | TBD | TBD | TBD | Security |  | Pending |
| Windows | RFC 3161 时间戳服务 | TBD | TBD | N/A | TBD | Security |  | Pending |
| Windows | MSIX、AppInstaller 正式 HTTPS 地址与品牌资产 | TBD | TBD | N/A | TBD | Release Owner |  | Pending |
| Android | 正式 keystore、alias、SHA-256 签名指纹与备份 | TBD | TBD | TBD | TBD | Security |  | Pending |
| Android | APK 正式 HTTPS 下载地址 | TBD | TBD | N/A | TBD | Release Owner |  | Pending |
| API/TLS | API 域名、证书链、AllowedHosts、CORS | TBD | TBD | TBD | TBD | Security |  | Pending |
| 数据库 | SQL Server 端点、迁移/运行账号、备份与恢复策略 | TBD | TBD | TBD | TBD | DBA |  | Pending |
| Redis | TLS 端点、ACL 与容量 | TBD | TBD | TBD | TBD | Platform |  | Pending |
| 消息 | RabbitMQ/Kafka 端点、账号与启用范围 | TBD | TBD | TBD | TBD | Platform |  | Pending |
| 身份 | JWT、OIDC、回调地址、SMTP/OTP | TBD | TBD | TBD | TBD | Security |  | Pending |
| 存储 | S3 端点、bucket、SSE、版本控制、Object Lock | TBD | TBD | TBD | TBD | Security |  | Pending |
| 观测 | 日志、指标、告警、值班路由与保留期 | TBD | TBD | N/A | TBD | Operations |  | Pending |
| 试点 | 租户、仓库、区域、一个设备组 | TBD | TBD | N/A | TBD | Warehouse Owner |  | Pending |
| 角色 | 主管、调度、操作、审计四类负责人 | TBD | TBD | N/A | TBD | Business Owner |  | Pending |
| 设备 | 十台 Android、扫描枪型号、HID/广播配置 | TBD | TBD | N/A | TBD | Device Owner |  | Pending |
| 主数据 | 条码、UOM、批次、打印机映射、正式标签模板 | TBD | TBD | N/A | TBD | Warehouse Owner |  | Pending |
| R2B | 首批产品、序列清单与 LPN 容器范围 | TBD | TBD | N/A | TBD | Inventory Owner |  | Pending |
| 恢复 | SQL 备份恢复演练与 RPO/RTO | TBD | TBD | TBD | TBD | DBA + Operations |  | Pending |
| 变更 | 安全、仓库、平台和运维 Go/No-Go 审批 | TBD | TBD | N/A | TBD | Change Manager |  | Pending |

## 3. RACI

| 活动 | Responsible | Accountable | Consulted | Informed |
| --- | --- | --- | --- | --- |
| 候选签名与清单 | Release Engineer | Release Owner | Security、Mobile Owner | Operations |
| 数据库初始化 | DBA/Environment Runner | DBA | API Owner | Warehouse Owner |
| Compose 试点部署 | Platform Engineer | Operations Owner | DBA、Security | Pilot Team |
| R2A 启用与退出 | Warehouse Pilot Lead | Warehouse Owner | QA、Operations | Change Board |
| R2B 转换 | Inventory Lead | Inventory Owner | DBA、QA、Security | Warehouse Team |
| Kubernetes 推广 | Platform Engineer | Operations Owner | Warehouse/DBA/Security | Stakeholders |

## 4. 输入冻结

候选 Tag 前冻结签名身份、下载地址、镜像仓库与证据根 URI。部署审批前冻结
环境 Secret 引用、域名、TLS 和外部服务。R2A/R2B 前分别冻结仓库、设备、
主数据和产品范围。冻结后任何变化都必须产生新证据、重新审批；不得手改
已经归档的清单。
