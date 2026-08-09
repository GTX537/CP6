# 第 4 章：Docker、部署、可观测性与生产排障

> 生产能力的分水岭不是会不会 `docker compose up`，而是异常时能否保留现场、建立假设、用证据缩小范围、止血并根治。

## 目录与学习顺序

| 阶段 | 章节 | 学习结果 |
|---|---|---|
| 生产地图 | 1–10 | 认识容器、部署、观测、告警和标准故障流程 |
| CP6 容器 | 11–16 | 逐段读 Dockerfile、Compose 网络、卷、配置与健康检查 |
| 观测排障 | 17–24 | 掌握 Prometheus、日志/trace、SLO、数据库和积压排障 |
| 输出验收 | 25–26 | 完成 15 问和五项可复现故障/恢复实验 |

---

## 1. 镜像、容器、卷与网络

- **镜像**：只读分层模板，包含程序与运行依赖。
- **容器**：镜像的运行实例，拥有可写层和隔离的进程/网络视图。
- **volume**：独立于容器生命周期的持久化数据。
- **network**：为容器提供名称解析与通信边界。

容器不是轻量虚拟机，它仍使用宿主机内核。删除重建容器通常会丢失可写层，所以数据库必须挂载明确卷，并真正验证备份能恢复。

### 多阶段构建

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish CP6.WebApi/CP6.WebApi.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CP6.WebApi.dll"]
```

SDK 只留在构建阶段，运行镜像更小、攻击面更低。生产还应考虑非 root 用户、依赖层缓存、固定镜像版本、只读文件系统与资源限制。

---

## 2. 配置与秘密

配置通常按“默认值 → 配置文件 → 环境变量/部署注入”覆盖。环境间改变连接地址、凭证与日志等级，不应编译不同业务代码。

- `.env` 可服务本地开发，真实秘密不得提交 Git；
- 生产凭证进入秘密管理系统或平台 Secret；
- 日志不输出完整连接串、token、cookie 或密码；
- 秘密要支持轮换；
- 启动时验证必需配置，快速失败但不泄露秘密。

---

## 3. 健康检查

| 检查 | 回答的问题 | 失败动作 |
|---|---|---|
| startup | 慢启动是否完成 | 暂不做存活判断 |
| liveness | 进程是否卡死到需重启 | 重启容器 |
| readiness | 当前能否接新流量 | 从负载均衡摘除 |

liveness 不宜强依赖数据库，否则数据库短暂故障会触发所有应用重启，加剧事故。`/health` 返回 200 也只证明端点可响应，不代表登录、权限、写库和核心业务全部正常；还需要关键业务合成探测。

---

## 4. 发布与迁移

- **滚动发布**：逐步替换实例，要求新旧版本可同时运行。
- **蓝绿发布**：两套环境切流，回滚快但资源成本高。
- **金丝雀**：先给少量流量，观察错误率、延迟与业务指标再扩大。

数据库使用 expand/contract：

1. 新增兼容列/表，不删旧结构；
2. 部署同时兼容新旧结构的代码；
3. 回填并验证数据；
4. 切换读写并观察；
5. 确认无旧版本后再删除。

“应用回滚”不等于“数据库可以回滚”。破坏性迁移、数据转换和新版本写入的数据可能无法简单还原。

---

## 5. 可观测性三支柱

### 日志：发生了什么

采用结构化字段：服务、环境、版本、trace ID、tenant ID、user ID、路由、耗时、结果。避免把信息拼成一条不可查询字符串。

```csharp
logger.LogInformation(
    "Outbound posted {OutboundId} for tenant {TenantId} in {ElapsedMs}ms",
    outboundId, tenantId, elapsedMs);
