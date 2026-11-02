# W277-01 封闭委派与显式业务重新授权

W277：TASK-R2封闭范围委派、当前双身份任务读写，以及迭代Owner独立业务委派、BLOCKED显式重新授权和连续原事实已实现。

[24小时持续计划](../../planning/20261102-codex-24h-oa.md)。基线`69909b2ec52a56917a41770279072e3384bcd97b`；COORD单人复用DEV1/DEV2，无新增工作树。依据原TASK-R2 §5/5.1、APP接受合同§17以及已有真实迭代Owner的业务行为。

## 实际交付

TASK授权本人创建、修订、撤销：grant身份固定、revision和version不可变、当前指针CAS，UTC半开区间[from,to)、封闭actionKinds/flowKeys/bizTypes、维度AND/维内OR及Ordinal比较。TASK_READ是必需动作，至少一个非空范围；选择稳定的单一完整grant。本人/同租户人员、禁止自委派和重叠转授链在授权Owner校验；旧空scope和本地时间授权不自动升级。当前授权写入共WF_AUTH根锁并推进撤销epoch，原命令查询/关闭无需重新获得写权限。PG不可变历史和延迟完整性约束阻止部分授权成功。

原生APP/普通FORM任务与信箱采用实际登录人/原职责人的当前授权交集。授权范围过滤先于搜索、计数、分页；只读、同意、拒绝分别匹配当前grant动作。APP业务类型从冻结提交读取，不能依赖旧instance可空字段。FORM字段取双方交集，APP实际业务Owner分别核当前资源披露；必要SOD、当前职责和最终权限继续保留。首次已读保存准确actual/principal/grant/revision；后续读取不重写首次事实，代理办理不替换assignee，已记录票据和原命令历史不依赖仍然有效的grant。

迭代Owner新增独立精确轮次业务授权，动作仅review-withdraw/review-apply。新的review-delegate能力只登记菜单动作，没有自动授给现有角色。授出人必须拥有对应业务职责和当前权限；实际受托人使用时仍需本人的业务权限、资源/字段可读以及委托人的当前完整资格。TASK授权不能作为业务撤回/应用资格。授权新增、修订、撤销与真实Main操作journal、不可变版本和WF_AUTH epoch同事务提交；原授权操作查询/关闭保留原意图。

业务撤回仍接收封闭两字段UserWithdrawRequest。Owner自行冻结完整actor、真实grant/revision、业务前后RV和完整撤回事实，沿原oa.app.withdraw/2.0运输；旧payload和原事实不改写。BLOCKED后只能由用户显式提交新的授权命令，准确关联旧命令并CAS当前控制版本；已撤回业务内容保持IN_DESIGN，新授权产生真实Owner历史/控制版本，不假造重新提交或第二次业务撤回。Workflow已有终态时拒绝新裁决，原命令仍返回原BLOCKED；旧grant续期不会偷偷替换历史授权。业务应用BLOCKED允许显式新actor/new-grant意图，准确引用前驱receipt并追加连续回执；普通重试仍要求原操作者，worker不自动换人。

页面新增当前任务授权管理、本人/受托身份选择、原命令恢复与撤权内容清理；旧版任务委派明确区分。迭代评审页可管理本轮业务授权，并分别显式重新授权撤回/应用。未确认写入只保存安全恢复标识，恢复前禁止新意图；身份/轮次变化和迟到结果不能回填旧内容。UTC控件可正常显示毫秒，未修改时间时保留服务器原值的完整精度。

## 使用入口与合同

- TASK：`/api/oa/v2/delegations`，GET本人/收到列表、`received`可代理人员、`targets?search=`同租户候选、`{id}/history`历史；POST创建、`{id}/revise`、`{id}/revoke`使用已有WFC_JSON命令信封和准确expectedVersion；`commands/{operation}/{resource}/{key}`查询原结果，`/close`显式关闭。
- 迭代：`/api/engineering-iterations/owner-v1/{id}/review-business-grants`管理当前业务授权；`review-submissions/{submission}/business-grants`读取本轮；`review-business-grant-operations/{operation}`及`/close`原意图恢复。UTC要求明确Z或+00:00；缺少必要nullable/CAS/时间/启用字段立即拒绝。
- 显式再授权：`review-application/reauthorize`使用准确If-Match和expectedReceiptId；`review-submissions/{submission}/withdrawals/{previous}/reauthorize`使用当前控制RV和原两字段body。代理身份由明确X-Acting-As给出，实际actor始终来自认证，不接受客户端提供grant事实。

## 验证与一次集中审查

本地33项唯一PostgreSQL18行为通过，0跳过；35项Vue组件/恢复行为通过，应用vue-tsc通过。实际Main登录/CSRF/HTTP、两连接授权撤销及撤回/应用竞争、延迟提交失败回滚均有定向证据。模拟业务数据；内部Owner、事务、原生Workflow和历史原件真实执行。

逐行为最后成功来源：`D:/CP6-local-test/wave277/foundation/final-validation.json`；早期失败原样保留。修复了trigger类型就绪判断、实际授权动作白名单、Owner授权写入自身推进epoch后的租约顺序、最终观察回调集合、撤回再授权控制状态规则和UTC输入控件精度；对应回归通过。未更改既有封闭业务应用回执wire，grant/onBehalf证据保存在真实Owner审计中。

作者一次任务级SPEC/QUALITY审查覆盖全部分支diff、新迁移和前后端：当前授权而非历史参与、真实双身份/范围/SOD/字段、授权与业务Owner边界、事务/CAS/原命令及不可变事实、UI未知结果与身份切换。没有以内部契约为由删除安全业务边界，也没有新增重复资格包装。新迁移仅在隔离PG18测试库运行；没有执行既有环境迁移、生产部署或Actions。已停止本窗测试PG/API。浏览器到真实PG完整验收留W278，本窗HTTP和组件测试不冒称该结果。

## 真实剩余与生命周期

本窗采用原WP12-08、WP12-10、WP12-19的对应局部范围；START把WP12-09同时列入的归属已纠正，退回/转交/加签/无审批人策略未在本窗实现。SENDBACK/TRANSFER可作为封闭授权动作登记，但本窗不声称已有相应原生办理入口。原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部仍3闭环/56余项，125/247正式状态保持。W278接续浏览器到真实PG完整链，W279原AC核对；历史比较/目录收藏下载、完整SFS patch/三版本、退回转交/无审批人、日历与期限Worker仍在后续窗口。其他业务Owner的委派撤回/应用未采用，不把迭代采用推广为全Owner完成。

24h总目标持续IN_PROGRESS；本窗不是三天完成。最终远端采用、准确任务分支本地/远端清理、工作区干净和官方finish以`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave277-final-verification.json`核销。固定席位与可复用缓存保留，主目录用户资料不动。
