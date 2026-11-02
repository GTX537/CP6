# CodeGen比较预览V1

POST `/api/pub/codegen/comparison/v1`，沿用认证和`pub-codegen:view`，不需要save权限。输入Table/Columns沿用现有元数据；TemplateRef=`cp6.codegen.crud.embedded`、TemplateVersion=`1.0.0`来自即时preview/v1。未知版本立即400，不加载用户模板。原字典预览及preview/v1保持原响应。

ExistingFiles为`{targetPath,content}`列表；OverwritePaths为显式允许覆盖的精确路径列表，最多各五项。两列表必须提供，可为空；Content空字符串表示真实提供的空文本，缺文件表示NoBaseline，不能解释为磁盘不存在。只接受服务器固定模板的精确路径，无绝对路径、父路径、反斜杠、大小写别名、流名、通配或重复项。HTTP体上限1MiB，原文件UTF-8文本总量也受1MiB约束；JSON开销计入传输上限，临界大小请求可能需要缩小。没有服务器文件读取/写入。

|产物|固定建议路径|
|---|---|
|Entity|CP6.Entity/DomainModels/{Module}/{Entity}.cs|
|Service|CP6.Core/Services/{Module}/{Entity}Service.cs|
|Controller|CP6.WebApi/Controllers/{Module}/{Entity}Controller.cs|
|列配置|cp6.web/src/types/{Module}/{camelEntity}.ts|
|Vue|cp6.web/src/views/{Module}/{Entity}View.vue|

Module/Entity沿用安全标识符边界，比较入口额外拒绝Module为Windows设备名。路径是当前固定五产物的建议，不宣称原PUB08历史八产物全部实现；不会生成缺失的API模块、执行依赖安装或部署。

返回schemaVersion=`codegen-comparison-v1`，applied恒false；保留原InputDigest（元数据/模板），新增ComparisonDigest绑定原文件内容、路径及覆盖选择，顺序无关。摘要只用于输入追踪，不作为保存授权、磁盘版本或应用凭据。

|Status|含义|
|---|---|
|NoBaseline|未提供原文件；只有生成文本，不断言可新建|
|Unchanged|保留自定义区后文本完全相同|
|OverwriteBlocked|存在变化，但未进入显式覆盖白名单|
|OverwriteAllowed|当前提供的文本上允许替换框架区，仍未应用|
|CustomBlockConflict|标记不平衡/嵌套或旧块不能与新槽对应；候选与diff为空，禁止覆盖|

自定义区以去除行首尾空白后独占一行的`// <custom>`与`// </custom>`标记识别。原块全文保留，包括混合换行；若原闭标记在EOF没有换行，而后面要接新框架，额外插入LF分隔，避免注释吞掉框架。不是代码语法分析器；不要在自定义区内部嵌套同名标记。当前固定Service模板只有一个自定义槽，其他产物没有。

Difference是线性单区间对比：StartLine从1起，RemovedLines/AddedLines保留各行终止符，包括CRLF/CR/LF和末行无换行；替换该区间可重建精确候选。不保证最小编辑距离，中间相同的行也可能落在同一区间内。显示被阻止覆盖文件的差异供审阅，不把被阻止误报为应用成功。

Vue使用现有即时预览版本准备固定路径，然后让用户选择UTF-8文件或粘贴文本并逐项允许覆盖。读取文件本身不发API；用户请求比较时发送已提供的文本。UTF-8读取严格解码并保留BOM；手工编辑textarea遵循浏览器换行规范。输入/原文件/覆盖选择变化即清除结果，晚到响应丢弃。所有代码按文本显示，不执行。

真实文件应用仍需独立Owner范围、当前磁盘版本、链接/路径实际解析和提交时并发检查；本V1不提供此能力。元数据租户scope/CAS保存/unknown恢复、业务编号消费者和完整WP02-16仍余项，不能凭本比较关闭整卡。
