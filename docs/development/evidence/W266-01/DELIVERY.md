# W266-01 / 第八期三天共享菜单树

第八期三天W266共享菜单树V1：PG原生版本与CAS、全部租户角色引用预览、准确原命令和强审计同事务；当前平台管理员加menu:edit的真实会话HTTP，实际拖拽页面冲突差异与未知恢复。

[三天计划](../../planning/20261102-three-day-cycle8.md)、[V1合同](../../menu-tree-v1.md)、[原59局部盘点](../../planning/20261102-progress-cycle6.md)。Main基线`2acdf8199928d31f462f6e32f824dd6c0c28e493`，COORD单人，固定DEV1/Main与DEV2/Handoff。

## 实施与验证

D1原生树版本/并发CAS、partial位置更新、全租户保守引用预览、强审计与不可变原件；D2当前平台管理员和动作权限、完整实际登录会话/NoStore/CSRF/严格输入；D3实际页面拖拽、全局确认、412差异草稿、unknown刷新后原样恢复与独立读取。

24项相关本地后端测试通过/0跳过：7项新增PG18/真实Main会话HTTP、8项原菜单/IAM动作回归、9项当前权限PG读取回归；业务数据模拟。29项MenuView行为测试、Vue类型检查退出0、实际Chrome/Element Plus原生拖拽与API夹具PASS。首次编译、NUL审计jsonb、测试约束、夹具目录冲突、旧UI夹具及类型断言失败均保留并修正；仅定向复跑。无Actions或部署。

汇总D:/CP6-local-test/wave266/foundation/final-validation.json逐项指向真实结果。7项新增覆盖partial保留、全部tenant角色计数、并发CAS、结构拒绝、原生ABA、删除后历史恢复、异义/他人拒绝、不可变历史、审计和最终会话回滚、提交丢答恢复一次、原生写锁等待/迁移保留/TRUNCATE推进、锁等待中动作撤权与实际HTTP平台撤权、真实cookie/CSRF/伪造字段/原件/注销。既有MenuController与IAM菜单动作8项、当前权限9项按独立夹具运行。

29项页面行为与[Chrome复现](check-page.mjs)覆盖真实Element Plus原生拖拽、预览确认、大版本精度、412保留草稿/只读差异、未知刷新、404/403不清原请求、原样重试、写入确认但列表失败恢复、同名新会话/晚答、存储失败不发请求、新草稿不覆盖。Chrome使用显式API/身份模拟，真实PG/Main HTTP另由后端用例验证，不称真实端到端整卡验收。截图/日志保存在本地证据目录；缺语言接口的fixture显示原语言键与注入故障toast，未当产品翻译完成。

## 集中审查与边界

一次作者SPEC/QUALITY审查覆盖完整任务diff、PG语义/迁移锁、原件恢复/当前授权/原子性、菜单与动作权限分离、前端身份/冲突/恢复和375原状态。审查修正NUL键进入jsonb的问题；原键保持bytea，审计用关联摘要，不缩小合同。未新增包、构建或工作流配置。

W266仅闭合共享树排序/层级V1及当前浏览器读取恢复。WP02-18仍余其他菜单字段/路由注册、个性配置、KPI Owner定义与scoped查询/授权缓存、全部相关导航传播及20条原业务AC整体验收；旧菜单/删除/PUT tree兼容入口仍未全面采用CAS/原命令和全局政策，不因V1交付宣称旧入口治理完成。命令历史保留/擦除归治理任务。WP02-03原整卡前置地位不变，本期直接复用已交付IAM共同门/当前权限。 原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部仍3关闭/56剩余，完整125目标/247正式验收状态不变。前七期/W249/七天一期不重做。

## 集成收尾

提交前为IMPLEMENTED_VALIDATED_PENDING_REMOTE；正常Main/Handoff采用后，wave266-final-verification.json核对完整远端树、准确分支删除、官方finish、固定席位干净释放及主目录用户文件保留。此文件不预写远端SHA。没有Actions/取消项/触发调整、部署或既有数据库迁移。
