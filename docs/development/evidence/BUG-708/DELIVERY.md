# BUG #708：画布展示不得改写流程定义

[Issue #708](https://github.com/GTX537/CP6/issues/708)。基线`c055d2b083a8ce0777255548777f24fe81180c61`，COORD在固定DEV2/`codex/bug-708-canvas-source`独立修复，W278现场保留DEV1；Handoff START准确推送后官方deferred释放DEV2，无新增工作树。

## 根因与改动

修复已发布流程载入画布即变脏并阻断历史/下一草稿的问题；展示初始化不再回写，只把真实图编辑合入原始schema，保留未编辑扩展、null及高精度数值。

Vue Flow挂载/选择产生运行时节点变化，DesignerCanvas深层watch将graphToSchema的展示投影直接当作源内容发出。该投影补齐坐标/连线ID/端点且不含根/连线未知字段，导致只读已发布版本立即被标为dirty；发布历史按钮因此被禁用。现在以载入时图投影识别真实编辑，展示变化不emit；对真正编辑按节点/路径身份将投影差异合入原schema，未编辑字段和省略值保持原样，再沿已有preserveSchema保留数值原词法。载入新源取消上一源待发出的debounce，撤销/重做仍用完整源快照。没有放宽dirty门禁或从只读已发布版本隐式保存。

## 实际验证与集中审查

- 真实Chrome组件红测`D:\CP6-local-test\bug708\red-canvas`：未改动时收到schema回写，扩展消失，期望空emit失败。早期harness加载失败日志另保留，不作产品红测。
- 真实Chrome绿测`D:\CP6-local-test\bug708\green-canvas`：载入、选择零回写；真实鼠标拖动只改变目标位置，其他节点省略坐标、原路径扩展/null与两处高精度词法保持。
- 35项相关本地行为测试通过，`C:\Users\tt\CP6-planning-reports\20261017-foundation\bug708-ui.log`：图编辑差异、连线重排/增删、撤销/重载、现有error-edge/service/subflow/authoring行为。应用类型检查`C:\Users\tt\CP6-planning-reports\20261017-foundation\bug708-typecheck.log`成功。
- 原实际业务复测`D:\CP6-local-test\wave278\permission-consumption-browser-canvas-fixed-20261102T230341Z\browser-chain`：真实Vue→Main Program→PostgreSQL 18，登录后读取已发布FLOW历史、创建下一草稿、保存、精确依赖预览及真实发布已成功。此证据只用于BUG708对应步骤，W278后续目录、委派、业务恢复仍由其完整任务报告核销；不把整个W278称为通过。模拟租户、角色、输入定义；内部Owner/HTTP不mock。

作者一次SPEC/QUALITY集中审查：图投影初始化、选中、拖动、属性编辑、节点/边身份映射、删除与重排、完整源历史、未知扩展/数值保持；没有改动后端授权、版本、事务或契约。代码候选先接入W278测试分支进行实际链验证，Main/Handoff的其他未提交内容未进入BUG分支。

## 采用边界

普通验证全本地，无Actions触发/取消、工作流调整、生产部署或既有数据迁移。24h目标继续IN_PROGRESS，原375及56局部卡统计不因本BUG改变。正常PR合并并核对远端main后关闭Issue；准确分支清理、工作区状态与官方finish释放见`C:\Users\tt\CP6-planning-reports\20261017-foundation\bug708-final-verification.json`。随后DEV2按官方bind恢复Handoff并resume其已保存分支。
