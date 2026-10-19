# W20-04：实际配置宿主的 PG / HTTP 验证矩阵

实际产品配置、JWT认证、可信source/authority、普通PG当前授权及目录端点完成7项定向矩阵，最终7通过、0剩余失败/跳过；复用W20-01/02/03未变有效证据。产品实现无需追加修改。本轮4个有限窗口的实现与验证均已交付，当前候选待正常PR/远端核对，随后只做共享状态与资源收尾。

基线 `08820bd53067cac140adc755a2c3373ee9e769ab`，分支 `codex/wave20-directory-matrix-20261018`。START为Handoff `64f193a3961d281b0da056b5f4d8f36a89a628cc`，`tasks/WP02-05/messages/20261019T035846Z-wave20-matrix-start/MESSAGE.md`。总控单独执行，单作者集中SPEC/QUALITY；没有独立审查代理。

## 最终覆盖

| 场景 | 实际验证 |
|---|---|
| JWT及注册 | 缺失/坏格式/篡改签名、错误audience、过期、重复tenant拒绝401；未记录、非reader、非规范tenant、client跨租户拒绝403；有效token及原60秒偏移内token成功 |
| 当前授权 | A已记录token撤销后403；A停用后403，B仍可读；A恢复后成功，全部通过普通账号查询 |
| 原HTTP合同 | 本租户快照成功；跨租户cursor、非法/重复query拒绝400；真实Owner源写入引发generation变化后旧cursor409；Bootstrap未就绪503，恢复后成功 |
| 故障/漂移 | 错误fingerprint、数据库OID、普通账号密码、过期注册均固定503；实际撤销Snapshot SELECT后versions/snapshot均503，恢复原安装ACL后成功，无Owner fallback |
| 配置冻结 | 修改原OIDC client租户/启用及原identity reader/region集合，不能扩展权限或重定向已冻结宿主；原A仍为A，新增读者/重定向B均拒绝 |
| 等待期间过期 | 原生PG表锁阻塞实际普通目录查询，经pg_stat_activity确认等待；推进可控时钟越过source TTL，解锁后丢弃结果并503，释放lease；新请求成功 |
| HTTP取消 | 原生PG锁等待期间取消实际HTTP请求，客户端取消且服务端释放lease，解锁后新请求成功 |

每项检查请求路径Owner SQL调用为0、连接Borrowers归零；有响应路径保留no-store，固定错误body不含数据库细节。临时签名key/测试令牌登记属于隔离夹具；产品配置、JWT校验、source/authority、授权和reader均真实实现。未把fixture发令牌当成真实OIDC发行端点验收，未使用旧W19 fixture verifier替代产品。

## 运行、失败与修正

证据根 `D:/CP6-local-test/wave20/w20-04/`，PostgreSQL18.6/.NET8与原锁定依赖；保留JSON/TRX/日志。2个自有PG均停止，stopped.txt存在、无postmaster.pid。

| 运行 | 原始结果与处理 |
|---|---|
| actual-matrix-20261019T040011Z | 新selector名称未命中外部runner的PG启用条件，编译成功但7项NotExecuted；不计通过。只修正runner启用参数，不改测试skip合同 |
| native-matrix-20261019T040108Z | 原生执行7项：4通过、3失败。权限测试恢复A授权改变了既有用户ACL数组顺序，安装指纹正确保持拒绝；污染后续JWT与等待场景 |
| affected-recheck-20261019T040255Z | 只复查上述3项，3通过、0失败/跳过。测试按原安装顺序恢复A/B ACL，校验原ACL及原fingerprint完全恢复；未改产品seal或重新封存 |

最终7项去重通过 = 首次原生执行的4个未变成功场景 + 修正测试恢复逻辑后的3项局部复查。没有把重复执行、每个断言或HTTP请求累加成覆盖数量。既有用户对象指纹含ACL数组顺序；系统ACL的有效权限规范化是另一份既有合同，不能凭恢复等价权限改写原seal语义。本次属于测试状态恢复错误，无新增产品BUG；原失败全部保留。

执行选择器 `FullyQualifiedName~IdentityDirectoryConfiguredHttpMatrixTests`；必须显式设置CP6_PERMISSION_PG_BIN及新的CP6_PERMISSION_PG_ROOT（permission-consumption-*）、复用原artifacts/NuGet缓存。集合串行共享单个自有PG，避免每项创建工作树/依赖副本。取消测试只给fixture客户端增加CancellationToken，不改产品调用合同。

直接复用[W20-01](../../WP02-02/directory-authorization/DELIVERY.md)7项授权PG、[W20-02](../../WP02-02/directory-installation/DELIVERY.md)2条真实PG/CLI生命周期及4个旧CLI入口smoke、[W20-03](../directory-host/DELIVERY.md)18配置+1实际HTTP采用链路。CLI和产品实现未变，不重复安装/发布或全仓测试。合并后按范围blob和祖先关系核对即可复用相同代码结果。

## 一次作者集中审查与剩余范围

核对完整任务diff、实际认证/普通SQL观察、故障恢复隔离、取消及时间边界、失败证据真实性和未变复用范围。集中审查定位测试ACL恢复差异后，只复查相关3项；无剩余阻断。未增加产品钩子、改变存储指纹或依赖/workflow。

正常集成后关闭有限W20-04及本轮状态；持续目标在最终文档远端核对前仍active。完整WP02/公共基础、全Main普通身份、既有环境升级/切换、冻结完整目录/CRM、Owner最终授权写fence、真实DEV/业务/生产仍未完成。无Actions触发/取消、新工作树/代理、既有库操作或部署。
