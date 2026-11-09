# W379-01 · RMA 来源可见性同快照归档

DEV-2 于 2026-11-09 11:00:33 UTC 从 #824 `6698a4344965f012af8426d47d3dab890fef8ba5` 开工。W377 采用后快进到 #825 `90645bc17618748e7fc86d30f270551d02168080`，两窗的代码路径独立。[W374 原来源归档](../W374-01/DELIVERY.md) · [W377 v3 staging](../W377-01/DELIVERY.md)

## 归档格式

`archive.py export` 同时传入 `--include-original-journal --include-rma-source --include-rma-visibility` 时生成 `cp6-rma-source-archive-v4`。沿用 v3 成员，增加一份流式 `rma-source-visibility.csv`。各行以 `kind,tenant_id,id,facts` 标识原表、租户、物理行 ID 和 JSON 事实。清单固定成员、字段、逐类计数和 SHA-256。v1 至 v3 沿用各自成员和格式。[归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [v4 投影](../../../../eng/wp02-02/rma-tenant-archive/visibility_v4.py)

从选定租户的原申请 heads 按出库 GUID 定位头，保留头和所有同单明细的原创建者及软删状态。Order 关联采用直接非空订单号，或在直接键 NULL 或空时跟随有效工单的订单号。原工单过滤允许空键参与回退，订单保留原部门码及软删状态。明细仓库 NULL 时才回退头仓库。库位按有效仓库和原库位码匹配，保留原区域、软删和 PostgreSQL 模型使用的初始化隔离状态。所有键关联沿用数据库原生 BPCHAR 比较，输出保留原大小写、尾空格、空字符串和 NULL。[原出库可见性](../../../../CP6.Core/Services/Integration/OperationsAccessV1.cs#L102) · [原订单部门查询](../../../../CP6.Core/Services/Erp/OrderDepartmentScopeV1.cs#L20) · [PostgreSQL Location 全局过滤](../../../../CP6.Core/EFDbContext/CP6Context.cs#L2620)

## 实库验证

最终执行 `test-native-03.log` 退出 0。实际原件在 `D:/CP6-local-test/CP6-72H-20261109/W379-01/rma-visibility-20261109T111625-8db32a6f`。使用独占 PostgreSQL 18.6 的 55567、W351 `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 发布 DatabaseInit 和已有 W353、W361 CLI，无产品重建。DatabaseInit PID 25716 退出 0，四套实际迁移历史为 224/1/1/1。W361 原 journal 安装 PID 18456 退出 0，所用二进制摘要在 `inputs.json`。

- A/B 合成数据使用同原出库单号、同申请和 operation GUID，各自原 journal 字节和实际事实分离。A 含 3 头、4 明细、3 订单、2 工单、4 库位，v3 部分仍只有 3 条有效明细。B 每类 1 行。A 原生 self 谓词因一条软删明细的 NULL 创建者排除该头。空仓码匹配空仓库位，未串入同码的 W1 库位。原生直接订单、空键工单回退、B 独有工单排除、非关联订单排除以及原尾空格均按实际 CSV 核对。
- 为了覆盖边界，夹具在本地种子事务内合成一条被遗留明细引用的隔离库位，只暂时关闭原 `location_draft_native` 后立即恢复。实测该触发器前后都是 OID 31778、模式 O、函数 OID 31777，定义一致。导出时原触发器生效，隔离库位仍按隔离状态记录。
- holder 30340 固定快照 `0000000C-00000005-1` 后，独立写事务 16460 提交，修正软删明细创建者并改变库位区域。当前源 native self 结果与区域随之改变，A 包保留此前的事实。pg_dump PID 39092、三份原来源 COPY、原 journal COPY 和 v4 COPY PID 52932 均导入该快照。A 清单 SHA-256 为 `18133a9eb354b4491f43fe69691a3d68f1ea331f2bc35668e2f5b9f313cd205d`，B 为 `4b3613e2b712f5ad30d0f9d524d0969d0fc2ba276e6378b0c0b0736eb91005eb`。
- B 正常 CLI v4 export/verify 成功。B 的独立 v3 和 v4 导出中，原出库、有效明细、lineage、原 journal 四份 CSV 的字节摘要完全相同。W363 v1、W366 v2 和 W374 v3 既有包均由新 CLI 校验通过。缺少原来源开关、修改可见性成员的调用实际退出 1。
- 原实现的未知开关 RED、首轮合成两条 live 同租同码库位导致的实际 SQLSTATE 23505、第二轮过宽的全域 Location readiness 断言均已保留。第三轮用合法的 live/隔离部分索引组合和精确触发器前后校验。各轮 PG 均停止，最终 postmaster 50324 已停，`pg_ctl status` 为 3，55567 无监听。

## 复现和范围

使用独占任务目录与环回端口。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_visibility_archive.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_visibility_archive.py' --root "$b/W379-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55567 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli "$b/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --journal-cli "$b/W361-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --webapi "$b/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll" --webapi-source c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 --v1-bundle "$b/W363-01/rma-archive-20261109T092353-eb98873a/export-a/bundle" --v2-bundle "$b/W366-01/rma-archive-20261109T095220-0d8f1824/v2-cut/bundle" --v3-bundle "$b/W374-01/rma-source-20261109T103956-600b02ab/v3-cut/bundle"

本窗交付 v4 导出和校验。历史 IAM、部门映射、当前授权、队列和外部资产仍分窗，v4 staging 和正式 PGT-021/022 恢复验收另行执行。原包 `recoveryReady` 继续为 false。`codex/w379-01` 本地提交，DEV-2 工作树干净并保留候选，远端采用和官方席位释放由 COORD 执行。[正式验收卡](../../acceptance/postgresql-tenancy.json)