```

### 指标：整体怎样

服务看 RED：请求速率 Rate、错误 Errors、耗时 Duration。资源看 USE：利用率、饱和度、错误。业务再看单据成功率、库存同步延迟、消息积压。平均延迟会掩盖尾部，应同时看 P50/P95/P99。

### 追踪：这一次慢在哪

一次 trace 下包含代理、WebApi、SQL、MQ 和下游 HTTP 等 spans。日志带 trace ID，才能从告警跳到某次请求，再回到上下游日志。

---

## 6. 告警设计

好告警代表用户影响或即将发生的容量风险，并包含影响、当前值、持续时间、仪表盘和 runbook。

优先告警：

- 核心接口持续违反错误率/延迟 SLO；
- 数据库连接池长期饱和；
- 消息最老年龄和积压持续增长；
- 磁盘按增长趋势即将耗尽；
- 备份或恢复演练失败；
- 核心业务成功率异常下降。

CPU 短暂 80% 未必需要半夜叫人；告警必须可行动。

---

## 7. 标准故障流程

```text
发现 → 确认影响 → 指定负责人/沟通渠道
  → 保留证据 → 止血（回滚、切流、限流、降级、扩容）
  → 分层定位 → 修复 → 验证 → 持续观察
  → 复盘 → 永久改进
```

重启可能暂时释放连接或内存，却隐藏泄漏根因；扩容可能把数据库打得更重；盲目重试可能形成重试风暴。它们都是有代价的止血动作。

### “系统突然变慢”排查树

1. 范围：全部接口还是单一路由？全部租户还是一个？何时开始？
2. RED：流量、错误率、P95/P99 是否变化？
3. 资源：CPU、内存/GC、线程池、连接池、磁盘、网络。
4. 依赖 span：SQL、Redis、MQ、下游 HTTP 哪一段慢？
5. SQL：阻塞链、死锁、执行计划、统计信息、索引。
6. 变更：版本、配置、数据量、定时任务、证书或基础设施事件。

每步先写假设和预期证据。例如：若连接池耗尽，则等待连接时间升高、活跃连接接近上限，但 SQL 执行时间本身可能正常。

---

## 8. 高频故障剧本

### 502 Bad Gateway

查代理 upstream 错误；确认 WebApi 进程和重启次数；从代理所在网络测试目标端口；检查应用监听地址和端口；确认 readiness 是否摘除全部实例。

### 504 Gateway Timeout

用 trace 找最慢 span；检查代理、应用与下游超时预算；检查慢 SQL、锁、连接池、线程池和外部依赖。不要第一反应调大超时，它可能只让资源占用更久。

### 数据库连接池耗尽

表现为请求等待连接，数据库 CPU 未必高。检查长事务、连接未释放、慢查询、实例扩容后的总连接数。根治通常是缩短事务、正确释放、优化查询和限制并发，不是无限增大池。

### 磁盘将满

确认增长最快目录与速率；安全清理可再生缓存/过期日志；扩容止血；检查数据库日志、容器日志、备份保留和转储；建立轮转、配额和趋势告警。删除前确认用途与恢复路径。

### 消息积压

看生产速率、消费速率、最老消息年龄、失败/重试率。区分消费者停机、下游变慢、毒消息、分区不均与真实流量增长。扩消费者前确认瓶颈不是共享数据库。

---

## 9. 复盘

无责复盘是把焦点放在系统为何允许错误扩大，不是免除改进责任。至少包含：时间线、用户影响、直接原因、促成条件、为何没提前发现、止血效果，以及带负责人、截止日期和验证方法的行动项。

行动项应改变系统：新增容量告警、失败测试、自动回滚或恢复演练。只有“以后更小心”不算闭环。

---

## 10. 动手练习与验收

1. 阅读项目 `docker-compose.yml`，为每个服务写出镜像、端口、网络、卷、健康检查和依赖。
2. 选择一个 API，列出结构化日志字段、RED 指标和 trace spans。
3. 写“P95 超过 2 秒持续 10 分钟”的 runbook。
4. 模拟错误端口造成 502，记录现象、假设、证据、修复与验证。
5. 设计一次数据库恢复演练，并记录 RTO/RPO。

验收：15 分钟内对“页面报 504”完成分层排查口述；能区分止血与根治；每个结论都能说出对应日志、指标或命令证据。

---

> **深入学习部分：下面逐行读懂 CP6 的容器、指标与生产排障设计。**

## 11. Docker 镜像为什么是分层的

Dockerfile 每条产生文件系统变化的指令通常形成一层。若某层输入未变化，Docker 可以复用缓存；某层改变，后续依赖层需要重建。

CP6 后端先复制项目文件再 restore：

```dockerfile
COPY CP6.Entity/CP6.Entity.csproj CP6.Entity/
COPY CP6.Core/CP6.Core.csproj CP6.Core/
COPY CP6.WebApi/CP6.WebApi.csproj CP6.WebApi/
RUN dotnet restore CP6.WebApi/CP6.WebApi.csproj

