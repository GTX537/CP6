# M-OA/WF T1 任务简报：权限键清单(端点×权限键真相源)

## 背景与位置
M-OA/WF 横切接线波首任务(branch=feat/m-oawf-crosscutting)。第四个同型波(M-WMS/M-ERP/M-MES 已上线)。清单质量决定全波,键错一字全链 403。

## 必读(按顺序)
1. `docs/00-横切接线规范.md`
2. 同型先例: `docs/seeds/mes-permission-keys.md`(最新最严的一版,结构 §一~§七 照抄)
3. 扫描对象: `CP6.WebApi/Controllers/Oa/`(11 控制器: ApproverMap/Catalog/Delegate/Designer/Draft/FlowAdmin/Forecast/Inbox/Notification/Pref/Query)+ `CP6.WebApi/Controllers/Wf/`(5 控制器: AdvancedFlow/Approval/Flow/Form/Task)

## 需求
1. **全量扫描**: 16 控制器全部非 GET 端点逐方法列表,双向验证计数闭环。
2. **逐端点定键**: 键前缀按锚定菜单归属定(OA 菜单锚→`oa-*`,WF 菜单锚→`wf-*`,连字符);先查 Program.cs 中 OA/WF 菜单段(500 段已见 501/502)的 MenuId/RoutePath/MenuKey 现状,给出每键锚定菜单候选;RoutePath 形态与回填时序命门照 M-ERP/M-MES 先例排查并在 §六 标注。
3. **高危键独立**(计划点名: 流程定义保存/发布[Designer/Flow]、委托授予[Delegate]、FlowAdmin 干预;实扫另有不可逆操作一并拆分),逐个佐证。
4. **只读 POST 豁免**: 查询/预测/通知已读类逐条读 Service 证得无写库才豁免(注意「标记已读」是写不是读)。**个人偏好类写端点(Pref)与收件箱操作(Inbox 审批动作)不豁免**——审批动作是全系统最高危写路径之一。
5. **⚠特别盘点(供用户裁决,不做决定)**: `/wf/form-designer`、`/wf/flow-designer` 两条路由的现状——前端路由在不在?菜单行在不在?对应控制器/页面是新栈(SSO 后)还是旧栈遗留?把证据列进 §六「用户裁决点」,退役/收编两案的影响面各写一句。
6. **交付**: `docs/seeds/oawf-permission-keys.md`(§一~§七 照 MES 版结构,计数自洽)。纯文档零代码。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\oawf-t1-report.md`。回复只返回: 状态、commit sha、一行计数摘要(N控制器/M真写/K豁免/J键/高危数)、concerns、报告路径(15 行内)。单 commit(docs)即 push。