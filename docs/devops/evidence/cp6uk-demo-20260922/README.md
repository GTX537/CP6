# 2026-09-22 cp6.uk demo 更新

用户明确选择更新到最新 main，范围为主站 ERP / WMS / 3D Space。应用源码固定为 `ac624299154d6186a92b54a14c02051c024aa6ed`；版本 `0.0.0-demo.20260922`。本次不发布 R2/GHCR 候选、不运行 Actions、不部署生产，也不接入独立 CRM。

## 实施

- 起始故障：旧 `cp6-api` 在 C04A 采用时有意停止且 `restart=no`，Web 的 nginx 无法解析该上游而重启，公网返回 502。保留退休 API 状态。
- `CP6DB` 的 COPY_ONLY/CHECKSUM 备份通过 VERIFYONLY；恢复到新库 `CP6_DEMO_20260922`。新账号对源库的 `HAS_DBACCESS=0`、`IS_SRVROLEMEMBER('sysadmin')=0`，仅在副本拥有 db_owner。备份 SHA-256 为 `e194124e3ca943a7be5cedfae657c00af94db22f8dd611188be10706381ae3ab`。
- 两个镜像均从固定源码本机构建成功，API 保留 locked restore/签名验证。API 本机 ID：`sha256:09b42260808339a7d450ba08827f87925d234cac8a4c29dbb9bf2d462e7fc9f9`；Web：`sha256:af07fe990d296f1a93e97ae906fb98929dd05a3234cf82662912836a3cc69d1f`。这些是本机镜像身份，不冒充 Registry 候选。
- 独立 Compose project `cp6-demo` 使用自己的 Redis/RabbitMQ/Kafka、数据卷及 loopback 端口。副本的一次性 db-init 退出 0，最新迁移为 `20260916130000_RetireLegacyCrmModel`；运行 API 跳过重复初始化。
- 浏览器发现默认关闭的 Design API 导致模型请求 404。核对已有 `cp6-business-api-ab5f5fda` 使用同一源库及同一已发布模拟站点后，复用该容器的 Space compatibility 配置。未更改既有证据标志、源库或旧 CRM 入口；模型请求随后返回 200。
- 先停止旧 Docker Tunnel 和反复重启的旧 Web，再启动唯一的 demo Tunnel。Cloudflare 切换后查询仅一个连接器；Windows Cloudflared 服务没有连入这个 Tunnel，未停止它。

## 本次验证

| 检查 | 结果 |
| --- | --- |
| Compose 渲染、PowerShell 解析、差异检查 | 通过 |
| API/Web 本地镜像构建 | 通过；保留原有 CS1998 和 Web 大 chunk 提示 |
| SQL 副本恢复、权限隔离、一次性初始化 | 通过 |
| Redis、RabbitMQ、Kafka、API 容器健康 | 通过 |
| 公网部署 smoke | 10 项通过：登录 HTML/JS、live/ready、双端发布身份、匿名访问拒绝、登录翻译 |
| 故意使用错误 SHA | 验证脚本拒绝，输出 passed=false |
| 本机日常启动脚本 | 对已有 demo 再次启动成功，未重建镜像或重跑 db-init；公网 smoke 再次通过 |
| 本机与公网正常登录 | 通过，进入 dashboard |
| 公网 ERP / WMS 列表 | 产品列表加载 11 行；库存首页加载 20 行 |
| 公网 Space 首页 / Viewer | 两站点、三楼层；标准模拟仓 Published 场景 200、两层 WebGL 模型显示 |
| 3D 实时库存叠加 | 未通过：已有 Issue #132，大量库位 URL 导致刷新失败 |

[自动化公网结果](verification.json)和[错误身份负向结果](negative-identity.json)仅含状态/版本信息；截图、完整构建日志、恢复日志、原配置、备份和密钥保留本机。SQL 辅助命令最初因同批次 USE 的编译顺序失败；分批后恢复成功，账号创建又因 PowerShell 取值为空被密码策略拒绝。确认副本已存在后仅修正账号创建，未重跑覆盖恢复；失败日志保留。

本次应用源码没有改动，因此复用 main 已记录的定向业务测试；没有重跑全仓测试或声称完整生产验收。演示模型为原有模拟数据。库存叠加问题见 [Issue #132](https://github.com/GTX537/CP6/issues/132)，主线未修复，本次不更改其状态。操作入口见[demo runbook](../../../../deploy/demo/README.md)。
