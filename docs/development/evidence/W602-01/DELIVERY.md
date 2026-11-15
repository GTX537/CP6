# W602-01 CAD 与 Excel 上传的共同事务栅栏

2026-11-15 UTC。Main 开发基线 `84dabbe3881b5cf75a66ba62b77a4365c5b3a467`，受测源码 `a144feffe66079826a5e8ddacef11409904100bd`。隔离 PostgreSQL 18.6 与实际 Main Kestrel，模拟租户和 Draft 版本，真实 multipart DXF 与 XLSX。 [缺陷 #1115](https://github.com/GTX537/CP6/issues/1115) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

`POST versions/{versionId}/cad-sources` 和 `POST versions/{versionId}/excel-sources` 在提交时核对 `space:model:edit` 与 `space:source:upload`。三类文件上传共用底图已有的登记事务。暂存、类型判断和哈希完成后登记 File、物理内容以及排队的 FileScan。来源单独在原生事务获取共同栅栏，再按当前 Draft 和输入格式保存 Source 及版本代次。

- 两条上传均以原生阻塞进程见证业务先提交、暂停先提交及当前会话仅失去上传权。失权先提交分别返回 401 或 403，业务与物理写集无新增。业务先提交保留原文件、任务和来源。同字节恢复维持原结果和任务地址。
- 文件与任务先提交、暂停后才请求来源登记时，来源停在原写集。恢复原权限后复用已提交的文件、任务和物理内容，唯一登记来源。回读核对 SHA-256、真实内容、临时文件和 Draft 代次。
- 初轮基线 6 个顺序场景复现。两个中断场景修正夹具查询后用同源 W600 已验发行宿主重跑，两者均实际复现。首次候选的夹具读取和任务地址断言留在原日志。最终 **8/8 PostgreSQL HTTP 场景通过**，同一生产输入复用 **2/2 已提交应答丢失及提交前拒绝**证据，并通过 104 项相关接口契约和 25 项文件单元。全部零跳过。
- 本地发布包含 310 个文件 SHA-256，七份受测程序集与产物一致。隔离宿主存活 200，两条匿名 multipart 写入各返回 401。 主线集成核对五份受测文件一致。原记录在 `D:/CP6-test-artifacts/W602-01`。Actions 与生产部署均为零。

本轮验收文件登记、来源写入、排队的扫描任务和旧键恢复。实际扫描、CAD 准备/解析/重试和 Excel 预检/匹配、刊发及其他业务写者继续核对。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [底图原生提交证据](../W598-01/DELIVERY.md) · [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
