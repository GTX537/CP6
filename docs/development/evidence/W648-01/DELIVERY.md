# W648-01 Space 上传来源登记与扫描终态

关联 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)。固定 Main `1e2b294eb2f4f5a72cf903a16bac724edc74694d`，源码和测试提交 `b7edb35bf5b0dba503831e7845832ba8c64855b6`。集中审查只包含三个原上传服务、原共用事务代码和一份原生测试，共五个文件。

## PostgreSQL 事务与回执

`POST /api/space/design/v1/versions/{versionId}/cad-sources`、`excel-sources` 与 `underlay-sources` 保留原分段登记。文件、已排队扫描 Job 和保留的原字节先独立提交。流读取与格式判断保持在 Main 根之外。来源登记取得原 `space:model:edit` 与 `space:source:upload` 的 Main 权限根后，复读 Draft，按 File → Source 顺序锁住原引用。扫描已经完成时，Source 的 Clean/Rejected 对应终态和新增来源的版本代次在同一次原生事务、同一次 SaveChanges 保存。扫描在来源登记后完成时，W616 仍拥有已存在 Source 的同步；API 的 PostgreSQL 响应阶段只读。

同字节的原可复用 File、Job、内容和来源身份继续保留。原 Source 已是 Rejected 时，仍返回原 409。旧 Scanning Source 引用的原 File 已 Rejected 时，登记使用此原 File 决定原 Source 状态，回执的 File、Job 和地址也对应原引用。由原文件阶段合法登记的同散列新 Quarantined File 及独立 Job 仍归原保留机制。Source COMMIT 后其他已授权请求软删同一来源时，原上传返回已提交的原 Source 快照，与存续的原文件和扫描 Job 一起返回。响应查询保持租户与原 ID 约束。其他提供方保留原上传完成路径，本次只验 PostgreSQL。

## 实际验证

隔离 PostgreSQL 18.6、真实签名 Main Kestrel、持原 Job 租约的 W616 隔离扫描器、实际私有存储和 DXF、XLSX、PDF 模拟输入。恶意内容适配器为确定性模拟，实际扫描处理读取及核对原文件字节和 SHA-256。使用 W641 实际 PostgreSQL relay 持住已经完成的原 File COMMIT 应答，此时独立连接读到原 File、Job 和 blob，Source 数为零。扫描完成后释放应答，再使用真实数据库阻塞关系与签名暂停排序。

- 固定基线六项预期失败。三路的 Clean 和 Rejected 原生终态都出现在版本登记之外的第三笔写入。Clean 的例子是 CAD 版本/Source `1117/1122`、Excel `1031/1036`、底图 `1074/1079`，HTTP 都是 202。 [基线 TRX](D:/CP6-test-artifacts/W648-01/baseline-1/results.trx) · [基线源码与程序集](D:/CP6-test-artifacts/W648-01/baseline-1/source-and-assemblies.json)
- 最终三路 Clean 分别实测两种顺序。暂停先提交时 HTTP 401，Source 未登记，版本及之前完成的 File、Job 和 blob 保留。来源先提交时 HTTP 202，版本与 Source 同一 native `xmin`，以当前 Clean 状态登记 Ready。重新启用后的同字节请求复用原 File、Job 和原存储字节。来源已提交时，Source ID 和 native `xmin` 同样保持。
- 三路另实测 Rejected 先于来源登记。首次原授权请求 202 并返回 Rejected，版本与 Source 同一 `xmin`。后续同字节 409，原来源、版本和原文件扫描史保留。旧 Scanning Source 对照使用一个已拒绝原文件和一个同散列新 Quarantined 文件，实际 202 的原 File/Job 回执与原 Source 引用一致，辅助文件与 Job 分别保留。
- 并发删除对照持住实际 Source COMMIT 应答。W616 扫描完成后，另一个签名移除请求以当前行版本合法软删，原上传遂返回 202 和原已提交 Source/File/Job。删除及版本的 native `xmin` 未被原回执阶段改写，文件字节保留。 [最终 11 项原生 TRX](D:/CP6-test-artifacts/W648-01/native-final/results.trx) · [旧 FileId 对照](D:/CP6-test-artifacts/W648-01/native-final/permission-consumption-native-final/space-upload-scan-legacy-file-reference-recovered.json) · [并发删除对照](D:/CP6-test-artifacts/W648-01/native-final/permission-consumption-native-final/space-upload-scan-concurrent-removal.json)
- 相关回归实际通过 W602 八项和 W598 两项原生文件提交边界，W616 六项开始/完成顺序及应答恢复，以及 CAD/Excel 和权限 46 项契约。最终结果均零跳过。初版十项亦通过，最终版本按回执和并发删除的审查结果复验。 [文件与上传回归](D:/CP6-test-artifacts/W648-01/catalog-regression/results.trx) · [扫描回归](D:/CP6-test-artifacts/W648-01/scan-regression/results.trx) · [契约](D:/CP6-test-artifacts/W648-01/contracts/results.trx)

## 本地发布与交接

Debug PostgreSQL 发布包含 310 文件 SHA-256 清单，五个源码文件和七份发布 DLL 与受测输入字节一致。清单 SHA-256 为 `fe9d628ac60adec4cd4612b73f9dcd0b7f461c85fcce4ae327017b6d43a67f92`。实际 PostgreSQL 57200 和 Kestrel 57209 启动，`/health/live` 返回 200，三种实际 multipart 匿名上传均为 401。所有任务进程已停止，57200–57209 无监听。 [源码、程序集及冒烟](D:/CP6-test-artifacts/W648-01/publish-verified-smoke.json) · [逐文件清单](D:/CP6-test-artifacts/W648-01/publish-sha256.json)

`codex/w648-01` 和可审计提交留在本地，DEV-2 工作树干净，席位待协调方接手集成后释放。本次无远程操作、Actions 或部署。
