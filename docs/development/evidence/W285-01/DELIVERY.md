# W285-01 普通FORM内容与引用绑定

W285：普通FORM不可变内容/扫描/原操作、草稿与提交及退回的同事务引用绑定、当前业务对象和字段权限下载已贯通实际页面；真实PG/HTTP/Chrome/Defender完成限定验收。 本地相关验收和集中审查通过，远端采用/准确清理/finish待最终核销，不等同整卡完成。

基线094627927cb50e60f896a76d1d9a35216119020f；COORD单人，固定DEV1/Main、DEV2/Handoff。按原OA-FORM-01 R1和SYS-FILE-01全文，采用PostgreSQL、Trust Internal Contracts和Fail-Fast。

## 实际实现

新增Main.File.FormContent.v2，普通FORM文件使用明确版本的内容引用，不冒充EngineeringRevision。上传入口在读取字节前核当前业务/字段授权；20MiB上限、实际存储、摘要核对与扫描后，再核当前身份/版本/权限并提交不可变内容、状态和原操作。状态保留真实CLEAN/PENDING/NOT_CLEAN及HOLD，保留锁控制还要求独立file-content权限。取消未观察操作使用原日志CLOSED，不把UNKNOWN解释成无效果；失败暂存原字节不进行猜测清理。

草稿、直接提交、草稿提交、重基、退回修改/重送在原Owner事务中准入并绑定准确FORM字段、业务对象、版本及命令。一个暂存原件只允许一条实际业务谱系，草稿转提交沿原谱系，跨对象/字段/租户拒绝。提交延期约束要求真实数据里的每个引用有该命令绑定，缺绑定时数据/通知/命令/认领一起回滚。隐藏/只读的原草稿值可保留，提交仍检查实际当前内容可用性。草稿删除和引用移除均不删除原件。

发布/激活采用实际FORM内容端口；端口约束缺失时继续准确字段诊断，未改成无条件放开upload。个人、退回、任务收件箱和提交查询使用当前业务对象与字段读权，不依赖持有URL。下载只返回经原字节摘要核对的附件，最终授权后才返回；完整性故障写NOT_CLEAN并拒绝内容，未使用默认文件。原操作回执与当前附件状态分开，页面回执恢复后重读当前状态。

Vue实际上传文件、UTF8命令头、原件下载与当前状态；浏览器持久化只保存身份/操作ID/种类，不保存私有文件名或字节。上传丢响应后刷新只查询原操作，不伪造重传。未确认附件阻止保存/提交和置空；身份/当前读权拒绝清私有内容，后续拒绝不能抹除原未知意图。表单字段仅传版本化引用，不传任意URL。

## 实际验证与集中审查

11项唯一相关PostgreSQL/HTTP行为和1条实际Chrome→Vue→Main→PostgreSQL/Defender链通过，0跳过；42项相关Vue行为及vue-tsc --build通过。覆盖同事务绑定与整事务回滚、未知原操作与当前撤权、并发认领、退回替换、隐藏值保留/重基、扫描/保留/损坏状态、当前对象字段授权、精确原件下载及刷新恢复不重传。 唯一行为及每项真实执行来源见D:/CP6-local-test/wave285/foundation/final-validation.json。

实际隔离新PostgreSQL18、Main Program/cookie/CSRF HTTP、真实Chrome/Vue和Microsoft Defender。业务身份、表单、政策、租户时区和原文件是模拟业务输入；内部Owner/事务/存储/扫描均真实执行。针对故障的内存存储/扫描器双替身另用于确定性PG边界测试，不冒充真实扫描。

已覆盖：原操作提交未知/当前撤权/上传前拒绝零读字节；缺事务绑定全回滚与同命令修复重试；同操作竞争及双草稿不能重复认领；退回替换与原快照保留、重送同实例、旧引用失去当前下载权；隐藏值保留与重基不改变原内容身份；PENDING、HOLD权限、完整性损坏及删除草稿不清文件；HTTP精确字节/摘要/响应头、当前隐藏和伪造字段拒绝。浏览器真实上传成功后丢响应，刷新查询原件，保存草稿、提交、历史只读下载均执行；原件字节逐字节相等。后续时间区模拟输入已补足，未将初次缺时区的查询页截图称为全页无错误。

实际来源：D:/CP6-local-test/wave285/foundation下file-binding-alias、file-runtime、file-http-route、file-review、file-browser-reviewed及相关Vue日志；web-typecheck-reviewed.log退出0。每项复用保留原执行来源，不因提交或SHA改变重跑。所有早期编译、迁移SQL别名、验收路由和组件mock导入失败日志保留，未将失败整包写为通过。原Main的PersonalFormEditor既有移除/空值测试先在精确基线单独复现失败，见ui-baseline-unset.log与wave285-baseline-field-probe.json；独立BUG721经PR722远端采用、Issue关闭与官方finish后，本分支官方resume并正常merge最新main。合并保留fileContext与默认allowUnset=true，实际42项相关Vue整包和最终类型检查通过；恢复历史见bug721-final-verification.json及wave285-merge-bug721.json。

一次作者SPEC/QUALITY集中审查覆盖完整差异、原合同、版本化内容/业务身份、租户/字段授权、同事务约束、未知提交、保留控制、当前下载与页面清私有边界。审查中修正普通表单编辑不能获得HOLD/RELEASE权限、原回执不能充当当前状态、未确认附件不能置空，并复用已经读取的内部actor/mask/content。对应定向验证通过；采用#721的合并差异已复查，未确认附件的两种清空动作又以实际父组件行为验证，42项UI和最终类型检查通过。未受影响的后端/浏览器结果保留真实来源，不因合并SHA改变重跑。

## 余项与处置

不覆盖全Owner引用图/物理清理、部门范围文件管理员适配、历史任意快照附件下载、外部选项Owner等剩余范围。不改已有工程/DOC身份，不执行既有数据库迁移或生产部署，不增加SQL Server要求。原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部3闭环/56余项，125/247正式状态不变，WP12-03/05与WP02-14整卡不关闭。

曾因独立主线BUG721将W285精确保存远端并官方finish(deferred)，后复用DEV1修复；PR722远端采用且Issue关闭、准确分支删除/finish完成。本分支已官方resume并正常merge最新main，四状态保留两项任务历史，无覆盖或新增工作树。当前准备Main/Handoff正常交付，最终远端采用、分支删除、干净工作区和席位释放由C:/Users/tt/CP6-planning-reports/20261017-foundation/wave285-final-verification.json核销。24h总目标持续ACTIVE；18h校准已登记，下一20h，W286随后开工。

无Actions触发/取消、无工作流修改、无生产部署，根目录用户资料保留。
