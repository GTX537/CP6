# W21-03：有界实际HTTP完整基线采集器

新增产品IdentityBaselineCollector，依次取得完整首轮目录、逐项准确原快照、完整末轮目录；来源、总数、边界及集合一致后才返回W21-01不可变基线。任何协议/HTTP/摘要故障或取消都不返回部分结果，不重试、不发布授权、不写CRM。

基线 `18d36ced499f4846ea6e50bda5e200182b5cbb06`，分支 `codex/wave21-baseline-collector-20261019`，W21-02经PR298合入；一次真实START为Handoff `95516e0df1602040292ed94f75483ed63cf359ce`。同总控单独实现、一次作者SPEC/QUALITY审查，复用原目录/缓存。

## 传输、完整性和预算

调用者显式提供可信origin、预期source、已固定runtime/validator及服务Bearer提供者。origin只允许HTTPS根路径、无userinfo/query/fragment；测试需显式AllowLoopbackHttp且地址为字面loopback IP。来源不从响应自举，不接受响应中的目标URL。每次请求使用同一固定路径前缀，aggregate/cursor仅转义为路径/参数数据。

产品构造器自己持有SocketsHttpHandler/HttpClient，禁用自动重定向、cookies和解压，默认TLS校验保持有效，header上限32KiB、连接超时30秒；仅测试assembly可注入handler。请求no-cache/no-store，响应须200、相同请求URI、application/json UTF-8且no-store、无内容编码。HTTP失败只公开固定状态码，不读取错误正文；网络错误固定净化，无自动重试或Owner回退。

首轮、末轮各自完整分页，逐页固定source/contract/boundary/TotalCount、pageSize≤200、最多100万条；禁止重复aggregate/游标、空进度、提前结束、超过源总数或预算。每次raw响应必须等于该次请求的准确条目，再由W21-01核验原UTF-8/hash/schema/tenant/version/deleted。最后集合顺序无关但内容必须完全相同，空完整目录仍只代表SOURCE_SNAPSHOT而非授权。

响应以ResponseHeadersRead串流读取，既检查Content-Length，也检查真实读取字节；单页1MiB、单raw HTTP包装9MiB（容纳1MiB原data的base64及JSON最坏转义开销），原data仍每项1MiB、累计默认256MiB。运行预算可调低，不能截断后冒称完整。JSON拒绝重复/未知/缺失属性、错误类型、损坏UTF-8；v1字段presence（包括isDeleted与nextCursor）不可由默认值补足。

一次操作默认10分钟，可配置正时限≤24小时，贯穿认证提供者等待、HTTP/body、校验与完成；异步token提供者即便不合作取消，也通过WaitAsync停止等待，发送前再次核对取消。同步阻塞的调用者委托仍应由调用者避免。响应/stream/请求均释放；一次失败没有公共partial对象，后续显式调用从头开始。

## 实际验证及复用

证据根 `D:/CP6-local-test/wave21/w21-03/`保存原log/JSON/TRX：

| 运行 | 真实结果 |
|---|---|
| red-collector-20261019T044500Z | 1执行失败于尚未实现的采集器，新增能力RED |
| initial-collector-20261019T044645Z | 1基本完整采集链路通过 |
| protocol-boundaries-20261019T044850Z | 32通过，0失败/跳过：完整/多页/原字节、缺项重复/冲突/来源、预算、HTTP状态、严格JSON、chunked大小、取消、空目录及真实HTTP重定向拒绝 |
| request-cancel-recheck-20261019T045007Z | 审查补齐每次请求准确条目和token等待取消后，局部6通过；含真实操作期限取消未完成body、取消不等待token提供者及正确路径回归 |

最终去重35项通过，无剩余失败/跳过。大部分用注入HTTP handler隔离协议，真实Kestrel loopback测试使用公开产品构造器，证明HTTP须明确许可且302不跟随、目标trap到达0；它不是实际身份宿主验收。测试服务器均在finally停止/处置，本窗口未启动PG。

复现：现有缓存执行`dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter FullyQualifiedName~IdentityBaselineCollectorTests`。复用[原字节/全集核验](../baseline-contract/DELIVERY.md)及[普通PG/HTTP端点](../baseline-raw-http/DELIVERY.md)未变证据；W21-04才用真实PG宿主执行产品collector并发/故障串联。

## 集中审查与剩余范围

一次作者SPEC/QUALITY完整diff审查覆盖可信源/固定目的地、TLS/重定向、Bearer边界、严格JSON、计数/全集/游标、资源上限、每次请求准确条目、取消/时限与不可变输出。补齐准确请求匹配和异步token等待取消后局部复查通过，无剩余阻断，不声称独立审查。

候选待正常PR/远端核对。W21-04实际PG串联及全轮收尾尚未完成；本组件不证明CRM已采用、全IAM写者已覆盖、Owner最终授权fence已完成或已有环境切换。完整WP02/公共基础、真实DEV/业务/生产未提升。无Actions、新工作树/代理、既有数据库操作或部署，台账/四状态同窗口同步。
