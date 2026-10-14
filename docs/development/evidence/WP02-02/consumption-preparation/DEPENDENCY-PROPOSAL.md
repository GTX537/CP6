# WP02-02 Main 依赖采用提案（未应用）

此提案只作后续独占窗口审查；当前正式依赖、锁文件、NuGet 签名策略和宿主实现均未改。候选 `0.10.3` 尚未登记/构建/发布/批准，无 producer SHA 或可采用包哈希。

[固定源码清单](inventory-final.json)记录 Main `1ab2d031e3c501662ecb89a33839fac1cba55d33`、Platform `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`、45 个独立准入指纹和 100 个项目/依赖输入。100 项工作树文本均与固定 Git 字节 LF 等价；输入始终从 Git 固定对象读取，CRLF 差异单列，不冒充字节完全一致。原产品输入基线 `9a0155dec1c3366df8a6631f3d8fdb086aa32fc8` 到 Main 派工基线仅文档增量。

静态清单包含 51 个项目、19 份包含 Platform 的锁；声明图不执行 MSBuild 条件，实际既有锁按 TFM 保留 dependency/requested/resolved/contentHash。初稿 inventory.json 将两份 P10 工具关联锁误分为受影响；inventory-final.json 已按全部 tools/p10/ 排除，初稿保留以追踪纠正，不能作为最终范围。

## 精确采用位置

[未应用补丁](PROPOSED-DEPENDENCIES.patch)仅展示四个直接引用从 `[0.10.2]` 到 `[0.10.3]`：Core 的 Messaging/EntityFramework、WebApi 的 AspNetCore、DatabaseCompatibility.Probe 的 EntityFramework。这里只检查补丁可应用性，未执行 apply。未来须先通过正式版本登记、当前信任/feed 校验、独立批准的 consumer 身份和互斥窗口，再重新基于当时 main 生成补丁。

WebApi 的现有 Platform 运行闭包为 Abstractions、AspNetCore、Contracts、EntityFramework、Messaging 共五包；producer 的七包检验额外包括 Deployment/Release，不等于 Main 运行依赖七包。Persistence.PostgreSql 没有直接 Platform 引用，通过项目闭包间接消费。保留既有 SQL Server 历史代码；此次没有新增其验收要求。

## 后续须重新生成并审查的 16 份锁

不得用文本替换版本、复制旧 contentHash 或凭空填新 hash。经授权的 restore 必须继续使用现有签名必需策略/source mapping、精确版本、真实正式 feed，以实际 NuGet 锁语义生成，再核非 Platform 依赖漂移。归档 SHA512 与 NuGet lock contentHash 不能混用。

- `CP6.Core/packages.lock.json`
- `CP6.Oidc.IntegrationTests/packages.lock.json`
- `CP6.Persistence.PostgreSql/packages.lock.json`
- `CP6.Space.Infrastructure/packages.lock.json`
- `CP6.Space.IntegrationTests/packages.lock.json`
- `CP6.Tests/packages.lock.json`
- `CP6.WebApi/packages.lock.json`
- `eng/contracts/postgresql-tests/packages.lock.json`
- `eng/crm/erp-integration-tests/packages.lock.json`
- `eng/crm/erp-live-fixture/packages.lock.json`
- `eng/crm/identity-events-fixture/packages.lock.json`
- `eng/crm/legacy-exit-tests/packages.lock.json`
- `tools/CP6.DatabaseCompatibility.ApplicationProbe/packages.lock.json`
- `tools/CP6.DatabaseCompatibility.MigrationProbe/packages.lock.json`
- `tools/CP6.DatabaseCompatibility.Probe/packages.lock.json`
- `tools/CP6.DatabaseCompatibility.RuntimeProbe/packages.lock.json`

## 独立历史 P10 工具（排除三份）

- `tools/p10/PreflightInputs/packages.lock.json`
- `tools/p10/ReleaseVerifier.Tests/packages.lock.json`
- `tools/p10/ReleaseVerifier/packages.lock.json`

上述工具继续 Release `[0.10.1]`；不为新业务包升级历史发布验证器，不将其三份锁算入五包运行闭包。

## 仍待验证的完整兼容性

本轮探针只引用从独立 hash 核对的真实归档提取的四个 context DLL，以 SDK 8.0.424 / .NET 8.0.30 执行合成接口场景。其 MSB3277 明确记录 framework DiagnosticSource 8 与包引用 10 的冲突，完整依赖兼容性保持 Unknown。下一阶段须在正式包和批准源码固定后，实际 restore/build 相关 Main 项目及必要依赖，解决适用的依赖冲突并执行真实宿主/数据库门禁，不能以探针成功或本未应用补丁作为兼容性证据。

本轮未 restore/build Main 产品项目，没有改 Program/DI/EF、角色/连接池、模型缓存、迁移、存储路由和数据库。原 17 PGT 全部 NOT_RUN，详见 [验收边界](PGT-BOUNDARY.md)。
