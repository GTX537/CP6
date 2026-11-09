# W387-01 · RMA 历史 IAM 引用同快照归档

DEV-2 于 2026-11-09 11:50:20 UTC 从 #829 `90ae10cb495322cdbf9f2fa9d18a9deba1733371` 开工。沿用 W379 出库可见性、W380 v5 Identity Priority 切面和 W375 已验证的现行 RMA Owner。[W379](../W379-01/DELIVERY.md) · [W380](../W380-01/DELIVERY.md) · [W375 实际宿主权限](../W375-01/DELIVERY.md)

`archive.py export` 在原 v5 四个开关外加 `--include-rma-iam-references`，生成 `cp6-rma-source-archive-v6`。仅增加流式 `rma-iam-references.csv`，每行用 `kind,tenant_id,id,facts` 记录所选租户、原 UUID 或数值键以及 JSONB 事实。计数、COPY 与原 tenant schema、journal、可见性及 Identity Priority 共用同一只读快照，清单固定字段和成员摘要。[归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [v6 投影](../../../../eng/wp02-02/rma-tenant-archive/iam_v6.py)

原 heads 和已验证的 W361 tenant/Owner/contract journal 取参与者并记录各来源，CLOSED 专有 actor 和缺失用户有明确记录。可用行保留原用户名以对应 Creator、启用、锁止时间、部门、主角色、附加角色及用户与角色修订。租户保留启用、到期时间、认证策略修订和原 Owner 使用的部门版本引用。`wms-rma` 原 `query/add/submit/cancel` 的租户 action、角色 action、原 scope 与所有原字段行分别保存。全局菜单仅保存 MenuId 和原 MenuKey 引用。WMS scope 按原角色及可见性切面的实际仓库匹配或 `*` 取行。Order 部门映射按所选原 Order 的 BPCHAR 键关联。原同租户部门树只保存 ID、父边、原路径及启用标志，`CustomDeptIds` 按原文本保存。[权限读取](../../../../CP6.Core/Services/Sys/PostgreSqlPermissionReader.cs) · [原字段事实](../../../../CP6.Core/Services/Sys/CurrentFieldPermissionSourceReaderV1.cs) · [原出库可见性](../../../../CP6.Core/Services/Integration/OperationsAccessV1.cs#L102)

## 实库验证

最终执行 `test-native-01.log` 退出 0，原件在 `D:/CP6-local-test/CP6-72H-20261109/W387-01/rma-iam-20261109T115615-5754e16b`。专属 PostgreSQL 18.6 环回端口 55567。W351 `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 发布 DatabaseInit PID 46852 退出 0，四套实际迁移历史 224/1/1/1。W353 commissioning 和 W361 原 journal CLI 复用，W361 安装 PID 59252 退出 0。二进制摘要在 `inputs.json`，无产品重建。

- A/B 使用实际合成 Core、原生 schema 和 W361 journal，同号角色 72 保持各自授权事实和修订。A 的 journal-only CLOSED actor 缺少用户行，仍保留原 actor UUID。另一 Owner 的 actor、无关资源、无关角色和无关仓库排除。附加禁用角色、未知字段、显式访问值 1、原用户名与 CustomDeptIds 尾空格、部门路径及 NULL/空原值均通过实际 CSV 核对。两份合成密码与 MFA 哨兵均未进入 IAM 成员。
- holder 59864 固定快照 `00000000-00000008-1` 后，独立 writer 60672、xid 1268 提交 A 的字段访问值 1→3 和租户到期时间变化。原 PostgreSQL 触发器推进角色及租户策略修订，B 的原修订保持。A 包保存变更前的值和修订。pg_dump PID 20792 与 IAM COPY PID 46536 使用相同快照，其他继承成员同切面。A 有 17 条 IAM 引用，清单 SHA-256 `280ccebb949df15bea0b5182a441e470681afab458d63cde759e81d15a88e641`，IAM 成员 SHA-256 `d3fb9815b15226ca7a1b24e6590387b88cd8b98044edf13d89ebb5bbb559655c`。
- B 正常 v6 CLI 导出及校验通过，清单 SHA-256 `0adf47cc128ce1a2f3381d9a5b8a5ec15dcb6c00c90a5fa0a346f8264763307c`。B 同源的 v5/v6 九份旧 CSV 字节完全相同，已保存的 v1–v5 原包均由当前 CLI 实际校验。缺失 v5 开关和缺失 IAM 成员实际拒绝。实现前旧 CLI 未识别 v6 开关，原 `test-red.log` 保留。
- #830 对 v5 Identity Priority helper 提取复用后，原 layout/count、COPY 生成 SQL、manifest 和 members 逐项相同，静态对照留在 `test-w384-compatibility.log`。复用 W375 已运行的当前宿主授权结果。任务 postmaster 47692 已停止，`pg_ctl status` 为 3，55567 无监听。

## 复现和范围

使用空闲环回端口、任务私有目录和已验证二进制。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_iam_archive.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_iam_archive.py' --root "$b/W387-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55567 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli "$b/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --journal-cli "$b/W361-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --webapi "$b/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll" --webapi-source c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 --v1-bundle "$b/W363-01/rma-archive-20261109T092353-eb98873a/export-a/bundle" --v2-bundle "$b/W366-01/rma-archive-20261109T095220-0d8f1824/v2-cut/bundle" --v3-bundle "$b/W374-01/rma-source-20261109T103956-600b02ab/v3-cut/bundle" --v4-bundle "$b/W379-01/rma-visibility-20261109T111625-8db32a6f/v4-cut/bundle" --v5-bundle "$b/W380-01/identity-priority-20261109T111628-589aa6fd/v5-cut/bundle"

本窗交付历史原引用。当前授权仍由原宿主决定，`recoveryReady` 为 false。凭据、MFA、令牌、签名密钥、全局 IAM 定义和正式恢复启用分窗。分支 `codex/w387-01` 本地保存，DEV-2 工作树干净，远端采用和席位释放由 COORD 执行。[正式验收卡](../../acceptance/postgresql-tenancy.json)
