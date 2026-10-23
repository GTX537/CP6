# W72-01 / Issue451 独立移动任务当前Owner

COORD本聊天单人，2026-10-23T18:01:59.123916+00:00。Main基线 `3c9b56ceaf66073d780c1dcb82f8bbba87bf0098`；DEV-1任务BUG-451-MOBILE-OWNER，分支codex/bug-451-mobile-owner；DEV-2仅W72-HANDOFF。唯一产品报告；[Issue451](https://github.com/GTX537/CP6/issues/451)。

## 缺陷与实施

旧MobileService、MobileTaskV1Service未核当前Owner，V2只依赖旧仓区查询，三入口不能证明双端实际位置、原会话和当前动作/对象/字段；旧取消还能信任调用方用户名。三个无Owner读取和一个伪造调用人取消的PG真实红灯保留。

PG全部公共入口现要求同一DbContext的OperationsOwner，在原生事务内核当前原会话、租户/角色/组织/动作/字段并最终复核。仅移动资源增加Task/Event/ScanLog/CommandReceipt的图锁和预跟踪保护；既有脏工作、错上下文、外层事务拒绝保留。非PG历史实现保留，已有InMemory结果不作SQL Server验收。

列表、详情、事件及旧统计使用共同对象范围：本人只按实际Creator；部门/子部门/自定义用可信同租户mobile-task UUID绑定；全域仍核仓区。来源和目标均需实际存活同仓Location，两个端点分别核当前仓库/仓区授权，允许授权联合；Task.AreaCd和AssignedTo不授对象权。范围在分页/计数/聚合前，页码参数冻结与稳定排序。V1保留原跨代MOVE历史读取语义，V2保留version2。不能安全投影的非MOVE（如COUNT盲盘）明确503，不造零数量或假空集，不改变源记录数量。

IAM在既有standalone mapping增加mobile-task，复用CAS、原操作结果和审计，选择器核同租户真实存活对象；新前向PG迁移扩展kind与实际对象验证，保留旧六kind、映射身份不可变和部门约束，不回填或猜归属。Program登记五级scope和四字段，未授角色新权限。

列表固定隐藏Instruction/Remarks/ExceptionDescription，详情按当前MIN规则显示；AssignedTo也核当前字段，禁止通过无权AssignedTo筛选推断。Creator/Modifier、source关联、parent/remainder、device、库存交易编号、原始事件身份/设备/DataJson及未经映射的原因文本不暴露；源数据不改。事件保任务号、类型、原OperationId、执行版本和时间，仍属历史记录，非完整S1证据。

START曾拟来源任务同范围读取；集中审查确认sourceType/sourceNo筛选本身会泄漏未授权来源关系，且无实际Source Owner/完整任务家族。因此GetSourceTasks在当前view Owner后503 MOBILE_OWNER_ADOPTION_UNPROVEN，本轮不声明来源查询可用。scan-profile同样缺正式采用门而拒绝。

26个旧写入/扫码入口加scan-profile共27次调用在一项零效果验证中覆盖：先核当前动作及已有任务对象（适用时），再于编号/库存/容量/任务/scan/receipt/审计/通知前503 MOBILE_OWNER_ADOPTION_UNPROVEN；这不是27个独立测试。旧历史任务及receipt不删除、不改写，不声称既有未知提交效果为零，也不伪造原操作恢复。

实际四个HTTP控制器接Owner状态/no-store，legacy GET补现有view动作；能力默认不可写。页面直接处理函数也核当前权限和能力，冻结请求、确认后再核身份；重复/未知结果不重发，响应taskNo不符不报成功。读取失败清旧数据，身份和乱序结果丢弃；详情替代列表时清加载状态，实时消息仅失效/重载，不授权限。历史状态明确为旧记录报告，事件缺身份/设备不虚构system/web。

仅生产控制台分析段改为实际analytics Owner与当前身份，失败/未载权限不显示旧结果或零看板。统计仍是LEGACY_CREATED_AT/LEGACY_ELAPSED，增加样本数与PhysicalResultsVerified=false；无样本显示“无样本”。旧AverageMinutes数值兼容保留，不声称净效率、物理完成或正式完整统计合同。

## 本地验证与一次集中审查

- `D:\CP6-local-test\wave72\foundation/mobile-red-20261023T174247Z.json`：4项PG真实失败；`mobile-ui-red.json`：4项实际SFC旧行为失败；原失败不删除。
- `mobile-native-20261023T175001Z.json`：18项真实PG通过；`mobile-regression-20261023T175454Z.json`：34项通过，其中2项本轮因保留V1读取兼容性重验，3项既有PG绑定/管理回归，29项既有InMemory。合并去重50项，21真实PG、29InMemory，无跳过；构建相关项目及必要依赖通过。
- PG覆盖三个服务当前Owner、租户/本人/部门祖先/双端仓区、原生绑定/CAS/原结果和跨租户约束、字段与筛选、非MOVE拒绝不损数量、统计样本、27调用零效果、撤权/最终字段来源失败、错误上下文/预跟踪/脏工作/调用方事务、实际Program/login/HTTP。并发用第二连接修改任务Creator收到55P03，当前读结束后可改且下一查询隐藏；未把它写成实际测过事件写竞争。审查仅把该用例名从task_and_event改为task，无行为改动，成功证据复用。
- `mobile-ui-final.json`：23项通过（7移动、3分析、10既有绑定页、3既有API）；审查补加载状态和权限未加载结果丢弃后，`mobile-ui-reviewed.json`重验10移动/分析通过，其余13项输入未变复用，总计仍23独立。实际SFC配API替身，不是现场E2E。
- `mobile-typecheck-reviewed.log`为空且进程exit0，最终Vue类型检查通过。共73独立本地用例，一轮作者完整SPEC/QUALITY已检查当前Owner/事务锁、两端对象范围、旧协议保持、字段/来源关系、命令效果隔离、绑定旧kind、HTTP及页面未知结果；无剩余阻断。完整diff检查通过，无包/依赖/工作流更改。

三个本轮PG自有簇均ownership/stopped存在且无postmaster.pid，数据/日志/TRX保留。没有Actions、既有库迁移、生产部署或全仓回归；迁移仅新隔离库验证。完整S1六kind、Source/Device/Claim、精确旧原操作恢复、最终效果、Native客户端、标签/条码管理、完整WP02-03均未采用。24/375整卡、125目标0交付、247运行NOT_RUN保持；CRM Issue109开放。

## 收尾

原估120–180min＋独立前置3min，实际按Main start/finish计，不重复累计Handoff。候选通过后正常Main/Handoff采用与同任务最终状态、准确分支清理及finish释放，未提前声称席位已释放。七天目标ACTIVE，随后继续D5/D6真实CRM Owner/公开表单基础。W51指定停止data删除曾被自动审批以blocked by policy拒绝，目录保留，不重试或绕过。

## 实际采用与最后收尾

Main [PR452](https://github.com/GTX537/CP6/pull/452) source `b3b38164e1d3732053f8241b86580a5d0f184f13`采用远端main `41cef2ffc77464745c18576e0f60108d59a8ec37`，36任务路径核对。Handoff [PR48](https://github.com/GTX537/CP6.AI-Handoff/pull/48)采用 `f833756a86252cd8fefa9d8f9af90c906904ab6e`，准确任务分支删除、固定树干净detached、finish释放。26个产品路径与已测提交逐blob相同，复用有效成功结果；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue451在最终远端和生命周期核对后关闭。当前约25.85min，原估120–180min，最终以finish为准，独立前置约3min，子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave72-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续D5/D6真实CRM Owner及公开表单基础，完整S1保持未采用。
