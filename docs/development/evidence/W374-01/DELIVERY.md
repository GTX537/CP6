# W374-01 · RMA 原出库来源同快照归档

DEV-1 于 2026-11-09 10:21:52 UTC 开工，登记基线 c1903e4e8ae3fa35d8a9f3dc24bfdf8021311012。共享归档入口在 W372 经 #822 采用并释放后，于 e96dbe7fd484418925e014a396d30fb11ff7204b 上串行接入。[W372](../W372-01/DELIVERY.md) · [S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

## v3 归档

archive.py export 显式同时使用 --include-original-journal 和 --include-rma-source 生成 cp6-rma-source-archive-v3。沿用 v2 的原 journal 并扩展 outbounds.csv，增加 CustomerCd、WebOrderNo 和 OutboundType。新 rma-source-details.csv 保存同租户已引用出库的有效明细，包括原 ID、单号、行号、产品、原 numeric(21,8) 发货数量、单位、仓库、位置、批次和出库流水。rma-source-lineage.csv 保存有效明细实际匹配的原 JSONB lineage 行。[来源投影](../../../../eng/wp02-02/rma-tenant-archive/source_v3.py) · [原 Source](../../../../CP6.Core/Services/Wms/RmaApplicationOwnerV1.cs#L65) · [原 lineage](../../../../CP6.Core/Services/Wms/OutboundOrderLineageV1.cs#L40)

从租户 heads 用 TenantId 与出库 GUID 定位头，明细联接沿用原 BPCHAR 单号比较。lineage 用 TenantId、明细 ID 和原头单号的精确文本匹配。COPY 保留 BPCHAR 原空格、原数量与空值，手工出库可以缺 lineage。三个来源 COPY、原 journal COPY 和 tenant schema pg_dump 均导入同一 PostgreSQL 快照。v3 清单明确来源范围、行数、成员与原文件摘要。默认 v1 和原 journal v2 各沿用既有成员与布局。[归档 CLI](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [PostgreSQL 字符类型](https://www.postgresql.org/docs/18/datatype-character.html)

## 实库

最终证据 D:/CP6-local-test/CP6-72H-20261109/W374-01/rma-source-20261109T103956-600b02ab，test-native-01.log 退出 0。专属 PostgreSQL 18.6。W351 c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 发布 DatabaseInit PID 50416 退出 0，四套历史为 224/1/1/1。复用 W353 commissioning 和 W361 原 journal CLI，后者 PID 17888 实际退出 0。依赖摘要在 inputs.json。使用原生合成业务和普通 A/B journal，W364 已实际验证的正常 Source GET 和 Hosted Create 作为原入口证据。[W364 实际入口](../W364-01/DELIVERY.md)

- A/B 使用同出库单号、同 RMA 申请和 operation GUID。A 的头单号有一个尾空格，选中明细有两个。实测 BPCHAR 相等而原文本不等。原匹配 lineage 保留；仅去掉头单号尾空格的 lineage、软删除明细及其 lineage、A 未引用行和 B 同单号行均排除。另一有效明细缺 lineage 时仍成功。精确数量为 1234567890123.12345678。
- holder 36116 固定快照后，独立 A/B 事务 30216/43736 实际提交，修改客户、订单引用和数量并追加 lineage。之后 pg_dump 36556，头 COPY 9256，明细 COPY 1376，lineage COPY 35852 和原 journal COPY 22676 均导入 00000000-00000007-1。v3-cut 保留较早的 1 头、3 有效明细、1 lineage 和 1 COMMITTED。原件和五份 SHA-256 在 v3-cut/bundle/manifest.json。
- 实际正常 CLI export/verify 接受新切面和第二个 lineage。实际 CLI 拒绝省略原 journal opt-in 和缺失 lineage 成员。W363 原 v1 与 W366 原 v2 均由当前 CLI 验证通过。
- 实现前 test-red.log 保留来源模块缺失。唯一任务 postmaster 53600 已停止，原生 status 为 3，55566 无监听。

## 复现与边界

沿用已发布宿主和已编译 CLI，无需重新构建。使用尚未占用的专属目录和环回端口。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_source_archive.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_source_archive.py' --root "$b/W374-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli "$b/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --journal-cli "$b/W361-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --webapi "$b/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll" --webapi-source c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 --v1-bundle "$b/W363-01/rma-archive-20261109T092353-eb98873a/export-a/bundle" --v2-bundle "$b/W366-01/rma-archive-20261109T095220-0d8f1824/v2-cut/bundle"

v3 提供当前快照的原来源事实。当前 Orders/WorkOrders、仓库可见性图、IAM 与授权、队列、路由、外部资产及生产切换仍在后续。v3 staging 另窗采用，正式 PGT-021/022 仍为 NOT_RUN。分支 codex/w374-01，合并和 DEV-1 官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
