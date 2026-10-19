# W19-04：身份目录HTTP与普通数据库读取串联

隔离宿主真实串联已完成，最终4项HTTP/原生PG场景去重通过，0剩余失败/跳过；一次集中SPEC/QUALITY作者自审无阻断。复用前三项有效组件证据。当前候选待正常PR合并及远端核对，完整公共基础/真实DEV和生产验收未完成。

## 源码与真实身份

基线 `525b16052f1c8edbe39d0e53eea69720d0de54a8`（PR287），分支 `codex/wave19-directory-http-integration-20261018`。Handoff START `f72b56518405ea9b21dc0445e507d2b387879fdd`，路径 `tasks/WP02-05/messages/20261019T010543Z-wave19-http-start/MESSAGE.md`。仅增加两个测试文件，无产品、工作流、依赖、模型或迁移修改；同一总控复用原目录。

实际链路为：loopback Kestrel HTTP → 原AddCrmIdentityEvents / JwtBearer + CP6 Platform 0.10.3 claims检查 → fixture可信来源/registration authority经真实TrustedContextExecutorV1建立scope → 原CrmIdentityController → 原CrmOidcDirectory和ServiceToken当前数据库检查 → TenantIdentityDirectoryReader → 托管普通PostgreSQL A/B连接和三表RLS。

| 边界 | 实际实现/身份 | 不宣称的范围 |
|---|---|---|
| JWT | 夹具RSA私钥，通过CrmOidcCrypto签名；实际RS256/issuer/audience/expiry/claims校验 | 无生产私钥、真实issuer或网络JWKS/发现轮换验收 |
| token登记/撤销 | 原CrmServiceTokenRecordStore，实际ServiceToken/identity/outbox数据库写入 | fixture签发，不冒充/connect/token生产发行流程 |
| 当前租户/服务授权查询 | 原控制器和CrmOidcDirectory，独立fixture_owner控制连接 | 未声称全请求/全Main使用普通身份 |
| 可信上下文 | 实际Platform executor/accessor；测试专用HTTP verifier/authority，以已验证JWT及fixture注册映射构造 | 没有生产目录host配置/authority注册入口 |
| 目录读取 | 实际directory_a / directory_b普通登录、可信manager、RLS和DTO reader；查询验证两个idle backend，事务已结束 | 无高权限目录回退；不是已有DEV数据库切换 |

IP和端口仅本机隔离使用：PG55435，每个HTTP宿主loopback动态端口。关闭后台bootstrap/dispatch以只验证本次受影响读取；无mock认证、mock数据库或替代授权查询。两个strict mock仅为CrmOidcDirectory未调用的浏览器黑名单/密码策略依赖。签名私钥/数据库密码仅夹具内存与既有私有PG文件，不写报告或提交。

## 实际本地验证

原始证据在 `D:/CP6-local-test/wave19/w19-04/`（log/JSON/results-TRX）。PostgreSQL18.6，实际Core/IdentityPriority迁移及W19-02三表首次安装，原锁定.NET8/Platform依赖。

| 运行前缀 | 真实结果 |
|---|---|
| http-composition-20261019T010831Z | 3通过/1失败/0跳过；失败是重复tenant_id在bearer阶段返回401，测试错误期待控制器403 |
| http-auth-diagnostic-20261019T011005Z | 仅受影响场景1失败，补充case编号定位到重复tenant claim；未重跑其他3项 |
| http-auth-contract-20261019T011051Z | 修正预期后该场景1通过/0失败跳过，包含后续正常读取；其他3项直接复用未变证据 |

已读取实际包nuspec对应源码提交 `1277da5e0a20c6d96f0a95d0e9b4f20e0008332a` 的 `src/CP6.Platform.AspNetCore/Cp6JwtClaimsValidator.cs`，它明确拒绝重复必需claim；修正的是测试合同预期，没有弱化认证或修改产品。最终4项按场景去重，保留两次真实失败。

覆盖结果：

- A→B→A经真实HTTP读取版本和自己的tenant快照；外租户快照404，响应tenant正确，manager两个普通pool，Borrowers=0。实际pg_stat_activity为directory_a/directory_b、idle且无backend事务；auth控制连接实际session_user=fixture_owner。
- 缺少/非法JWT、错误audience、已过期JWT、重复tenant claim返回401。已签名但未登记token、非reader客户端、非规范tenant文本、client/tenant错配返回403；随后合法请求200。
- 已成功的token经真实RevokeAsync撤销后再次读取403；新的有效token在实际Sys_Tenants停用后403，恢复后200，不复用旧授权成功结果。
- 跨租户cursor400，tenant查询覆盖/重复pageSize/超限pageSize/非法cursor400；实际角色投影变更使旧cursor409。撤销仅directory_a的Snapshot SELECT时，授权查询仍成功但page/snapshot均固定503；恢复权限后成功，证明没有Owner fallback。Bootstrap not-ready时两入口均503，恢复后200。
- 所有被断言响应均no-store，固定目录错误体仅一个code字段，无SQL或凭据泄漏。组件取消、catalog/mapping锁等待与事务已在W19-03通过，本项不重复。

复现（新根、单fixture集合，避免复用旧PG目录）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave19/permission-consumption-w19-04-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter 'FullyQualifiedName~IdentityDirectoryHttpPostgresTests' -v:q
```

三次自有PG均stopped.txt且无postmaster.pid。六次HTTP宿主创建均由await using关闭Kestrel、app、manager和signer；三个testhost进程均退出。原依赖和分析告警保留，无升级/抑制。无新增产品缺陷，因此未新增BUG Issue。

## 审查、复用和收尾

唯一总控完成本任务一次集中作者SPEC/QUALITY审查：完整两份测试diff、真实注册/认证未替代、受控fixture信任边界、当前数据库授权、普通读路径、私钥/密码不落提交、失败预期来源和资源释放。无独立审查声称，无剩余阻断。

复用[W19-01目录一致性](../directory-consistency/DELIVERY.md)、[W19-02普通账号隔离](../../WP02-02/identity-directory-isolation/DELIVERY.md)、[W19-03可信reader](../trusted-directory-reader/DELIVERY.md)有效且未变化的相关结果，不把重叠用例相加成覆盖目标。共享roadmap/四状态/本轮dispatch同一窗口同步；正常合并及远端包含性核对后关闭本轮四个有限窗口并发布Handoff收尾。

仍待独立后续任务：生产配置/可信HTTP host适配与最小授权查询身份、既有环境目录安装升级/恢复与漂移验证、不可变全目录协议及CRM消费、Owner最终撤权门、真实业务DEV/UAT/PROD验收。当前没有默认Program切换、网络JWKS/真实浏览器验收、生产发布、既有数据库变更、Actions触发/取消或新worktree/代理。
