# 第九期三天：菜单展示配置版本化管理

Goal：菜单名称、图标、Enable显示状态采用当前全局管理授权、共享树CAS、强审计和原命令恢复。COORD本对话单人，writing-plans/executing-plans；遵从项目精简验证与固定席位规则。基线970ef3bbb2e05fb96ebd512359767f95f60874d2，W266只归档不重跑其整期。

Architecture：复用W266共享树版本、IAM共同门、不可变commands和会话适配，不另造版本/表/授权层。新增严格MenuDisplayCommandV1，与旧树命令共享操作键空间但不同请求字节，不改变旧DTO序列化。展示状态不等于动作权限撤销，不删除角色映射。技术.NET8/EF Core/PostgreSQL18/Vue3。

每阶段6小时核心+2小时缓冲，为工作量估计，不需按日历等待。三阶段同任务分支，准备交付时一次集中SPEC/QUALITY审查。

## D1：Owner与原子行为

- [x] 修改CP6.Core/Services/Sys/MenuTreeAdministrationV1.cs为partial，将现有引用统计和审计/原件完成提取为共同私有方法；保持旧命令Bytes及历史恢复协议。新增MenuDisplayCommandV1.cs与MenuTreeAdministrationV1.Display.cs，接受OperationRef/ExpectedRevision/MenuId/MenuName/Icon/Enable/Reason；名称非空白≤100、图标null或≤100，文本合法Unicode无NUL且原样保留，不trim/缩短。
- [x] PreviewDisplayAsync在当前门内验版本/节点、计算变化节点和全tenant保守引用；ExecuteDisplayAsync先恢复同键原件，再核CAS/输入，保存三个展示字段与强审计/原件同事务。不同命令同键409，旧树与展示互相CAS，目标删除后恢复历史不重建。
- [x] 新增CP6.Tests/MenuDisplayV1PostgreSqlTests.cs，真实PG覆盖文本精度/字段保留、原生写/旧树CAS、异义及跨命令、原件恢复、审计/最终会话回滚、未知提交、全tenant引用和当前动作撤权；复用W266测试夹具及已有真实IAM门。

## D2：实际HTTP

- [x] 在CP6.WebApi/Controllers/Sys/MenuTreeAdministrationControllerV1.cs增加POST display/preview、display/commands，复用当前GET/commands/read。严格JSON拒绝RoutePath/MenuKey/ActorId，不扩写原字段。实际Main Program登录cookie验证NoStore/CSRF、普通tenant拒绝、原件权限、停用保留角色授权与启用恢复。

## D3：页面与交付

- [x] cp6.web/src/api/sys/menu.ts增加展示DTO/API；新增MenuDisplayPanel.vue并接入MenuView.vue当前选中节点。明确名称/图标/显示状态独立保存；先读当前版本和预览全局影响，412留草稿并可重读当前值，确认后重新提交；存储失败不发送。
- [x] 原请求按当前身份epoch保存sessionStorage，刷新可查询/原样重试，后续404/403不消除未知；选中节点变化和身份变化丢弃晚答，确认后刷新当前列表/导航，与树/旧节点编辑互斥。已有旧接口全面改造留待对应窗口。
- [x] 相关PG/实际HTTP测试、Vue行为/类型检查和Chrome实际页面夹具；一次任务审查，报告/台账/四状态同步，正常Main/Handoff采用，准确分支清理、官方finish和席位释放。

验证命令使用本地run_wave267_main.py的Menu_display_v1及受影响Menu_tree_v1筛选，Vue运行MenuDisplayPanel/MenuView相关测试和vue-tsc。验收均可模拟数据；PG和实际HTTP是真执行，页面API夹具明确标注。无Actions、既有环境迁移或部署。

原375状态和59局部卡真实边界保留；WP02-18仍余RoutePath/MenuKey/路由注册、个性配置、KPI/scoped授权缓存、全用户导航传播、旧入口全面采用与原20AC整体。不能以本期替代整卡完成。

实现与相关验证完成，最后一项状态同步、远端采用、清理/finish由wave267-final-verification.json核销。提交前VERIFYING。

W268依wave267-final-verification.json核销W267实际采用/清理/finish；历史24h估算失准，实际约19分钟，本轮改为24h有效投入滚动预测。
