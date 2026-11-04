# BUG756 — 保留个人FORM完整合法数据预算

[Issue756](https://github.com/GTX537/CP6/issues/756)，Main基线`eb8ecfbf97f00151e58ae1da2234e782d080c2b0`，固定DEV1、`codex/bug-756-form-data-envelope`，COORD单人。由[W304原卡核对](../W304-01/DELIVERY.md)发现并优先修复。

个人FORM的1 MiB业务数据预算与命令外层开销分离；内部存储JSON仅作必要转义，中文、补充平面和私用区Unicode不再被二次转义挤出合法范围。

原OA-FORM约定data不超过1 MiB。旧Owner的WFC解析器与HTTP入口把整个命令限制为1 MiB，因此合法data加命令ID、上下文和版本就被拒绝。现在仅携带业务data的指定FORM入口给命令外层预留64 KiB；所有通用WFC调用保持原默认，data本体以及合并/patch/规则后的完整数据仍检查1 MiB。

第二个同根缺陷是内部JsonNode默认序列化会扩张Unicode；简单改用UnsafeRelaxedJsonEscaping仍会扩张emoji和私用字符。最终采用Owner内的必要JSON转义输出，保留顺序、数字原词法和各JSON类型。Merge/Patch只序列化一次并复用结果校验。普通HTML字符作为业务字符串保留，HTTP输出仍由JSON框架编码，页面仍按数据/文本消费；没有新增HTML插入。

原输入摘要和成功回执不重算，既有数据库和历史快照不改写。新写入的数据文字表示可由Unicode转义变为等价UTF-8，值与金额精度无损；旧成功命令仍沿原回执恢复。跨版本未完成预览可能需要重新取得当前预览，不自动改写旧预览或将冲突当成功。

本地PostgreSQL18和实际Main Program认证HTTP通过：原生/HTTP满额提交、双预览、草稿创建/保存/提交、退回编辑和SFS决策；超出1字节明确拒绝且无额外效果，原意图回放、原摘要、精确数值及退回前快照保留。另验证中文满额HTTP、补充平面/私用字符经merge/patch/rules/projection、合并后真实超限拒绝、嵌套数组/控制字符/空值/布尔和精确数字。当前隐藏/只读mask、重基规则及原结果、五上下文closed DTO和canonical回归均通过。

最终实际执行14项相关场景，无跳过冒充通过。模拟用户、表单、审批和业务数据，真实隔离PG18及实际Main认证HTTP；唯一索引`D:/CP6-local-test/bug756/foundation/final-validation.json`保留全部日志/JSON/TRX与运行范围。没有本轮浏览器或全仓运行，UI原有证据按W275/W293/W303范围复用；不宣称生产部署。

失败历史如实保留：首轮原生与HTTP各一项失败，原生COMMAND_SHAPE_INVALID和HTTP请求体上限；首次GREEN被新增测试引用私有常量挡在编译，改为实际公开路由值；随后六项中五项通过，中文满额FORM_DATA_INVALID；宽松编码器改动后中文及五回归通过，但新增emoji/私用字符两项仍失败。最终必要转义实现后运行上述全部受影响范围。中间成功只证明当时范围，不覆盖后续发现的问题。隔离HTTP开发异常页原始日志留在本机，不把其中模拟登录头带入仓库或交付正文。

集中审查整个分支：仅五个携带data/dataPatch的命令及两个预览采用1 MiB+64 KiB有界外层预算；业务Shape与合并后大小检查仍为1 MiB，通用WFC入口默认、重复键/Unicode/深度/摘要规则未变。存储序列化保属性/数组顺序、数字词法和JSON必要转义，不作为HTML输出；HTTP外层序列化、UI纯文本读取和授权投影照旧。沿已有原生Owner事务/最终权限fence，不增资格包装或多层重复校验；无迁移、依赖、UI或workflow变化。

原37531DONE/2IN_PROGRESS/342PLANNED、原59余52及125/247正式状态不变。WP12-03/04还余旧入口适配与重基前快照新草稿恢复，不能用本BUG关闭整卡。24小时目标继续；下一W316受控旧入口采用，之后W317恢复。Issue仅在远端main准确采用核对后关闭。

分支处置、工作区状态和官方finish席位释放以`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug756-final-verification.json`核销。无需新增Handoff席位，下一总控窗口统一同步。无Actions触发/取消、触发配置改变、生产部署或既有数据库迁移。
