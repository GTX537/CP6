# cp6.uk 本机演示

此 Compose 用于用户授权的 ERP / WMS / 3D Space 演示。它消费已验证的本机镜像 ID，使用独立数据库副本及独立 Redis、RabbitMQ、Kafka，不属于 R2 生产发布或 Azure DEV 推广。

2026-09-22 部署源码为 `ac624299154d6186a92b54a14c02051c024aa6ed`，版本为 `0.0.0-demo.20260922`。本任务之后的文档/部署配置提交不改变已构建的应用源码身份。验证及已知限制见[部署记录](../../docs/devops/evidence/cp6uk-demo-20260922/README.md)。

## 访问

| 用途 | 地址 |
| --- | --- |
| 登录 | <https://cp6.uk/login> |
| ERP 产品列表 | <https://cp6.uk/product-list> |
| WMS 库存 | <https://cp6.uk/wms/stock> |
| Space 首页 | <https://cp6.uk/space/home> |
| API 健康 | <https://api.cp6.uk/health/ready> |
| 本机 Web / API | `http://127.0.0.1:28080` / `http://127.0.0.1:29991` |

使用数据库副本中的现有应用账号。演示中的修改只进入 demo 副本。电脑、Docker Desktop 和网络须保持运行；休眠或关机会中断公网访问。

Space 使用已存在的模拟标准仓。其 Published 模型和两层画布已验证；大量库位的实时库存叠加仍受 [Issue #132](https://github.com/GTX537/CP6/issues/132) 影响。不要把模型展示或 HTTP 检查描述为库存业务全流程验收。新版独立 CRM 不在本次范围。

## 配置与首次部署顺序

实际 `runtime.env`、Space 配置、备份和完整运行日志保存在仓库外受限目录，不提交 Git。所需变量：

| 变量 | 内容 |
| --- | --- |
| `CP6_DEMO_API_IMAGE` / `CP6_DEMO_WEB_IMAGE` | 已验证的本机不可变镜像 ID |
| `CP6_DEMO_GIT_SHA` / `CP6_DEMO_VERSION` | 两个镜像共同的完整源码 SHA / 版本 |
| `CP6_DEMO_DB_CONNECTION` | 只授权到 demo 副本的 SQL 账号连接串，不使用 `sa` |
| `CP6_DEMO_MQ_PASSWORD` / `CP6_DEMO_JWT_SECRET` | demo 独立随机值 |
| `CP6_DEMO_SPACE_ENV_FILE` | 已核对的该站点 Space compatibility 配置；保留其来源，不伪造验证标志 |
| `CP6_DEMO_TUNNEL_DIRECTORY` / `CP6_DEMO_TUNNEL_IMAGE` | 已有 Tunnel 凭据目录 / 镜像 ID |
| `CP6_DEMO_DATABASE_NETWORK` | 已有 SQL 容器网络，默认 `cp6_default` |

1. 对源库执行 `COPY_ONLY, COMPRESSION, CHECKSUM` 备份并 `RESTORE VERIFYONLY WITH CHECKSUM`；恢复到全新数据库/物理文件，不使用 `REPLACE`。创建只拥有副本权限的 SQL 登录，并确认无法访问源库。
2. 从确认的 main 构建一次 API/Web，注入相同版本和 SHA。API 私有包认证用 Docker BuildKit `nuget_token` secret，禁止 build argument 或镜像内凭据。
3. 把需要的应用文件/i18n 复制到 demo 独立卷。核对 Space 配置对应副本中的 Tenant、Site 和已发布模型；不重开旧 CRM 写入口。
4. 启动 demo 的 Redis、RabbitMQ、Kafka 并等待健康；仅对副本执行 `db-init`，确认退出码 0 后启动 API/Web。日常启动不重复初始化。
5. 验证本机登录、前后端身份、业务页面。用 Cloudflare `tunnel info` 核对连接器身份，停止旧 `cp6-cloudflared` 后再启动本项目的 `cloudflared`，避免同一 Tunnel 混送两套应用。
6. 执行下方公网检查和登录后的页面复测。保留旧容器、卷及备份；失败时停止 demo 公网入口并前向修复，不恢复旧数据库覆盖当前库。

## 日常命令

在仓库根目录运行 PowerShell 7；先把 `$demoEnv` 设置为仓库外的实际环境文件绝对路径。

```powershell
$demoCompose = 'deploy/demo/compose.yaml'

# 只看服务状态
docker compose --env-file $demoEnv -f $demoCompose --profile public ps -a

# 日常启动：SQL 容器应已健康，旧 cp6-cloudflared 和退休 cp6-api 必须保持停止
docker compose --env-file $demoEnv -f $demoCompose up -d --wait redis rabbitmq kafka api web
docker compose --env-file $demoEnv -f $demoCompose --profile public up -d --no-deps cloudflared

# 验证公网与源码身份；不会登录或修改业务数据
./scripts/Test-Cp6Demo.ps1 -ExpectedGitSha ac624299154d6186a92b54a14c02051c024aa6ed

# 只关闭 demo 公网，或停止 demo 应用（保留数据）
docker compose --env-file $demoEnv -f $demoCompose --profile public stop cloudflared
docker compose --env-file $demoEnv -f $demoCompose --profile public stop
```

不要用旧 `cp6-daytime-server.bat start` 启动此演示：它针对根 `cp6` 项目，会重新引入已停用的旧 API。不要删除命名卷。首次初始化、备份恢复和版本更新属于独立操作，不放入日常启动。
