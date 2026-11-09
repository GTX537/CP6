# W382-01 · RMA v4 可见性原子 staging

DEV-2 于 2026-11-09 11:23:44 UTC 从 #826 `aaccdd610c2772efb273a1051a213f7702bcfa87` 开工。沿用 W377 的隔离目标、可信 manifest pin、原 schema、ACL、journal 和来源 staging。[W377](../W377-01/DELIVERY.md) · [W379](../W379-01/DELIVERY.md)

`stage.py` 将 v4 的 `rma-source-visibility.csv` 以 PostgreSQL `COPY HEADER MATCH` 装载到中性 `rma_archive_stage.visibility_snapshot`。行使用闭集 kind、受限租户 UUID、原行 UUID 和原 JSONB facts。原 tenant schema、journal、来源、可见性及回执使用同一事务。提交前核对五类数量、头和有效明细与已有来源的双向对应及重叠原字段。所有明细含已软删明细按原租户、出库 ID 和 native ICU BPCHAR 单号关联到头。重叠字段按原 BPCHAR 字节比较，JSON NULL、空串、大小写、尾空格及库位隔离状态分别保存。仅 v4 `STAGED` 回执增加 `visibilityCounts`，现有 `recover` 返回原字节。[实现](../../../../eng/wp02-02/rma-tenant-archive/stage.py)

## 实库验证

唯一 PostgreSQL 18.6 实例使用专属环回端口 55567。`test-native-01.log` 退出 0，原件在 `D:/CP6-local-test/CP6-72H-20261109/W382-01/rma-stage-v4-20261109T112716-861cf75b`。输入为 W379 A 的真实合成业务包 `v4-cut`，原 manifest SHA-256 `18133a9eb354b4491f43fe69691a3d68f1ea331f2bc35668e2f5b9f313cd205d`。使用原生 PG，复用原包与 W377 fixture 基础，无 .NET 运行。

- 实现前旧 CLI 对该固定输入在连接前退出 1，错误为 `rma-stage-bundle-member-rejected`，保留 `test-red.log`。
- 夹具保留输入原件及 pin，在原 native driver 的所有 COPY 完成后仅将一条已加载有效明细的仓库从 JSON NULL 改为空串。backend 55368、xid 757 以 P0001 `rma-stage-visibility-graph-mismatch` 拒绝并回滚。隔离目标无残余 schema，backend 8164 的只读查询无回执。
- 同一物理库、租户、pin 经正常 CLI 重试，backend 57120、xid 758 提交。夹具丢弃 715 字节 stdout，backend 42580 只读返回持久原回执。回执为 `STAGED` 且 `recoveryReady=false`，可见性数量依次为 3 头、4 明细、3 订单、2 工单、4 库位。带换行的原恢复输出 SHA-256 为 `a747d88605d6ead2d823642e245c430b555518e049e277e67f7e7f0a3dd47657`。
- native COPY 回出的可见性成员与原件逐字节相等，SHA-256 为 `83dab5f0433af32bf2e0598507c41b3baa1c6fb264d23407930f18e0965011cd`。原生查询验证 1 个 NULL 仓库、2 个空仓库、1 个创建者 NULL 的软删明细、1 个区域 NULL 的隔离库位和 1 个区域为空串的普通库位。原 journal 字节、tenant 原 ACL 和非活动目标边界均通过。
- 实物 v1、v2、v3 包用同一输入生成 stage/recover driver，六份 SQL 各自逐字节等于 #826 的生成结果，原回执写入和恢复 SQL 一并覆盖。复用 W377 的实库兼容证据，静态比较留在 `test-legacy-sql.log`。最终 postmaster 56160 已停止，`pg_ctl status` 为 3，55567 无监听。

## 复现和范围

使用 W379 已导出原包、未占用的环回端口和任务专属证据目录。[实际夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_visibility_stage.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_visibility_stage.py' --root "$b/W382-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55567 --bundle "$b/W379-01/rma-visibility-20261109T111625-8db32a6f/v4-cut/bundle" --manifest-sha256 18133a9eb354b4491f43fe69691a3d68f1ea331f2bc35668e2f5b9f313cd205d

本窗交付受控 v4 staging 和原回执恢复。IAM 和当前授权、路由、队列、外部资产、活动切换及正式 PGT-021/022 恢复验收分窗。分支 `codex/w382-01` 已本地保存，DEV-2 工作树干净，远端采用和席位释放由 COORD 执行。[正式验收卡](../../acceptance/postgresql-tenancy.json)
