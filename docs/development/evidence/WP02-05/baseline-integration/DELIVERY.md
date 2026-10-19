# W21-04：实际完整基线矩阵与BUG-300修正

产品collector通过实际JWT/可信普通PG宿主完成A/B完整目录采集，并验证并发变更、缺件/摘要故障、当前撤权、完整空目录、原1MiB数据及取消恢复。矩阵发现并修正[BUG-300 P0](https://github.com/GTX537/CP6/issues/300)：目录总数被EF提前同步执行，违反同一SQL观察且阻止取消释放连接。最终9项有有效通过证据，候选待正常PR及远端核对后关闭Issue。

基线 `406b9cfbf16dcc82252d5d3505e7a18a47900769`（PR299）。W21-04真实START为Handoff `43cffcd3dbbe70c7b164c8c47cad4520d1da7188`；发现BUG后由同总控接续原窗口，范围记录 `c4e15606f15df80d9ff71283e7fbff0a9397101b`。从核对一致的最新main建 `codex/bug-300-baseline-count-cancellation`，保留当前自有矩阵，原集成分支仅在基线；没有新工作树/代理或重复START。

## 根因及最小修正

原`Count = all.LongCount()`捕获外部IQueryable，EF在参数提取时同步单独执行COUNT，随后才执行原页SQL。可重复观测：客户端取消、服务端RequestAborted已触发，但ordinary directory_a仍active/Lock/relation，borrowers=1；pg_stat_activity确认`standalone_count=true`。因此W21-02原报告“总数与页同一SQL”的表述在原候选不成立，本报告及原报告勘误明确覆盖这一点，不把早期分页成功当作原子观察证明。

最小产品改动是将COUNT关联外层已就绪state.TenantId，使其必须作为服务器子查询随外层异步SQL执行；原tenant谓词/RLS、mapping锁、安装检查、来源、分页与旧接口均不改变。修正后，在控制方仍持有Snapshot排他锁时取消，PG查询和lease及时结束、Borrowers归零；释放锁后同collector新尝试成功。新版本页的就绪/generation/count/items现处于同一语句观察。

测试宿主只增收到/取消/完成三个计数用于定位HTTP→PG取消边界，不记录JWT、密码、连接串或请求正文。没有新增产品诊断接口/控制hook、存储迁移、授权范围或依赖。检查主Program的MVC配置仍为默认Web JSON，没有忽略null或自定义命名覆盖；本矩阵使用产品身份注册/认证/Controller及公开collector构造器，不声称完整Main启动或既有环境部署验收。

## 实际验证

证据根 `D:/CP6-local-test/wave21/w21-04/`，原log/JSON/TRX和失败根保留。全部为原生PostgreSQL18、真实迁移/Bootstrap、v2显式隔离安装、真实JWT及普通directory_a/b；测试数据为合成fixture。

| 运行 | 真实结果 |
|---|---|
| native-baseline-matrix-20261019T050151Z | 9执行：8通过、1失败，无跳过；失败为取消后连接未及时归还 |
| cancellation-trace-20261019T051235Z | 1失败：received=1、aborted=1、completed=0、borrowers=1、PG仍锁等待，确认取消已到HTTP |
| count-shape-trace-20261019T051418Z | 1失败：进一步确认PG独立同步COUNT，standalone_count=true |
| correlated-count-recheck-20261019T051712Z | 修正后4项专项通过：取消/恢复、A/B完整分页、分页/快照/末轮并发变更、完整空目录，0失败/跳过 |

按最终用例去重9项通过，无剩余失败/跳过：4项当前受影响专项重验，原5项未变证据复用。包含公开产品collector的A→B→A准确source/原字节/hash/版本；三个阶段真实角色写入均拒绝当前尝试且新尝试恢复；hash损坏及Bootstrap未就绪503/恢复；首轮后删除准确snapshot为404且不能跳过；撤销/租户停用在下一读取403；可信预期tenant/安装身份不符拒绝；空原生目录完整但非授权；1MiB原始字节经过PG/HTTP/最终基线无损；请求取消后释放并恢复。Owner请求查询0，实际普通角色会话已核对，HTTP宿主退出。

复现：设置`CP6_PERMISSION_PG_BIN=C:/Program Files/PostgreSQL/18/bin`及全新自有`CP6_PERMISSION_PG_ROOT=D:/CP6-local-test/wave21/permission-consumption-<unique>`，使用既有缓存运行`dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter FullyQualifiedName~IdentityBaselineHttpPostgresTests`。同一fixture串行，勿并行抢55435。4个本窗口自有PG均有stopped.txt、无postmaster.pid；此前W21-02四个也已停止。

复用[W21-01](../baseline-contract/DELIVERY.md)原byte/schema/不可变结果、[W21-03](../baseline-collector/DELIVERY.md)严格HTTP/预算/重定向/JSON/取消证据；[W20矩阵](../directory-configured-matrix/DELIVERY.md)未变注册、权限漂移和安装边界不重跑。既有依赖/分析告警保留，未在本窗口升级或抑制。

## 集中审查及收尾边界

一次作者SPEC/QUALITY覆盖本窗口完整diff、BUG根因与关联服务器查询、取消/事务/池归还、真实writer并发、数据恢复及证据适用范围。专项修正通过后无剩余阻断，不声称独立审查。BUG-300仅按本地代码及隔离复现范围关闭，不新增真实DEV门禁；Issue须等正常合并及远端main核对，当前仍Open。

本轮四个有限窗口的实现/验证完成条件已具备，尚待本候选集成、Issue关闭和共享文档/Handoff最终收尾；持续目标active。完整WP02/全公共基础、全IAM写者覆盖、CRM消费者采用/发布、Owner最终写授权/撤权fence、既有环境升级与真实DEV/业务/生产均未完成。无Actions触发/取消、新树/代理、既有库操作或部署；原用户资料与失败证据保留。
