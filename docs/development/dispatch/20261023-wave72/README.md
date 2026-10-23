# W72 / 七天一期 D2：移动任务三组独立入口当前Owner

COORD本聊天单人，开工2026-10-23T17:40:11.035533+00:00；Main `3c9b56ceaf66073d780c1dcb82f8bbba87bf0098`，Handoff `3eedbd17b1910905b133cabb1a10ee4250b01b43`。W71 Main449/450、Handoff47全部采用，Issue448关闭，41独立本地用例/类型检查通过；实际29.77min＋前置4min，两准确分支删除、固定树干净detached、全部席位释放。W37–W71不重复。

## 前置、范围与真实估时

AGENTS/POLICY哈希未变；status/capacity无容量告警，非allowlist两目录不动。开放Issue无重复后登记[Issue451](https://github.com/GTX537/CP6/issues/451)。已核最新台账/七天目标、MobileService/V1/V2及四个HTTP入口/页面/实体/当前IAM映射；读取MOBILE目标入口和当前binding e385664760f43759、正文dbb44d5be748357d的共同规则、§§1–6、8.1–8.3。原接受身份优先于正文内历史候选字样，不把旧ImplementationAuthorized=false当本次用户七天授权不存在。

旧Mobile和V1服务无当前Owner，V2只按任务可填AreaCd裁剪，三者未最终核原会话/动作/字段/部门，不能证明真实源与目标两端权限；旧Scan能列库存，旧Complete直接移动库存，V2还有events/source/analytics入口。PG现在沿现有OperationsOwner，以原生同事务当前权限/身份最终复核覆盖全部公共方法。

本窗口120–180min：当前Owner及三入口/双端范围45–65，历史投影及未采用命令30–45，页面/定向验证/合并收尾45–70；独立前置约3min，W71合并等待中的只读核查不重复计时。延续同一七天ACTIVE目标，不另起目标。

1. 同一移动任务对象范围用于列表、详情、事件、来源任务及旧统计；tenant过滤、本人实际Creator、部门/子部门/自定义用现有IAM显式mobile-task UUID绑定，范围先于分页/计数。任务AreaCd不授库区权，两个真实Location独立核权；无实际位置不猜。原生映射保旧kind、同租户真实对象/部门、身份不可变、CAS/原结果和审计，不回填。
2. 历史MOVE读取保原协议代数及准确数量；人员/指令/备注/异常文本按当前字段投影，未授权历史source/parent/device/raw event不外泄。旧非MOVE缺kind专属安全投影（尤其COUNT盲盘和LABEL无数量）显式503，不以零或假空冒合法投影；本轮不把六类任务合同缩成MOVE已交付。页码参数冻结、稳定排序；旧统计标记创建日期批次/历史经过时长/样本数，不能冒净效率或物理完成。
3. 所有旧PG写命令和扫码接受、scan-profile在当前动作及既有对象检查后、任何编号/库存/容量/任务/scan/receipt/通知前明确未采用拒绝。新原件/source/device/Claim/原操作共同门不齐，不回退旧链；不删除或改写既有任务/receipt、不用拒绝伪称已有未知效果为零。原精确receipt恢复、新S1六kind、现场设备/客户端/标签/条码维护另属后续。
4. 实际HTTP保Owner状态/no-store，legacy GET补现有view动作；Program登记scope/字段，不增加角色授权。页面默认只读，历史状态不称真实完成，能力/查询失败与空集分开，身份/乱序结果清理，命令重复/未知不自动重发。独立分析小范围同步身份/失败显示，其他控制台模块不扩范围。

允许：MobileService/TaskV1/V2及接口/合同、OperationsOwner/Access、现有IAM映射及新PG迁移、Program、相关四HTTP、移动页面/API/types与控制台分析小段、必要语言seed、相关Tests，以及本任务docs。公共资源本聊天串行编辑；DEV-1 `codex/bug-451-mobile-owner`、DEV-2 `codex/w72-handoff`固定目录复用。

相关PG红灯后验证三服务Owner、双端/tenant/self/dept/当前撤权/字段/并发、实际IAM原生绑定和旧kind回归、全部命令零效果、实际Program/login/HTTP；页面身份/失败/只读定向检查。一轮完整作者SPEC/QUALITY，已有证据按输入复用。全卡24/375、125目标0交付、247运行NOT_RUN保持。停自有PG保数据与失败，正常两仓PR/精确采用/分支删除/finish释放。无代理/Actions/既有库迁移/生产部署。W51指定data删除曾被自动审批以blocked by policy拒绝，保留不重试。

## W72验证完成

50项独立.NET（21真实PG、29既有InMemory）及23项Vue通过，无跳过；Vue类型检查和相关构建通过。三个PG停止保留，PG/UI原始红灯保留。来源查询经审查确认关联筛选需真实Source Owner而明确503，未采用为功能完成。进入两仓采用及同任务最后收尾；完整S1与验收不提升，七天目标ACTIVE。

## 实际采用与最后收尾

Main [PR452](https://github.com/GTX537/CP6/pull/452) source `b3b38164e1d3732053f8241b86580a5d0f184f13`采用远端main `41cef2ffc77464745c18576e0f60108d59a8ec37`，36任务路径核对。Handoff [PR48](https://github.com/GTX537/CP6.AI-Handoff/pull/48)采用 `f833756a86252cd8fefa9d8f9af90c906904ab6e`，准确任务分支删除、固定树干净detached、finish释放。26个产品路径与已测提交逐blob相同，复用有效成功结果；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue451在最终远端和生命周期核对后关闭。当前约25.85min，原估120–180min，最终以finish为准，独立前置约3min，子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave72-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续D5/D6真实CRM Owner及公开表单基础，完整S1保持未采用。
