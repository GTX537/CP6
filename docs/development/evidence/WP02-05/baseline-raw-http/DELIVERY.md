# W21-02：版本化完整目录页与原字节快照HTTP

新增实际普通PostgreSQL宿主上的v1目录页和原字节快照接口。目录总数、就绪状态、generation及页内容来自同一SQL观察；快照保留原JSON UTF-8字节及摘要。旧接口兼容，本项不运行完整HTTP采集器或发布CRM授权基线。

基线 `3209824ef71dc56bc251d92b75ab40463833c970`，分支 `codex/wave21-baseline-raw-http-20261019`，W21-01 PR297已合入；一次实际START为Handoff `692c7c8b95832074456addd66eb902b936299b95`。单总控串行，复用原工作目录及构建缓存。

## 行为及安全边界

- `/internal/crm/identity/directory/v1/versions`返回Contract、Source、Boundary、TotalCount、Items、NextCursor，pageSize为1～200。普通PG事务中，一条SELECT关联已就绪Bootstrap、Generation、全部条目计数和分页结果；空页仍返回源总数。cursor使用独立DataProtection目的，绑定来源元组摘要、tenant所属来源、generation、last aggregate和10分钟到期，不接受旧接口或其他来源游标。边界变化仍409，非法参数400，未就绪503。
- `/internal/crm/identity/directory/v1/snapshots/{aggregateId}`在一条SELECT观察相同的就绪/generation与准确条目；不存在404。以严格UTF-8编码原PayloadJson，不经JsonElement再序列化；原hash和已固定schema/身份语义不匹配则固定503。原JSON空白、转义和尾随空白在实际PG bpchar存储及HTTP传输中经过验证。
- 两个新路由只在IdentityDirectoryTenantHost存在时开放，缺失直接404；实际JWT/注册/当前租户与服务令牌授权、可信scope、普通LOGIN、mapping锁、固定安装identity/fingerprint检查与lease管理均复用现有宿主。响应no-store，不接受tenant/database查询参数，没有Owner回退。
- Source来源是冻结runtime及安装期望：tenant、issuer、region、bundle、storage identity hash。安装摘要v1定义为固定顺序字符串数组的System.Text.Json默认UTF-8序列化SHA-256（小写）：`cp6-identity-directory-storage-v1`、InstallationId(D)、Server、Port十进制、Database、DatabaseOid十进制、SystemIdentifier、原大写Fingerprint。无密码/动态请求字段。预期源须由可信配置提供，不能自动信任HTTP首个响应。
- 新内部reader仅在已验证普通事务内创建；原IIdentitySnapshotReader与旧读取行为保持兼容。没有新表、迁移、权限profile、安装seal或既有数据库升级。

## 实际验证及复用

证据根 `D:/CP6-local-test/wave21/w21-02/`，实际PostgreSQL18、产品配置注册/JWT/verifier/authority/controller/普通账号；测试只提供隔离签名key、合成数据和测试端口。原log/JSON/TRX及失败根保留。

| 运行 | 真实结果 |
|---|---|
| red-raw-http-20261019T043505Z | 1执行失败，原接口缺失返回404，新增能力RED |
| green-raw-http-20261019T043645Z | 1真实PG/HTTP A→B→A读取通过，准确总数及原hash一致 |
| raw-byte-boundaries-20261019T043900Z | 4通过：3项原生HTTP行为及1项宿主缺失拒绝；0失败/跳过 |
| cursor-source-digest-20261019T044014Z | 将cursor改为来源摘要以避免长来源字段扩大游标后，局部2项分页/隔离/旧接口兼容复查通过 |

最终去重4项通过（3原生+1无宿主），包含实际原字节空白/转义、hash和tenant篡改拒绝/恢复、A/B隔离、source与安装期望一致、总数/完整两页、跨tenant/跨协议游标拒绝、非法请求、未登记/匿名拒绝、未就绪503及旧接口200。Owner请求查询0，Borrowers归零；4个自有PG均有stopped.txt且无postmaster.pid，HTTP宿主全部处置。

复用[W21-01](../baseline-contract/DELIVERY.md)的核验/原始大小/schema结果，以及[W20实际宿主矩阵](../directory-configured-matrix/DELIVERY.md)未变JWT、权限漂移、撤权及scope时效证据；没有重复全仓测试或W20安装检查。源码SHA变化本身不触发重跑。

复现：设置`CP6_PERMISSION_PG_BIN=C:/Program Files/PostgreSQL/18/bin`和全新自有`CP6_PERMISSION_PG_ROOT=D:/CP6-local-test/wave21/permission-consumption-<unique>`，使用现有缓存执行`dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter "FullyQualifiedName~IdentityRawSnapshotHttpPostgresTests|FullyQualifiedName~IdentityRawSnapshotHostRequirementTests"`。

## 集中审查与剩余范围

一次作者SPEC/QUALITY审查覆盖完整diff、同一SQL观察、可信source/安装边界、RLS/普通事务复用、原字节/hash/schema、错误净化、游标绑定与上限、旧接口兼容及资源释放；发现游标可受来源文本长度影响后改为来源摘要并局部复查，无剩余阻断，不声称独立审查。

候选待正常PR/远端核对；随后W21-03实际有界HTTP采集及W21-04串联。完整WP02/全IAM写者覆盖、CRM授权采用、Owner最终写授权门、既有环境切换与真实DEV/业务/生产未完成。无Actions触发/取消、新工作树/代理、既有库操作或部署；台账/四状态同窗口同步。
