# W347-01 · WP02-28 共库签发与原撤销重放

2026-11-09，COMPAT-VERIFY 在固定 DEV-2 从 Main `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 领取，官方 START 为 06:50:27 UTC。B4 的共库服务令牌验证通过。两份 W345 发布物共用新隔离 PostgreSQL 上的原服务令牌合同，当前制品停机后旧制品重启并继续处理原 JTI。 [原任务](../../task-plans/WP02.md#wp02-28) · [W345 制品与独立 pin](../W345-01/DELIVERY.md)

## 实际输入与环境

两份原入口 DLL 源分别为旧 `585ab63045930fcae131305c927aadb458fa544e` 和当前 `634ca5cc2ffda8eadbfd109c922ce59dd2d3eb15`。开始和结束都按 W345 独立 pin 核对收据、完整目录、依赖闭包和实际正式包归档。复用 W345 已通过的五包运行身份观察。两份制品的 `.deps.json` 均绑定 Npgsql 8.0.8 和 PostgreSQL EF provider 8.0.11。本机 SDK 8.0.424，ASP.NET Core 与 .NET 8.0.30。验证器以独立 content root 和真实 Kestrel HTTPS 启动原发布 DLL。

新自有 PostgreSQL 18.6 的业务库为 `w347_service_token`，loopback 55438，system identifier `7706063513994744472`、OID `16401`。使用已有 TenantStorage CLI 的 `install-service-token-v1 --stdin` 和原目录控制命令初始化两个合成租户。实际安装 `3019d496-47e6-4c52-a77f-fdcd083930fc` 的 baseline SHA-256 为 `D2218A1ED060A533645F742C40EC4C17828918D1C5D904EFB156EBAD4AFD00EE`。安装前业务库为空，管理标记在另一库。实际收据、现场目录指纹前后匹配，令牌表 FORCE RLS，默认连接无令牌写权限。共存时 `pg_stat_activity` 读到两条 A 普通登录连接和一条 B 普通登录连接。业务输入仅为本窗生成的模拟租户、客户端与签名材料。 [五表安装合同](../WP02-02/service-issuance/DELIVERY.md) · [验证器](../../../../eng/wp02-28/verify_service_token_pair.py)

## 实际 HTTP 与 PostgreSQL 结果

| 顺序 | 实际响应 | 物理库结果 |
| --- | --- | --- |
| 旧版 A 签发原 JTI | `/connect/token` 200 | 1 条未撤销记录。JWT issuer、audience、subject、租户、客户端、scope、JTI 和过期时间符合原记录与配置。 |
| 当前版 B 尝试撤销 A 的 JTI | `/connect/service-revocations` 空体 200 | 原协议的隐匿响应。令牌、快照、generation 和 Outbox 全部逐行相等。 |
| 当前版 A 撤销旧版签发的原 JTI | 空体 200 | 一次撤销和 row version 更新。新增一个版本 1 快照和一个优先 `token.revoked.v1` 事件，A generation 从 0 到 1。 |
| 当前版 A 签发另一原 JTI | `/connect/token` 200 | 只新增一条未撤销原记录。 |
| 当前版退出，旧版重新启动，A 撤销第二个原 JTI | 空体 200 | 原签发事实保留，新增一个版本 1 快照及一个优先事件。A generation 到 2。两条令牌、两个快照和两条 Outbox。 |
| 重启的旧版分别重放两个原 JTI | 两次空体 200 | 两笔原撤销时间、row version、快照、generation 和 Outbox 原字节逐行相等。 |
| 真实当前制品使用错误 InstallationId 尝试签发 | 503 `temporarily_unavailable` | 上述物理状态完整相等。 |

两个撤销均核对真实事件数据与原快照、issuer、subject、JTI 和签发过期时间一致。按数据库快照原文本和 Outbox 原字节重算的 SHA-256 均匹配存储值。每次 HTTP 都检查 `no-store`。该验证覆盖原撤销的重复处理，签发仍按原合同每次生成新 JTI。

## 证据、失败和范围

最终命令退出 0。原件 `D:/CP6-local-test/WP02-28/05f2c34be0f843ee9abbd00a70fe17d1/w347-results.json` 记录固定制品、四个实际宿主、每步 HTTP、业务行和停机。最终从新库准备至停机实测 75.4 秒。原首次运行 `7a7aa95b593149e2a4cb7dc731dcb883` 的旧宿主返回 500，日志指向全 Program 的 C02 读取者必填配置。补充与现有夹具相同的已登记读取者后，`18db3f2f40b748de865f4a47dc5fd37b` 通过。最终验证增加快照和事件原字节关联以及失败清理，使用上面的最终原件。三个自有数据库实例均停止，四个最终宿主均退出，独立复查 55438 无监听。

两宿主 `/health/release` 均为 200。Version、Git SHA 和 digest 来自受控启动配置，`latestMigration=null` 与实查无 EF history 一致。业务证据来自安装布局、实际 HTTPS 和物理行。此五表布局的全局 OperLog 旁路尝试返回 `42P01`，相关日志保留。令牌撤销自身的原子记录和优先 Outbox 在同一事务实际成功。

本次核的是同一已安装 schema 上的本地应用切换和回退。WP02-28 仍有正式兼容矩阵、正向 DDL 迁移和发布交接范围。现有宿主回退判断及 R2/GHCR 候选门沿用原规则。本窗使用现有制品和 CLI，全部在本地执行，无 Actions 触发、取消或工作流变化。交付为本地 `codex/w347-01` 候选，DEV-2 保留给 COORD 集中审查、PR 与官方 finish。 [原发布边界](../../../client/r2/README.md)

复现命令使用现有 Python 3.13，从任务工作区执行。验证器创建新私有根，CLI 安装输入走标准输入。

~~~powershell
$w347Python = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13.13-windows-x86_64-none/python.exe'
& $w347Python -B -X utf8 eng/wp02-28/verify_service_token_pair.py `
  --publish-root 'D:/CP6-local-test/WP02-28/146aa60c2fe84c6aaad680d2faaa4ec5' `
  --package-cache 'C:/Users/tt/.nuget/packages' `
  --tenant-storage 'D:/CP6-local-test/wave18/w18-01/artifacts/bin/CP6.TenantStorage/debug/CP6.TenantStorage.dll'
~~~
