# W598-01 Space 底图写入的共同事务栅栏

2026-11-15 UTC。开发基线 `922fb9ff71d66c1c69692e0234e4106564998dce`，实际受测 `f71bfa721c4626dee962655eaae79207a1363a73`。隔离 PostgreSQL 18.6、实际 Kestrel、模拟租户、楼层、PDF、文件目录和命令历史。 [缺陷 #1109](https://github.com/GTX537/CP6/issues/1109) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

| 实际入口后缀 | 权限和写集 |
| --- | --- |
| `POST versions/{v}/underlay-sources` | `space:model:edit` 与 `space:source:upload`。文件暂存和类型检查完成后，文件、扫描任务和物理内容在文件登记提交中受栅栏控制。来源与版本代次在第二次受控提交持久化。 |
| `POST versions/{v}/sources/{s}/underlay-calibration` | 同样核对两项权限。原楼层、版本、标定、命令记录与回执在同一个原生事务。 |
| `PUT versions/{v}/floors/{f}/underlay` | 同样核对两项权限。挂接、楼层和版本代次、批次和回执在原生事务保存。 |
| `POST versions/{v}/floors/{f}/underlay:compensate` | 使用 `space:model:edit`。按原历史、当前租约及代次保存补偿。 |

- 每条 HTTP 写入口都验证业务或暂停先提交。另独立撤销前三项的上传权，分别返回 403。补偿只需模型编辑权。暂停先提交返回 401，楼层、来源、文件、租约、命令与回执的业务写集保持原值。原生阻塞进程及实体写集随测试留存。
- 上传在持栅栏前完成暂存、哈希和格式验证。原文件与排队扫描任务先提交、暂停截断来源时，同字节恢复复用 File、Job 和物理内容，登记来源一次。确认在进入 COMMIT 前失败可清理本次文件。已进入原生 COMMIT 后保留内容，实测 PostgreSQL 已提交而应答路径抛错时仍可沿原文件恢复。
- 基线 12 个 HTTP 场景均复现。第一轮候选有一个已消费响应的夹具断言失败。改正后定向通过。最终 8 个无关上传变化的 HTTP 结果复用，加 6 个最终运行的受影响场景，共 **12 个实际 HTTP 与 2 个原生提交故障场景通过，零跳过**。相关单元 **30/30**，权限和 OpenAPI **98/98**。保留首次契约静态资产路径错误及补齐完全相同资产后的结果。
- 本地发布 310 个文件 SHA-256，五个受测程序集与发布对应。隔离宿主存活 200，匿名 multipart 上传 401。集成时四份受测源码逐字节一致。证据在 `D:/CP6-test-artifacts/W598-01`。Actions 运行零次，生产部署零次。

| 业务入口 | 当前证据 |
| --- | --- |
| 版本创建四种模式，含原任务、模板内部租约与回执 | [W596](../W596-01/DELIVERY.md) |
| 楼层、元素和布局指令及资产 | [W593](../W593-01/DELIVERY.md) |
| 已扫描来源挂接和移除，以及公开编辑租约取得、续期、释放和接管 | [W594](../W594-01/DELIVERY.md) |
| 底图上传、标定、挂接和补偿 | 本轮 |
| CAD 上传、准备预览、解析、应用选择、取消、重试。Excel 上传、预检、Excel/CAD 匹配、确认、补偿 | 各自文件与业务写入仍需核对。CAD 准备预览有准备行持久化。 [CAD](../../../../CP6.WebApi/Controllers/Space/SpaceCadParseController.cs) · [Excel](../../../../CP6.WebApi/Controllers/Space/SpaceExcelPreflightController.cs) · [匹配](../../../../CP6.WebApi/Controllers/Space/SpaceExcelCadMatchController.cs) |
| 刊发启动、重试、历史版本刊发、验证与规划 | 独立业务事务待核对。 [刊发](../../../../CP6.WebApi/Controllers/Space/SpacePublishController.cs) · [验证](../../../../CP6.WebApi/Controllers/Space/SpaceValidationController.cs) |
| WMS 采纳刷新、单笔/批量绑定与布位、租户模板登记、编码或映射配置 | 待核对独立写者。 [Design 入口](../../../../CP6.WebApi/Controllers/Space/SpaceDesignV1Controller.cs) · [编码](../../../../CP6.WebApi/Controllers/Space/SpaceDesignCodingController.cs) |

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。本轮证明底图文件登记、来源写和已排队扫描任务；实际扫描执行、CAD/Excel 文件写者、其余财务及 CRM 产品级切换分别待核对。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
