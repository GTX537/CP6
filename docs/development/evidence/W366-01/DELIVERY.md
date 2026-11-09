# W366-01 · RMA 原 journal 同快照归档

DEV-1 于 2026-11-09 09:40:07 UTC 开工，基线 9fe80b515fee21a84150f6224e5ec3cfb627b7de。实测使用专属环回 PostgreSQL 18.6。租户、RMA、共享出库和原操作载荷均为合成数据。[W361 已采用原 journal](../W361-01/DELIVERY.md) · [W363 v1 归档](../W363-01/DELIVERY.md)

## 归档边界

archive.py export 默认沿用 cp6-rma-source-archive-v1。显式加 --include-original-journal 生成 v2，在原 PostgreSQL 快照中增加 rma-original.csv。唯一选择条件是本租户 GUID 和 Main.Wms.RmaApplication.v1，保留原表全部十二列。原输入和结果为 PostgreSQL bytea 的十六进制表示，CLOSED 的输入和摘要仍为空值。原对象 ID、审计 jsonb 与时间均保留其数据库值。v2 清单列出原文件哈希、原操作状态计数和 W361 受保护标记记录的 catalog digest。[归档实现](../../../../eng/wp02-02/rma-tenant-archive/archive.py)

v2 在原物理库、安装和 Ready 边界上验证 W361 标记的 owner、表及列 ACL、commissioning 绑定与共享日志 ENABLE/FORCE RLS。计数和独立的 journal COPY 事务各自设置 row_security=off，权限不足的原生查询失败后不会发布完成 manifest。Windows 子进程的已知断管按清理处理，保留原失败诊断。包验证依格式核确切成员和字节摘要，可信来源由备份操作人核对。[PostgreSQL RLS](https://www.postgresql.org/docs/18/ddl-rowsecurity.html)

## 实库结果

最终证据 D:/CP6-local-test/CP6-72H-20261109/W366-01/rma-archive-20261109T095220-0d8f1824，父目录 test-native-04.log 退出 0。复用 W351 c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 的发布 DatabaseInit，PID 57204 退出 0，四套历史逐项匹配 224/1/1/1。W353 CLI 实际 prepare/commission，W361 CLI PID 12180 安装 overlay 退出 0。DLL 与依赖 SHA-256 见 inputs.json。[实库入口](../../../../eng/wp02-02/rma-tenant-archive/verify.py)

- 普通 A/B 实际调用 W361 store，以同一操作 GUID 分别提交不同原字节。A 还实际写入 CLOSED，并保有另一 Owner 的同键行。A/B 在 holder 49996 固定快照后各自又提交一条同键新操作，A 同时在模拟业务事务更新共享出库并写新 RMA。实际 pg_dump 10608、出库 COPY 41216 和原 journal COPY 26140 导入同一快照 00000002-0000000D-1。
- v2 快照保留 A 的 2 heads、4 versions、1 history、2 出库和 2 条原 journal（COMMITTED 与 CLOSED 各一）。A/B 后续操作及 B 和另一 Owner 的同键记录均排除。rma-original.csv 原件为 1,204 bytes，SHA-256 42411d5b03fa1b5737e2b2fbf75841aa60b51fedf96ba4ef586b0d246724d840。其余原件与哈希在 v2-cut/bundle/manifest.json。
- 隔离 w366_staging 以 pg_restore 和原 COPY 恢复本次自有包。共享片进入非活动表。实际逐值比对十二列、COMMITTED 的 UTF-8 原字节和摘要、CLOSED 的原结果及两个 NULL、原时间和审计值，并核旧出库状态、业务版本与 A/B 来源日志在 staging 前后相同。普通 A/B 读 staging journal 均返回 42501。
- 普通只读备份登录在默认 RLS 下实际查得零行。相同登录的 v2 导出在计数处原生返回 42501，CLI 返回 rma-original-journal-read-failed。向该登录实际授予标记列 UPDATE 后，导出拒绝标记 ACL，随即恢复夹具权限。直接写标记、缺 W361 标记、v2 缺 journal 文件均拒绝。失败导出均无完成 manifest。
- 正常 v2 CLI export 和 verify 退出 0，后续快照为三条 A journal。当前工具的默认 v1 实库入口及 W363 原 v1 包的实际 CLI verify 均通过。两版本清单均保留 recoveryReady=false。

保留 test-red.log 的旧 CLI 未识别开关。首个实库 rma-archive-20261109T094540-57948e54 保留原共享触发器拒绝 NUL 结果的 22021，随后夹具改用现行合法 UTF-8 JSON。第二个 rma-archive-20261109T094726-58c963d0 保留 staging COPY 夹具前缀缩进触发的 22P04。第三个 rma-archive-20261109T094934-e5398b2f 保留原生 RLS 42501 和 Windows 管道关闭遮蔽 CLI 诊断的失败。四个专属 PostgreSQL postmaster 60836、42704、60200、60112 均已停止，PID 文件移除且 55566 无监听。

## 复现与范围

沿用已发布宿主和已编译 CLI，无需重新构建。使用空闲端口与专属验证目录。

    $base = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify.py' --root "$base/W366-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli "$base/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --original-journal-cli "$base/W361-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --webapi "$base/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll" --webapi-source c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 --v1-bundle "$base/W363-01/rma-archive-20261109T092353-eb98873a/export-a/bundle"

本窗覆盖 W361 采用后的只读租户业务 ACL。W364 的写者 ACL 及其归档采用另行验证。IAM、其他 Owner、队列与路由水位、外部资产和生产切换仍属后续，PGT-021/022 保持 NOT_RUN。分支 codex/w366-01，DEV-1 的合并和官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
