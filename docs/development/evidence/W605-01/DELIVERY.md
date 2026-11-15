# W605-01 CAD 准备、解析和 Excel 预检的共同事务栅栏

2026-11-15 UTC。开发基线 `c2b2e8bb7f04171f98681efc9b54e84e81569f23`，受测源码 `f7ca7d65949f98b86f4cb842dc0bfebcf69a8708`。隔离 PostgreSQL 18.6、签名 Main Kestrel 和真实 multipart DXF/XLSX。文件扫描终态与 CAD 转换器为明确模拟，其余仍走产品 HTTP、持久化和权限路径。模拟转换器断言转换期间没有活动的 Space 事务。 [缺陷 #1121](https://github.com/GTX537/CP6/issues/1121) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

- CAD 准备预览、解析启动和 Excel 预检启动在同一业务连接核对 `space:model:edit` 与 `space:source:upload`，在提交前确认当前会话。CAD 取消和人工重试按本来的编辑权运行，夹具单独移除上传权。准备阶段的已完成扫描回填独立获取同一栅栏，转换后重读 Draft、来源、文件与楼层才保存准备结果。
- 原版 `14/14` 真实复现，包含修正 CAD 模拟 IR 计数后的 12 项及两项原 Excel 复现。最终 **14/14** PostgreSQL 场景通过，零跳过。五条业务入口均见证两种提交顺序，额外覆盖 Clean 与 Rejected 扫描回填。暂停先提交后不增加业务写集。Clean 回填先提交、随后暂停时保留原 Source Ready，拒绝新增准备结果。合法 Rejected 回填与原 409 实测保留。
- CAD 解析启动和重试、Excel 预检启动按原请求键读回 Job，重复取消保留同一原 Job。实际回读来源、准备、任务、重试关系、幂等记录、版本和物理内容。
- 104 项权限、控制器及 OpenAPI 契约通过。相关 Space 集成测试 9 项通过，在本地构件中排除一个原有接口实现不全的无关测试文件；完整集成项目的原 CS0535 结果保留。发布 310 个文件 SHA-256，7 个程序集与受测输入一致。实际存活 200、匿名请求 401。原始证据在 `D:/CP6-test-artifacts/W605-01`，发布见 `publish-verified-smoke.json`。Actions 启动、重跑、取消和生产部署为零。

本轮覆盖创建准备结果和处理命令的前台提交。实际扫描、CAD/Excel 后台内容、匹配、确认和刊发执行待继续验证。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