COPY . .
RUN dotnet publish ... --no-restore
```

原因是源码比项目依赖声明变化频繁。只改 Controller 时，项目文件未变，NuGet restore 层可以复用；若先 `COPY . .`，任何源码变化都会让 restore 缓存失效。

### 11.1 构建上下文与 `.dockerignore`

`docker build` 会把 context 中未排除的文件发送给构建器。若包含 `.git`、测试产物、日志、`.env` 和 node_modules：

- 构建变慢；
- 缓存频繁失效；
- 敏感文件可能进入构建上下文，甚至被错误 COPY 到镜像。

所以 `.dockerignore` 同时是性能和安全边界。

### 11.2 `EXPOSE` 不等于开放端口

`EXPOSE 5000` 是镜像元数据，不会自动映射宿主端口。真正映射由 Compose 完成：

```yaml
ports:
  - "9991:5000"
```

宿主访问 `localhost:9991`，进入容器 5000。容器之间应使用服务名和容器端口，例如 `cp6-api:5000`，不是 9991。

`ASPNETCORE_URLS=http://+:5000` 让 Kestrel 监听所有容器接口。若只监听 localhost，容器内部 curl 可能成功，其他容器却连不上。

---

## 12. 前端镜像的两阶段

```dockerfile
FROM node:22-alpine AS build
RUN npm install
RUN npm run build-only

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
```

Vue/TypeScript 在构建阶段转成静态 HTML/JS/CSS。运行阶段不需要 Node 开发服务器，只需 Nginx 提供静态文件和 API 代理。

审查两点：

1. 可重复构建通常偏向 `npm ci` + lockfile；`npm install` 的依赖解析约束相对宽；
2. `build-only` 只做 Vite 构建，不等于 package.json 中包含 type-check 的完整 `build`。CI 应单独保证 type-check 与测试。

这不是见到某条命令就下结论，而是结合脚本真实语义判断构建证据。

---

## 13. Compose 网络：`localhost` 到底是谁

Compose 服务默认加入同一项目网络，通过服务名 DNS 发现：

```text
cp6-api → cp6-db:1433
cp6-api → cp6-mq:5672
cp6-api → cp6-kafka:9092
宿主浏览器 → localhost:9991 → cp6-api:5000
```

API 容器配置 `Server=localhost` 是典型错误：容器内 localhost 是 API 自己。正确连接串用 `Server=cp6-db`。

### Kafka 为什么有内外两个 listener

```text
PLAINTEXT://cp6-kafka:9092   容器网络内部
EXTERNAL://localhost:29092  宿主客户端
```

Kafka 客户端先连 bootstrap，再按 broker 返回的 advertised listener 建正式连接。若给容器客户端广告 localhost，它会连接自己；若给宿主广告 `cp6-kafka`，宿主 DNS 通常不认识。因此 listener 必须匹配访问方的网络视角。

---

## 14. Compose 卷：逐个判断数据寿命

| 卷 | 容器路径 | 丢失后果 |
|---|---|---|
| cp6-db-data | `/var/opt/mssql` | 业务数据库丢失 |
| cp6-redis-data | `/data` | 取决于 Redis 是否仅缓存 |
| cp6-mq-data | `/var/lib/rabbitmq` | 队列与元数据可能丢失 |
| cp6-kafka-data | `/var/lib/kafka/data` | topic、offset、消息丢失 |
| cp6-api-i18n | `/app/wwwroot/i18n` | 发布语言快照回退 |

卷不是备份：卷会损坏、会被误删、宿主磁盘会故障，逻辑删除也会写入卷。备份必须是独立副本，并通过恢复演练证明可用。

cloudflared 使用只读 bind mount：

```yaml
- ./cloudflared-docker:/etc/cloudflared:ro
```

`:ro` 限制容器修改凭证配置，是最小权限的一层。

---

## 15. 环境变量插值与 ASP.NET 配置键

