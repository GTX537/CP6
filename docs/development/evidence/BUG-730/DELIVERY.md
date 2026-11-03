# BUG #730：SFS办理意见Unicode容量

[Issue #730](https://github.com/GTX537/CP6/issues/730)，基线`5b5c900251a5065e0ce174c0e33cecfa7fdba9a1`。COORD单人从最新main领取固定DEV1，分支`codex/bug-730-sfs-comment`；W290两仓完整收尾后开工。

SFS意见按Unicode字符计数，1000个补充字符不截断，1001保留全文并禁提交；三张工作流运行记录的PostgreSQL容量同步为1000个字符，保留尾空格。

实际Chrome原步骤先复现：输入1000个🙂，HTML maxlength=1000按UTF-16保留500个；组件级同时发现程序调用未提前拒绝超长意见。移除会截断输入的maxlength，按code point显示计数，超1000保留全文和明确提示，两个按钮及发送入口都拒绝。SFS可空意见合同不变，APP仍为其原500字符域。

UI修复后的真实POST暴露同一容量不一致的存储问题：Wf_FlowFormTo.Comment旧UTF16约束拒绝合法1000字符而返回503。实际决定同时写Wf_FlowTask、Wf_FlowHistory和Wf_FlowFormTo，三者原PG兼容映射都限制1000 UTF16单元。定向前向迁移与Core的PostgreSQL模型/快照将这三个Comment约束改为1000 Unicode code point；使用bpcharsend原bytes解码后计数，不能转text丢尾空格。列/数据没有转换或截断，旧合法UTF16长度≤1000必然满足新字符长度≤1000，是无损扩容。共享实体MaxLength、SQL Server模型和其他字段历史容量不变；新业务仍由各自Owner控制500/1000上限，不借存储容量更改权限/租户/事务规则。

真实Chrome先复现1000个🙂被截为500；UI修复后真实POST进一步发现旧PG UTF16约束503。最终Chrome/登录HTTP/PG完整1000字符保存通过；定向PG行为验证1001拒绝且无票、三张运行表精确1000含尾空格保存与1001数据库约束拒绝、原命令回放；原APP500字符/命令兼容回归通过。9项相关Vue行为、显式应用vue-tsc通过。

完整证据`D:/CP6-local-test/bug730/foundation/final-validation.json`。保留初始组件红灯、浏览器截断红灯及只修UI后DB503失败，不伪称通过。新夹具的原结果JSON引用比较改为内容比较；直接尝试改已完成事实先被原不可变触发器阻止，故超限边界改为临时表LIKE INCLUDING CONSTRAINTS采用数据库实际约束，生产保护不动。两项夹具修正均只定向重跑；同批已通过的Chrome与APP证据直接复用。浏览器使用模拟业务数据与实际PG18、内部Owner、认证HTTP、Chrome；数据库迁移只运行于隔离临时测试库。一次集中审查修复完整diff，相关成功结果按相同输入复用。没有全仓测试、Actions、已有数据库迁移或生产部署。

原37528DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125/247不变。22h有效执行校准保存在`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug730-22h-calibration.json`；本修复计入同一24h目标，随后接续原WP12-08逐项核证与真实剩余。

Issue保持Open至远端main准确采用后关闭；本任务分支定向清理、工作区干净、官方finish及DEV1释放以`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug730-final-verification.json`核销。DEV2/集成席位保持空闲；固定缓存和根用户归档保留。
