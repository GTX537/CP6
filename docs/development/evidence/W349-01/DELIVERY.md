# W349-01 · WP02-28 本地回退判定与采用输入

2026-11-09，ROLLBACK-COMPAT 从 Main `3b3773bfd5d6a452ff25fbe67f5278c08f6c18a1` 在固定 DEV-2 领取，官方 START 07:10:57 UTC。实际离线 CLI 对 W347 已观察的 PostgreSQL 服务令牌切换返回 `COMPATIBLE_FOR_OBSERVED_LOCAL_ROLLBACK`。唯一方向是当前 `634ca5cc2ffda8eadbfd109c922ce59dd2d3eb15` → 旧 `585ab63045930fcae131305c927aadb458fa544e`。 [WP02-28 原卡](../../task-plans/WP02.md#wp02-28) · [实际 W347 回退](../W347-01/DELIVERY.md)

## 判定输入与结果

- 复用 W345 两个独立收据 pin、已固定的源码、实际发布闭包及从正式包归档验证的 Platform 0.10.3 期望。以收据、入口 DLL 和两个驱动 DLL 的实际 SHA-256 同 W347 观察匹配。Npgsql 为 8.0.8，EF PostgreSQL provider 为 8.0.11。发布目录由原收据校验器定位，W349 的制品身份比较使用源码与字节身份字段。 [W345 原收据](../W345-01/DELIVERY.md) · [离线 CLI](../../../../eng/wp02-28/verify_local_rollback.py)
- 独立固定 W347 最终原件 SHA-256 `11ec68897b08607832255eeff7d54e3f69895049cf88ef70acc59a5d0bff6ec5`，执行者源码 `84cd5a3239b974088a7c432df996f6469a3f6e5d` 随 Main PR #805 采用。判定器读取原完成、两份实际切换源和宿主、监听、数据库停止记录。撤销原子性、重放与 Outbox 字节来自 W347 已执行的同一次业务观察。
- 请求须绑定该观察的 PostgreSQL 18.6、端口 55438、数据库 `w347_service_token`、OID 16401、system identifier `7706063513994744472`、安装 `3019d496-47e6-4c52-a77f-fdcd083930fc`、`service-token-v1` 基线哈希及真实目录指纹。安装回执和库身份一致，前后现场指纹一致，令牌表 FORCE RLS，默认连接无令牌写权，EF history 为 0。结果只覆盖这份已观察的同 schema 切换。

## 实际验证

Python 3.13 的 7 项定向单测通过。最初 `red.log` 记录实现模块尚缺失时的导入失败。真实 CLI 最终执行 1 次正向和 9 次拒绝，正向使用原 W345 制品、两个原收据、正式包缓存与 W347 原始文件，退出 0。缺少或改动观察原件、错误收据 pin、未知源、错误驱动、错误数据库 OID、错误目录指纹、反向及额外放行字段均退出 1，返回有原因的 `REFUSED`。首次验证编排的 here-string 在解析阶段失败，按字面保留换行后完成上述实际执行。

本机证据根 `D:/CP6-local-test/CP6-72H-20261109/W349-01/offline-local-rollback/`。`observed-current-to-prior.request.json` 是实际精确请求，`observed-current-to-prior.result.json` 为输出，原始字节 SHA-256 `1ccf9d83a7ce709d72dd5ab7eb57ea44a01c56870edfd09f08a42b94c5643170`。`cases.json` 索引每次退出码和原输出。W349 只读取制品及 W347 的实际观察，产品构建、PostgreSQL 和 HTTPS 均复用原执行证据。

~~~powershell
$w349Python = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13.13-windows-x86_64-none/python.exe'
$w349Evidence = 'D:/CP6-local-test/CP6-72H-20261109/W349-01/offline-local-rollback'
& $w349Python -B -X utf8 eng/wp02-28/verify_local_rollback.py `
  --request "$w349Evidence/observed-current-to-prior.request.json" `
  --evidence 'D:/CP6-local-test/WP02-28/05f2c34be0f843ee9abbd00a70fe17d1/w347-results.json' `
  --publish-root 'D:/CP6-local-test/WP02-28/146aa60c2fe84c6aaad680d2faaa4ec5' `
  --package-cache 'C:/Users/tt/.nuget/packages'
~~~

## R2 源审查交接

交接给 Release Owner 的本地参考是本报告的固定 Git 引用与上面的结果原字节和哈希。结果 `schema=CP6-WP02-28-LOCAL-ROLLBACK/1`、`authority=LOCAL_EVIDENCE_ONLY`，供源审查核对 `decision`、`direction`、`artifacts`、`drivers`、`database`、`layout` 与 `observation`。R2 Schema 2 的 SourceGateReport 继续来自受保护 Tag 实际生成的源门报告，候选与部署仍使用既有签名、迁移、镜像 digest 和批准证据。生产宿主的回退判定保留 `COMPATIBILITY_EVIDENCE_MISSING` 边界。本地结果的范围止于 W347 物理库的服务令牌场景。正向 DDL 迁移、其他布局以及正式发布采用分别保留。 [R2 候选边界](../../../client/r2/02-signing-candidate-artifacts.md) · [DevOps 发布权威](../../../devops/README.md)

本窗无 Actions 触发、取消或工作流变化。交付为本地 `codex/w349-01` 候选，DEV-2 交由 COORD 完成集中审查、集成及官方 finish。