```yaml
ConnectionStrings__DefaultConnection: "Server=cp6-db;..."
RabbitMQ__Password: "${RABBITMQ_PASSWORD}"
JWT__Secret: "${JWT_SECRET:?Set JWT_SECRET ...}"
```

双下划线映射冒号：

```text
ConnectionStrings__DefaultConnection
→ ConnectionStrings:DefaultConnection
```

`${VAR:?message}` 在缺失时让 Compose 立即报错；`${VAR:-default}` 提供默认值。秘密不应有弱默认值，非敏感本地配置才适合默认。

CP6 `Program.cs` 注释明确低到高：基础 appsettings → 环境 appsettings → Local → 环境变量 → 命令行。若错误把 Local.json 追加到最后，它可能盖过容器环境变量，导致应用仍连本地库。

排查时可以打印“配置是否存在、来源和脱敏主机”，不要打印完整密码/token。

---

## 16. `depends_on` 与健康检查的真实语义

CP6 API 等数据库、Redis、RabbitMQ、Kafka healthy 后启动。这改善启动顺序，但不保证依赖之后永不故障、schema 已迁移、业务账号有权限或 API 核心功能健康。

SQL Server 健康检查执行 `SELECT 1`，比只检查 1433 端口强，但它使用 sa，未证明 CP6DB schema 和业务账号权限。检查越深越接近真实业务，也越可能因依赖短暂波动造成摘流/重启，因此必须区分 startup、readiness、liveness。

`restart: unless-stopped` 能自动拉起退出进程，但可能形成 crash loop。重启次数本身必须观测；否则应用每分钟崩一次又起来，根因被“自动恢复”掩盖。

---

## 17. Prometheus 指标类型不是随便选

### Counter

只增不减，适合累计请求/失败数。进程重启可归零，查询通常用 `rate()` 看增速。

### Gauge

可增可减，适合当前队列深度、活跃连接、温度。

### Histogram

按 bucket 记录分布，适合请求延迟/大小，可聚合计算分位数。bucket 要覆盖业务关心范围。

### CP6 的真实业务指标

`BridgeMetricsCollector` 暴露：

```text
cp6_bridge_hook_total{hook,status}
cp6_bridge_retry_queue_depth
cp6_integration_event_dead_letter_total
```

它把 `T_IntegrationEvent` 当数据真相，每次 scrape 重新聚合，进程重启不会丢当前状态。虽然名字含 `_total`，实现用 Gauge，因为值来自数据库快照而非本进程单调累计。

### scrape 查数据库的权衡

优点是数据持久且直接；代价是每次拉取增加 DB 查询。scrape 过频或事件表很大时，观测本身会施压。CP6 在聚合失败时保留其他 HTTP 指标，但还要让采集失败可见，否则业务指标会静默陈旧。

### 标签基数

`hook`、`status` 值集合有限，适合 label。不要把 userId、orderId、traceId 放 label，每个唯一值会创建新时间序列。这些高基数字段应进日志/trace。

---

## 18. 日志、指标、追踪如何协作

### 18.1 结构化日志

错误：

```csharp
logger.LogInformation("处理单据 " + id + " 花费 " + ms + "ms");
```

正确：

```csharp
logger.LogInformation(
    "Posted outbound {OutboundId} for tenant {TenantId} in {ElapsedMs}ms",
    id, tenantId, ms);
```

模板稳定，值成为可查询字段。日志中保留 tenant、route、status、elapsed、traceId，但不记录 token/密码/完整连接串。

不要在每层重复 LogError 再抛。通常在能增加业务上下文或最终异常边界记录一次，否则同一异常产生多份堆栈和告警噪声。

### 18.2 一条 trace 的结构

```text
HTTP GET /api/wms/stock                  180ms
├─ auth/tenant middleware                  3ms
├─ StockService.Search                   170ms
│  ├─ SQL SELECT Stocks                  145ms
│  └─ map DTO                             18ms
└─ serialize response                       7ms
```

指标发现“P95 普遍升高”，trace 定位“慢在 SQL”，日志说明这次请求的租户、参数摘要和错误。三者通过 traceId 关联。

高流量下不会永远保存每条 trace。头部采样简单但可能错过后来失败；尾部采样可优先保留慢/错请求，但设施更复杂。

### 18.3 日志不等于审计

