# CAD Provider 认证与运行版本围栏

日期：2026-08-14

范围：Space Studio V1 核心 GA / WP3 仓库实现

结论：仓库版本围栏已实现；真实 Provider 认证与 Site 接受仍为 Pending，核心 GA 仍为 No-Go。

## 问题

此前 Site 认证、运行时注册和执行路由只绑定 `ProviderKey`。同名 Provider Worker 升级后，未经过同一黄金集、冻结环境和客户审批的新版本仍可能被视为原认证链，无法证明“评分版本就是执行版本”。

## 实现

- Site Provider 认证新增必填、不可变 `ProviderVersion`；管理请求、能力响应、OpenAPI、C# SDK、TypeScript SDK 和 CAD 向导同步该字段。
- 运行时 `SpaceCadProviderRegistration` 必须声明规范版本。配置写入只接受与当前部署注册完全相同的 Key + Version、部署模式、数据边界和格式能力。
- 能力查询和执行路由同时要求认证版本与运行注册版本按 Ordinal 完全一致；不一致时 `RuntimeAvailable=false`，并产生角色级 `CAD_*_RUNTIME_VERSION_MISMATCH` 阻断码。
- Preparation Provider 的实际 `ConverterId` 与 `ConverterVersion` 必须同时匹配运行注册；身份漂移在产物进入 sealed Preparation 前失败关闭。
- 新 CAD Parse payload 升级为 v5，保存 Preparation 封存的 `PreferredProviderVersion`。解析路由要求封存 Key + Version 仍属于当前合规链，避免预处理完成后同名 Worker 或 Site 认证换版造成漂移。
- 历史 payload v2–v4 保持显式读取兼容；从本版本开始生成的 v5 缺少规范 Provider Version 会在 Worker 调用前失败关闭。
- 新增独立可回滚 EF 迁移 `20260814063519_SpaceCadProviderVersionFence` 和幂等 SQL。历史认证行只得到空版本，不猜测回填；`HasCompleteQualification` 因此失败关闭，必须重新执行真实版本认证。
- `qualify-providers` 评分报告原本已保存候选版本；现在生成的 Site 认证输入也携带同一版本，打通“评分 → 认证 → 注册 → Preparation 输出 → Parse”证据链。

## 自动化证据

- Provider 路由覆盖认证/注册版本不一致零调用、输出版本漂移拒绝、sealed Parse 版本漂移拒绝、配置写入拒绝未注册版本。
- SQL Server 用例覆盖版本持久化、历史空版本失败关闭和迁移脚本重复执行；若未设置 `CP6_TEST_SQLSERVER`，这些用例只可记为编译/环境门禁，不能记为真实 SQL 接受证据。
- OpenAPI 守卫要求认证输入和能力槽的 `providerVersion` 均为 required；生成 SDK 漂移门禁同步验证。
- CAD 向导显示主备链认证版本，前端类型检查和向导单测覆盖必填字段。

## 未关闭门禁

- 没有安装、注册或实测真实 ODA、APS 或评分后替代者。
- 没有 20 份授权黄金 CAD 的同版本、同 Worker、同文件正式评分。
- 没有目标 Site 的客户、安全、法务、采购审批和主备认证记录。
- 没有将本迁移和版本竞争场景在生产等价 SQL Server 上保存为接受证据。

因此，本变更只关闭 WP3 的仓库级版本身份漏洞，不提升 `WP3_SITE_PRIMARY_BACKUP_PROVIDERS` 的 `Partial/Pending` 状态，也不提升核心 GA 完成百分比。
