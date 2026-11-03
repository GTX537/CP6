# BUG #725：直接提交附件的独立动作权限

[Issue #725](https://github.com/GTX537/CP6/issues/725)，精确基线26a590985fb562fb6ee489aba2cc91eb67a7be21，COORD在固定DEV1分支codex/bug-725-form-file-submit执行。

普通FORM附件准备接受当前已登记的新增草稿或直接提交权限；仅有query/submit的发起人可上传后直接提交，不创建草稿、不取得add权限。

原OA-FORM-01 R1 §4/§5将草稿创建的read/add和直接提交的read/submit分开。原FORM文件入口固定要求add，导致合法直接提交输入受限。现有动作边界复用为AnyAction，仍要求当前菜单、角色动作和启用菜单下实际登记的动作；既有单动作调用保持同一约束。FORM附件先核query，优先合法add路径；仅submit时核真正可提交的表单/绑定准入。字段、准确版本、租户和当前身份约束不变。每个最终草稿/直接提交命令仍独立核自己的权限，DRAFT附件仍要求edit，保留锁仍要求file-content权限。

没有新输入字段、引用格式或日志重编码。上传预检及扫描后的最终事务都使用当前权限；撤权后新写拒绝，已提交原操作在当前读权下仍可查回，不以UNKNOWN作为无效果。已有撤权用例现在撤销add和submit两种合法准备路径，保留原丢响应恢复意图。

35项唯一受影响自动化行为通过：33项实际PostgreSQL/HTTP行为（包含真实Defender扫描和3条HTTP场景），2项纯重基行为；0跳过。34项集中回归通过后，仅新登记动作边界及修正实际v2草稿计数的3项定向复验通过，未重复未受影响结果。

红测：D:\CP6-local-test\bug725\foundation\red-20261103T103549Z.log，未修复时仅query/submit实际上传抛ACTION_FORBIDDEN。修复后5项针对性权限行为覆盖直接提交零实际v2草稿、add-only建草稿但无submit、现有草稿无edit、无写权零读字节、缺提交绑定、字段隐藏、扫描期间撤权、当前菜单动作撤销和当前read撤销。实际Main Program/cookie/CSRF HTTP上传经过Microsoft Defender，直接提交后当前历史下载精确字节，隐藏字段/伪造字段拒绝。测试数据和身份是明确模拟，PostgreSQL18/HTTP/实际扫描是真执行；确定性故障测试的内存存储/扫描器双替身单独标识。

证据：D:/CP6-local-test/bug725/foundation/final-validation.json及所列原始日志/TRX。集中审查中将“零草稿”断言改为实际cp6_personal_form_v2.drafts计数，避免查询旧Wf_FormDrafts冒充当前证据；仅这两项和新增登记权限边界再验证，其他通过来源复用。无UI行为改动，不重复浏览器/组件测试；没有数据库迁移、Actions触发或取消、工作流修改、部署。

一次作者SPEC/QUALITY集中审查最终完整差异，覆盖权限分型、原操作恢复、最终准入、测试真实来源及范围。四状态同步，原375/56局部卡/125目标/247正式验收状态不变。Issue保持Open至正常PR合并与远端采用确认；准确任务分支清理、工作区干净、官方finish释放由C:/Users/tt/CP6-planning-reports/20261017-foundation/bug725-final-verification.json核销。24h有效执行目标继续，下一窗口按原范围落实外部选项Owner准入。