运行日志用于诊断并会轮转；审计要求主体、动作、对象、前后值、时间、来源以及保留/防篡改。不能因“日志里大概有”就宣布审计完成。

---

## 19. SLI、SLO 与错误预算

- SLI：实际测量，如成功比例、P95 延迟；
- SLO：内部可靠性目标；
- SLA：对外合同承诺及可能赔偿。

99.9% 月可用性粗略允许约 43 分钟不可用。错误预算让发布速度与可靠性有共同语言：预算消耗过快，优先稳定；预算充足，允许受控变更。

不要把所有 4xx 算成服务端故障。用户参数错误与 500 意义不同；但若认证故障导致 401 暴增，也可能是生产事故，需要结合业务指标判断。

---

## 20. 生产变慢：一次证据驱动示范

场景：14:05 起 WMS 库存查询 P95 从 300ms 升至 8s，部分 504。

### 20.1 先写影响声明

```text
14:05 至今，全部租户库存查询约 35% 超过 5s，8% 经网关 504；
出库写入错误率暂未上升。
```

先确认范围：单路由还是全站、单租户还是全部、何时开始、是否有发布/批任务。

### 20.2 建立可证伪假设

- 流量突增：QPS 与资源/连接应同步上升；
- SQL 计划退化：该 SQL duration/逻辑读上升，其他接口相对正常；
- 连接池耗尽：等连接时间与池使用率上升，SQL 自身可能正常；
- 线程池阻塞：线程/队列增长、CPU 未必满、堆栈有同步等待。

按最能区分假设的证据查，不是同时乱开 20 个 dashboard。

### 20.3 止血与根治

新发布导致回归可先回滚；重报表可限流/降级；必要时扩容，但先确认共享数据库能承受。每次动作记录时间和指标变化。

若最终根因是遗漏租户+状态复合索引，永久动作应包括索引/查询修复、数据规模回归基准、慢查询告警和发布前计划检查。不能以“重启后好了”结案。

---

## 21. 502/504 的命令级排查

```powershell
docker compose ps
docker compose logs --tail 200 cp6-api
docker inspect cp6-api
Test-NetConnection localhost -Port 9991
curl.exe -v http://localhost:9991/metrics
```

检查退出码、重启次数、健康、监听端口和启动错误。分享输出前脱敏环境变量。

宿主访问成功不等于代理容器可访问。需要从代理网络视角验证 `cp6-api:5000`，因为 DNS、路由和端口不同。

合格结论不是“可能网络问题”，而是：

```text
宿主 9991 可连；Web 容器能解析 cp6-api，但连接 5000 被拒绝；
API 容器 14:03 后反复退出，启动日志显示必需配置缺失。
因此 502 的直接原因是上游未监听。
```

---

## 22. 数据库事故专题

### 阻塞与死锁

阻塞是 A 持锁、B 等待，A 提交后 B 继续；死锁是 A 等 B、B 又等 A，形成环，数据库回滚一个 victim。

死锁可在请求幂等前提下有限重试，但必须缩短事务、统一更新顺序、优化索引。重试只处理症状。

### 长事务来源

- 事务内调用 HTTP/MQ；
- 大批量逐行操作；
- 慢 SQL/缺索引；
- 异常路径资源未及时释放；
- 把用户交互时间包进事务。

事务应只包住必须原子的一小组数据库动作。

### 备份三问

1. 备份是否成功产生？
2. 是否在独立故障域？
3. 是否实际恢复并通过业务校验？

RPO 表示最多能丢多少数据时间，RTO 表示多久恢复。没有恢复演练，就没有证据证明目标可达。

---

## 23. 消息积压专题

深度本身不是全部，速率更重要：

```text
生产 100/s，消费 120/s：积压正在恢复
生产 100/s，消费 80/s：每秒净增 20，迟早失控
```

当消费率大于生产率时：

```text
预计清空时间 = backlog / (consumeRate - produceRate)
```

排查：消费者重启 → 错误/重试 → 下游延迟 → 分区利用 → 毒消息 → 数据库瓶颈。扩消费者前确认下游能承受。

除了 queue depth，还要看最老消息年龄。“只有一条但卡两天”可能比刚出现一百条更严重。

---

## 24. 发布兼容与安全回滚

滚动发布期间新旧实例并存：

