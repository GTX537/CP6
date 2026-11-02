# W271-01：固定发布源FLOW/FORM克隆

W271：FLOW/FORM从准确Published版本克隆新停用草稿，清流程身份码、不可变clonedFromRef、当前读源/创建权限与原命令恢复；Main HTTP及版本面板采用。24h持续目标第四窗口，继续W272。

[24小时持续计划](../../planning/20261102-codex-24h-oa.md)，基线`ea4236450ade3e0fa5ffe98e1e5aa04aaeec3766`。COORD单人、固定DEV1/DEV2；首次2h校准随本任务同步。

## 产品行为与边界

POST /api/oa/v2/definitions/{FLOW|FORM}/{sourceKey}/clone要求明确sourceVersionId、新Key/name和expectedVersion:null。只能复制当前租户/环境、指定定义的不可变Published版本；草稿/别的定义/别的租户来源拒绝。复制完整schema原文和设计引用，FLOW保FormKey、清FunctionId/FlowCode，FORM保分类；新Owner为当前actor，新head/draft、整数版本1，默认停用，无运行事实或通知副作用。保存为Draft后仍按原schema边界编辑；不因克隆重复验证可信历史快照或换为latest。

直接复用两类草稿Owner的原子写入事务，新增FLOW_CLONE/FORM_CLONE独立命令槽，以目标Key恢复；摘要绑定源路由和准确源版本，原始请求保留。当前源资源/字段可读与目标创建/字段写权限在实际边界核验，最终点重核会变化的授权。目标冲突不覆盖；提交未知查/重试原命令，之后写权限撤销或Scope禁用仍可按当前读权限投影原件。FLOW原件路径补齐响应完成点expiry检查；缩窄原有23514错误翻译，审计故障不再被误报身份冲突。

clonedFromRef为准确来源版本GUID，存于目标head；同租户/环境外键关联不可变versions，UPDATE不可更换或抹除。它是目标定义的创建来源元数据，不授予源定义的新读取权。普通保存、下一稿和既有发布/激活保留该来源；不复制当前activation配置。数据库变更只在隔离测试PG运行，本任务没有迁移既有环境。

FLOW/FORM现历史版本面板嵌入共用克隆入口，明确显示选中的版本、新Key/name和停用结果。未保存修改阻止新克隆；成功才打开目标。发送前持久化恢复记录，未知/NOT_OBSERVED/拒绝查询或未知后的冲突不换新意图，损坏缓存保留并阻止新操作；身份切换清两个缓存、丢弃旧响应。克隆恢复锁定发布/切换，权限拒绝清历史私有内容。

## 实际验证和一次审查

本地48项唯一PG18行为通过、0跳过（16克隆行为含实际Main HTTP/并发/权限/回滚，32受影响草稿与版本回归）；22项Vue行为通过；显式vue-tsc --noEmit -p tsconfig.app.json --extendedDiagnostics通过，实际3328文件。政策/业务数据模拟、真实隔离PG与Main登录会话；组件API模拟，无浏览器到PG整链、Actions、既有环境迁移或生产部署。

证据D:/CP6-local-test/wave271/foundation/final-validation.json。首次clone编译失败来自新测试写错DbSet名；修正后behavior 46项中44通过，2项故障注入ADD CHECK检查到了其他测试租户历史模拟行，改为NOT VALID只约束后续写入后fault-review 2通过。集中审查补齐FLOW原件最终expiry并添加2项边界测试；completion-review原件回归通过、2项新测试清理ClaimsIdentity所复制Claim的对象引用失败，修正测试清理后定向通过。所有原失败日志保留，48为去重后的最新有效结果，不把失败轮次整体称通过。

一次作者SPEC/QUALITY集中审查覆盖完整分支：准确源/路由/目标槽、当前授权与最终expiry、租户隔离、原件披露、PG FK/不可变/并发/审计原子性、原草稿行为回归、UI缓存/锁/身份/隐藏历史，以及首次工时校准。无子代理、无全仓产品回归；受影响32项旧行为是草稿写入口复用的必要回归。应用类型检查真实使用显式应用配置，未复用空根配置。

## 余项、时间与生命周期

WP12-01仍余准确版本比较/完整原AC；WP12-02仍余FORM/子流程依赖pin、递归资格、唯一绑定、实际运行和旧入口采用。克隆仅v2 managed定义路径，不称旧Designer.CloneAsync已退役，不复制运行/审批/外呼事实。125/247和原375整卡状态保持，59局部仍3闭环/56余项。

W270最终累计6743秒；本窗口完整投入与累计按wave271-final-verification.json核销。首个2h校准在累计7274秒完成：前三窗口实耗112分23秒，低于原150–315分钟；W271–279暂调9.5–19h，新增原范围W280版本比较、W281目录/历史/最终下载披露、W282退回重送/转交加签恢复、W283准确截止/worker竞争，5.75–11.5h。剩余预测15.25–30.5h，低置信度，容量仍24h；下一校准累计4h，不空等或补重复验证。扩展只规划，开工前再核对应实际提供者。

本提交为合并前快照：Main/Handoff正常远端采用、准确任务分支删除、进程停止、官方finish/席位释放以最终证明核销。主目录用户资料保留，固定路径复用，七天/W249不重复。
