# W420-01 · 同源 RMA 与 FileContent Owner 夹具

DEV-2 于 2026-11-09 18:56:07.205331 UTC 从 Main #856 `1a9eeb74d282b856012a0f4b64c9f2c97cc52c48` 开工。唯一 PostgreSQL 18.6 副本沿用 W372 的 `127.0.0.1:55567/w372_live`、OID `16386`、system `7706117870886737900` 和安装 `2ad5d2ef-54c4-4abd-9bf9-5d2b0a1a2cc4`。原 A/B 及其普通数据库角色映射均保留。W372 v2 pin 为 `3aa7b1c19332be13d0c0d5e04ba92f20717bca84d0abeca832e1317f93a885cc`。[停机复制](/D:/CP6-local-test/CP6-72H-20261109/W420-01/source-clone/copy.json) · [W372 原包](/D:/CP6-local-test/CP6-72H-20261109/W372-01/rma-submit-20261109T103302-90db8e21/submit/archive-v2-submit/bundle/manifest.json)

## 实测

- 写入前用 PostgreSQL 原生 `pg_restore` 提取封存 v2 的 head 和两个 version，以原生 COPY 逐字节对照 A 当前数据、原 journal 和出库切片。写入后 A/B 的各一条 head、两条 version、Ready 迁移、出库切片和原 journal 均与各自的写前 COPY 一致。共享原流水四条旧记录均逐字节保留，只增加本轮六条真实 Owner 原流水。[写前原件](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/before) · [写后原件与结果](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/after/result.json)
- 沿用两租户原 RMA actor 和各自的角色 `4371`。夹具补三个资源的当前 scope 和所需动作，复用或建立本租户 `ENG_REV` 编号输入。实际 `EngineeringProductOwnerV1`、`EngineeringRevisionOwnerV1` 与 `FileContentOwnerV1` 各自提交原操作。两租户的文件 ID 都是 `f1445ce0-0fb6-4b15-9ba4-4dea96bf91f3`，上传操作 ID 都是 `7e01bf8b-a2ea-4ad3-a790-aaaad4a98829`，对应不同的父版本和各自 45 字节的原文件。B 在自己的同键上传前查询 A 文件实际得到 `403 OBJECT_FORBIDDEN`。两次同键重放均返回各自的原回执，原操作读取为 `COMMITTED`，实际 LocalFileStore 下载逐字节吻合。业务输入、会话 principal 和扫描器为明确的合成夹具，数据库权限和 Owner 路径为真实执行。此处由本地 Owner 数据库角色执行组件路径。[Owner 原输出](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/owner.stdout)
- 正常 `file_content_history.py export` 和固定 pin `verify` 在 A/B 均成功。每份归档的内容、head、event、原流水和被引用父版本各一条，与按原 ID 独立执行的五份 PostgreSQL COPY 完全一致。A 的准确 pin 按 B 验证时得到 `file-content-history-manifest-rejected`。[A 包](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/after/A-history/bundle/manifest.json) · [B 包](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/after/B-history/bundle/manifest.json) · [跨租户拒绝](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/after/A-history-as-B.stderr)

| 租户 | FileContent manifest SHA-256 | FileContent 快照 |
| --- | --- | --- |
| A | `080cf46538994a00cd317cae0a239e728113802d52c2d183e7b2b826864202bd` | `00000006-00000004-1` |
| B | `af204315a82a2968efaa668bbebe3e3ec3d552a4645f3abb78c3d7a0b145cbf8` | `00000011-00000004-1` |

## 复现与收尾

在 W372 原停机源的新专属副本上，先用 `verify_same_source_file_content.py before` 核封存原包，再运行 `VerifySameSourceFileContent.cs` 编译出的 `CP6.Tests.dll` 夹具，最后用 `verify_same_source_file_content.py after` 核写后原件及正常归档入口。复用 W383 的 Core 依赖和窄 Owner 权限定义。封存 Core SHA-256 为 `1b7b9bb2af0b5d8d5a5eb075582bdb9a68cfacb27fdb3a67f3858dfc47076f00`。相应 Owner、权限读取、上下文与 PostgreSQL 依赖源码在 W383 封存版本和本基线一致。单次 csc 退出 0，夹具 SHA-256 为 `758e1ec89f9daa4af62f8f01563a639240cd66d138fb6f13a6013f8b258892ad`。[可复现 Owner 夹具](../../../../eng/wp02-02/rma-tenant-archive/VerifySameSourceFileContent.cs) · [原生比较器](../../../../eng/wp02-02/rma-tenant-archive/verify_same_source_file_content.py) · [实际参数与门禁](/D:/CP6-local-test/CP6-72H-20261109/W420-01/launch.py)

`native-01` 退出 0，CLR 21452 已退出，PostgreSQL 9444 在 19:09:32.717653 UTC 确认停止，距 START 13 分 25.5 秒，启动到停机证明 17.0 秒。PG/CLR 启动前分别为 1,237/1,176 MiB，运行最低 1,022 MiB，可用提交最低 20,295 MiB。原库和副本 `pg_ctl status` 均为 3，55567 无监听。原库控制文件与 HBA 摘要相同，副本原 HBA 摘要保留。后续可复用的停机副本和私有 LocalFileStore 保存在 W420 任务根。任务分支本地交接，DEV-2 由 COORD 采用和释放。[运行原件](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01) · [停机原件](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/stopped.json) · [私有资产根](/D:/CP6-local-test/CP6-72H-20261109/W420-01/native-01/private-assets)

RMA 原包和 A/B FileContent 历史各有独立切面。本窗建立同一物理库中的实际 Owner 数据。跨域同一 holder、ERP/Stock、候选开放和 PGT-021/022 仍需分别验收。[验收卡](../../acceptance/postgresql-tenancy.json)
