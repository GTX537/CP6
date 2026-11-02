# W270-01：FORM发布版本与独立激活

W270：FORM准确发布预览/不可变版本、显式准确来源下一草稿、独立激活CAS/历史/原件及设计器/管理页采用已实现验证；24h持续目标第三窗口，继续W271。

[24小时连续计划](../../planning/20261102-codex-24h-oa.md)，基线`782ec746f78031059ec50b4987a48e325427f202`；COORD单人、DEV1 Main/DEV2 Handoff。

## 实际产品行为

FORM独立DIRECT_FORM_PUBLICATION_V1政策；发布须原form-save、显式form-publish、当前资源/完整字段可读及SOD/不同作者规则，既有FLOW发布政策不扩权。预览绑定准确draft ID/token、完整原schema摘要、政策版本及外部来源诊断，有效期10分钟；发布在当前IAM/Scope事务门内重核预览与到期，Draft→Published、不可变版本、审计/原命令原子提交，不自动Enable。旧schema原文/分类保持，历史按当前字段/资源权限披露。附件缺少真实File Owner准入时明确FORM_FILE_SOURCE_REQUIRED，不宣称已资格齐备。

GET head与draft分开，已发布返回无草稿；先核资源可见性再报告草稿状态。下一草稿须准确同定义Published sourceVersionId与当前head token，能选择较旧版本，服务端分配下一整数版本；已有草稿拒绝覆盖。数据库生成opaque token、已发布快照/命令/审计不可变，draft身份只允许明确状态转换，旧native写门保持；无隐式latest、历史pin回填或native运行投影。

FORM activation独立revision/准确Published版本/原因/历史/原件，与schema草稿和发布分轴。oa-flow-admin:form-enable是显式新动作，已有FLOW enable不默授FORM；管理员无需、也不获得设计器编辑/发布能力。并发令牌不能覆盖后发状态，原command恢复不重复写历史。启停不修改运行实例、业务单据或任务；当前运行采用尚未接入，影响数返回未知而非猜0。

现表单工作区增加发布预览、确认、只读版本和准确来源创建下一稿，既有flow-admin增加FORM版本启停页。版本/目标切换可取消丢稿，Published内容只读；保留数字原文和完整schema。命令发送前持久化，同一缓存/命令恢复；NOT_OBSERVED、未知后的403/409或网络失败不当作未执行；损坏/不可读缓存保留并立即阻止新操作，无关storage事件不清稿。身份切换丢弃旧响应，隐藏历史schema不进DOM；发布与启停分别展示实际权威结果。

## 实际验证、审查与更正

本地36项唯一PG18行为通过、0跳过（15 FORM草稿回归、19 FORM发布/激活、2既有FLOW实际HTTP回归）；24项Vue组件行为通过；显式应用配置vue-tsc --noEmit -p tsconfig.app.json --extendedDiagnostics通过，实际检查3325文件。当前Main真实登录/CSRF/HTTP与隔离PG执行，政策/业务数据模拟；无Actions/工作流变更/既有环境迁移/生产部署。

证据D:/CP6-local-test/wave270/foundation/final-validation.json。baseline 15；lifecycle首轮19含1个真实可见性顺序失败，修复后permission-review定向3通过；flow-http-regression 2；boundary-review定向1复查PG不可表示NUL原因/Key在外部入口明确拒绝。去重36，各用例最新有效来源保留，不将失败运行删掉或改称通过。Vue最后24项，W269未改的完整编辑器原文/撤销证据可继续采用。组件API是模拟，不冒充浏览器到PG全链；实际Main HTTP由PG用例独立运行，W278仍负责整链。

本次发现验证口径缺陷：根tsconfig.json的files=[]仅含references，原W268/W269执行vue-tsc --noEmit虽退出0但不检查应用源码，不能作为应用类型通过证据复用。已在两份历史报告注明更正；本轮显式-p tsconfig.app.json真实检查3325文件通过，保留历史日志原样，不回写历史为新通过。最后文案修改只检查差异，类型输入未变，复用同次有效结果。

一次作者集中SPEC/QUALITY覆盖完整分支：准确版本/预览/期限、最终当前授权/原件披露、PG迁移/不可变/锁竞争/原子性、草稿与启停分轴、旧native互斥、页面未知提交/缓存/身份隔离、375范围及类型门有效性。修正了先返回无draft导致不可见定义状态泄露、原件查询失败误当明确失败的页面分支、不可读缓存的静默清理，以及PG不可表示文本的边界错误；仅对修改处定向回归，无未解决必需门禁。

## 剩余、工时与生命周期

WP12-01仍余FLOW/FORM固定sourceVersion克隆、完整原AC与比较等原范围；WP12-02仍余FORM/子流程精确依赖、唯一绑定和实际运行/旧入口采用。文件Owner准入、提交字段/规则权限、个人草稿及跨窗口浏览器到PG全链按W272–279推进。新FORM启用配置不代表运行入口已接入，未知影响数不写0；125/247及原375整卡状态保持，59局部仍3闭环/56余项。

W269最终累计4138秒，本窗口完整实际投入和总累计以wave270-final-verification.json核销；总目标保持IN_PROGRESS，2小时点再按真实吞吐重估和补相邻原范围，不拖延或重复测试凑24h。

本提交是合并前快照：Main/Handoff正常远端采用、准确任务分支删除、进程停止、官方finish和席位释放在最终证明核销。固定目录复用，主目录用户未跟踪资料保留；不重复七天/W249。