```text
旧应用 + 旧 schema
新应用 + 兼容 schema
旧应用 + 兼容 schema
新旧消费者同时处理事件
```

先 expand 新列/字段并双兼容，再迁移读写，最后 contract 删除。消息新增可选字段通常较安全；删除/改义需版本与迁移窗口。

回滚前问：新版本是否写了旧版不能理解的数据？migration 是否可逆？是否已发布新消息格式？后台任务会不会重复？切回镜像不等于系统状态回滚。

---

## 25. 面试题 15 问（含答案）

### 1. 镜像和容器区别？

镜像是只读模板；容器是运行实例并有可写层。持久数据不能依赖容器可写层，应放卷或外部服务。

### 2. 多阶段构建价值？

SDK/Node 等工具留在构建阶段，运行镜像只含产物和 runtime，减少体积与攻击面；合理 COPY 顺序还能复用依赖缓存。

### 3. `EXPOSE` 会开放端口吗？

不会，只是元数据。宿主映射由 `ports`/`-p`；容器互连使用网络服务名和容器端口。

### 4. 为什么容器里不能用 localhost 连数据库？

localhost 指当前容器。数据库在另一个容器，应使用 Compose 服务名 `cp6-db`。

### 5. volume 是备份吗？

不是。卷仍可能损坏、误删或随宿主故障。备份需独立副本、保留策略和恢复演练。

### 6. liveness 与 readiness？

liveness 判断进程是否需重启；readiness 判断是否接新流量。把易波动下游塞进 liveness 会引发集体重启风暴。

### 7. 平均延迟为什么不够？

少量极慢请求会被平均掩盖，P95/P99 反映尾部体验。仍需结合请求量与完整分布。

### 8. 日志、指标、追踪如何分工？

指标发现范围和趋势，trace 定位一次请求慢在哪段，日志提供详细事件与上下文；通过 traceId 关联。

### 9. 什么是高基数标签？

user/order/trace 等唯一值作为 label 会创建海量时间序列，拖垮指标系统，应放日志/trace。

### 10. 为什么 504 不能直接调大超时？

可能只让慢请求占连接与线程更久，放大拥堵。先确定慢在哪层和资源瓶颈，再调整预算。

### 11. 扩容为什么可能更糟？

更多应用实例产生更多 DB 连接、重试和下游并发。瓶颈在共享依赖时，扩应用只会加压。

### 12. 如何判断消息积压能否恢复？

比较生产与消费速率、看最老消息年龄。消费率必须高于生产率，才会清空。

### 13. 为什么备份成功仍不够？

备份可能损坏、缺日志链、权限错误或恢复太慢。只有实际恢复并校验才能证明 RPO/RTO。

### 14. 滚动发布对数据库的要求？

新旧应用并存，schema 必须兼容；采用 expand/contract，不在第一步删旧列或改变旧语义。

### 15. 复盘行动项怎样才合格？

有具体系统变化、负责人、期限和验证方式，例如“新增连接池饱和告警并在故障演练中证明触发”，不是“以后注意”。

---

## 26. 章末实验（含预期证据）

### 实验 A：网络视角

分别从宿主与 Web 容器访问 API。预期：宿主走 `localhost:9991`，容器网络走 `cp6-api:5000`。记录 DNS、目标端口与结果，解释为何不同。

### 实验 B：卷持久性

在隔离开发环境创建测试数据，重建容器但保留卷，验证数据仍在。不要对含真实数据的卷执行删除；改为写出“若卷丢失如何从备份恢复”的方案。

### 实验 C：指标

访问 `/metrics`，找到 HTTP duration、retry queue depth、dead letter。在隔离测试库制造失败事件，观察指标变化；恢复后确认指标回落或状态改变。

### 实验 D：故障演练

在测试环境故意配置错误 API 目标端口，收集浏览器状态、代理日志、容器状态、应用日志；修复后用同一请求与业务断言验证。保留完整时间线。

### 实验 E：恢复

把备份恢复到全新测试库，不覆盖原库。校验表数、关键行数、最近交易，并完成登录、查询和一项写入；记录实际恢复耗时与 RTO 差距。

最终输出不是一句“实验成功”，而是一份另一位工程师可以照着复现的 runbook。
