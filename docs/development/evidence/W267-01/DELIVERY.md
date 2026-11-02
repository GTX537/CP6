# W267-01 / 第九期三天菜单展示配置

第九期三天W267菜单展示配置V1：名称/图标/Enable共享树CAS、全tenant引用预览、强审计/原件同事务、当前平台及menu:edit实际HTTP、集成页面冲突与未知原请求恢复。

[计划](../../planning/20261102-three-day-cycle9.md)、[合同](../../menu-display-v1.md)、[原59局部盘点](../../planning/20261102-progress-cycle6.md)。基线`970ef3bbb2e05fb96ebd512359767f95f60874d2`，COORD单人，固定DEV1/Main与DEV2/Handoff。

## 实施与验证

D1新展示DTO和Owner、共用引用统计/完成事务，旧树序列化与原件语义不变；D2实际平台/动作授权会话和严格HTTP；D3选中菜单展示面板、全局预览确认、412草稿/重读、未知刷新/原样恢复、身份/选择晚答隔离及与旧编辑互斥。

12项真实PG18/实际Main会话HTTP通过/0跳过（5项展示配置、7项受影响菜单树回归）；34项Vue行为通过，最终展示面板5项定向复查通过；Vue类型检查退出0，实际Chrome集成页面API夹具PASS。业务数据模拟，PG/HTTP真实执行。无Actions、取消项、工作流调整、既有环境迁移或部署。

汇总D:/CP6-local-test/wave267/foundation/final-validation.json。5项新增测试覆盖原样文本/最大长度/null图标、未提交字段保留、跨tenant引用、跨命令同键、并发CAS/no-op/原生ABA、目标删除后恢复不重建、非法输入无写、审计和最终会话回滚、提交丢答一次恢复、当前撤权、真实cookie/CSRF/严格字段、隐藏后profile过滤/角色关联保留/恢复显示。7项旧树测试确认共同方法提取未破坏原协议。

页面34项（29原MenuView+5展示面板）；最后清除已确认旧current值后仅复查展示5项并运行[Chrome集成脚本](check-page.mjs)。实际Chrome/Main UI配显式API/身份夹具，真实PG/Main HTTP另行验证，不称全业务E2E或整卡20AC完成。截图中的语言键/故障toast来自空语言及故障夹具；不称完成翻译。

## 集中审查与剩余范围

一次作者SPEC/QUALITY审查：白名单/text忠实、当前权限/共同门、跨命令版本及历史恢复、强审计原子性、导航显示不改变动作授权、页面互斥与身份/选中节点晚答、375状态及引用。直接复用W266迁移/会话权限边界；本期无新数据库迁移、包或构建配置。

W266排序层级和W267名称/图标/Enable展示配置已限定交付。WP02-18仍余RoutePath/MenuKey及路由/权限命名空间治理、个性配置、KPI Owner与scoped授权缓存、全部用户导航传播、旧接口/旧节点编辑全面CAS与原命令采用及原20AC整体；隐藏菜单不是动作撤权。命令历史保留/擦除归治理任务，原WP02-03整卡前置地位不变。 原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3关闭/56余项；125完整目标和247正式验收状态不变。前八期/W249/七天一期不重做。

## 集成收尾

提交前IMPLEMENTED_VALIDATED_PENDING_REMOTE；正常Main/Handoff采用后wave267-final-verification.json核对远端完整树、准确分支删除、官方finish、席位干净释放、主目录用户文件保留。报告不预写未来SHA。
