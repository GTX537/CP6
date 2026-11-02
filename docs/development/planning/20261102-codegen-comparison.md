# W261 CodeGen差异与覆盖范围预览执行计划

COORD按executing-plans单人执行；沿用固定席位、PostgreSQL基线及一次任务级集中审查。上期三天计划已经按wave260-final-verification.json完成，本窗口从原WP02-16真实剩余范围继续。

目标：在现有固定模板纯预览上，让用户提供原文件文本，看到保留自定义区后的差异、固定建议目标路径和逐文件覆盖资格。所有结果仍未应用，不读取服务器任意路径、不执行代码、不隐式保存元数据或改变号码。

## 实现步骤

- [x] 核对当前固定模板、原卡与直接前置，官方start登记DEV-1/W261-01及DEV-2/W261-HANDOFF。从Main457e4a6和Handoff a18fdb4开始，旧三天计划核销归档。
- [x] CP6.Core/Services/Pub/CodeGenComparison.cs定义纯比较合同及算法；复用GeneratePreview的模板版本/输入校验。固定五种产物的建议仓库相对路径；请求只接受精确匹配的原文件路径和显式覆盖白名单，未知/别名/重复路径立即失败。原文本为空仍表示已有文件，缺文件表示未提供基线，不能宣称磁盘不存在。
- [x] 自定义块按完整标记行匹配，原始块文本无损保留；不平衡、嵌套或无法对应时明确阻止该文件替换。保留区外变化以线性单区间差异表示，原行/新行含实际换行符，保证可重建，不截断、不用平方级diff。覆盖未获准时显示候选差异但不可替换。
- [x] CP6.WebApi/Controllers/Pub/CodeGenController.cs新增POST comparison/v1；沿用Authorize、pub-codegen:view及1MiB传输限制。只转换预览合同异常，旧preview/v1及字典接口保持原形状。CP6.Tests/CodeGenComparisonTests.cs和CodeGenPreviewHttpTests.cs覆盖保留代码、无基线/空文件/未授权/显式覆盖、非法路径/版本、换行及损坏标记、HTTP权限及无保存。
- [x] cp6.web/src/api/pub/codegen.ts及views/pms/CodeGenView.vue接入：先生成固定文件清单，再由用户选择原文件或粘贴原文本，逐文件勾选允许覆盖，显示旧/新差异及阻止原因。输入或基线改变即清除结果；晚到响应丢弃；文本插值不执行。文件内容只在用户请求比较时发送，实际磁盘状态未经确认。
- [ ] 运行受影响CodeGen测试、前端type-check和实际页面行为检查；明确页面夹具与真实API验证范围。单次集中SPEC/QUALITY审查，唯一交付报告，同步台账/生成进度页/四份状态文件。正常PR合并、远端采用核对、准确删除本任务分支、官方finish释放席位。

验证命令：dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~CodeGen（本机沿用隔离artifacts与已还原包）；前端npm run type-check；node docs/development/evidence/W261-01/check-page.mjs。没有数据库写入或迁移，相关纯计算/HTTP夹具无需启动PG；不将其称为真实租户持久化验收。

原375状态和原59局部余56不变。模板已有版本并非新缺口；本轮只新增明确版本选择校验、diff及建议路径/覆盖白名单的纯预览合同。实际文件应用、元数据CAS/unknown、真实业务编号消费者和完整WP02-16仍待对应窗口；CRM过期事件消费者也仍未交付。

实现/42项测试/类型检查/实际页面验证及集中审查完成，正常合并和席位收尾由wave261-final-verification.json核销；提交前保持VERIFYING。
