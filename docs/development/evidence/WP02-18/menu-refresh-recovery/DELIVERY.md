# WP02-18 · 第十三轮 D 菜单刷新恢复

关联 [Issue #243](https://github.com/GTX537/CP6/issues/243)。仅交付已准入的菜单客户端恢复窗口，完整 WP02-18 仍为 PLANNED / NOT_CHECKED。分支 `codex/bug-243-menu-refresh-20261017`，工作树 `D:/CP6-worktrees/wave13-d-menu-refresh-20261017`；开工和交付前 fetch 均核对 HEAD 基线、local main、origin/main 为 `4704592f52172c941e1cb95bdeb3c73f0dd179e8`。候选提交由 Handoff DELIVERY 固定引用，避免本报告自引用。

真实 START 已发布并远端回读：[6835a38231ef67699d21df52730b7f4dab7ce0ee](https://github.com/GTX537/CP6.AI-Handoff/blob/6835a38231ef67699d21df52730b7f4dab7ce0ee/handoffs/CP6-DEV-20261012/tasks/WP02-18/messages/20261017T041447Z-wave13-start/MESSAGE.md)。执行者为用户新开启的第十三轮 D 对话；按已确认窗口连续实施，没有等待重复 ACK。

## 改动与原步骤复测

- 菜单创建、节点修改、树结构保存及删除分别确认写请求结果，确认后独立发起列表和当前浏览器导航读取。一组读取失败或仍在等待不阻断另一组。页面区分“写入已确认成功”、列表待刷新、导航待刷新，提供两个独立恢复入口。
- 恢复回调只调用现有 `menuApi.getAll` / `authApi.profile` GET，不持有或重放原菜单 POST/PUT/DELETE。写入失败/超时只显示未确认结果，保留草稿，不自动补发写请求，也不因为后来 GET 得到相同值而改称原写入成功。原公共 HTTP 客户端的 401 会话刷新机制未改；本窗口没有声称取消其认证刷新 POST，也没有改变它的全局重试策略。
- 列表手动恢复沿用放弃树结构/节点编辑的确认；取消或读取失败不清草稿。请求发出后编辑、选中节点或新建草稿发生变化，则丢弃该列表结果并继续显示待刷新。写入期间的新草稿同样保留。已确认节点的本地副本用于避免读取失败后“放弃更改”退回写入前内容；后续列表读取仍是权威来源。
- 两组读取各自使用请求代次；新写入使旧读取失效。卸载、storage/focus/既有菜单事件以及响应提交前的身份核对阻止迟到结果回写。只观察现有登录标志、用户名、菜单、平台标志及 impersonation 信号；观察到变化后暂停当前编辑会话，要求重新打开页面。自身合法导航发布更新快照，不误伤并发列表读取。
- 导航只接受包含合法菜单数组的画像，按现有 `id`/名称/可空路径和图标/父级/排序字段校验，拒绝重复 ID 和循环父级；缺失或畸形集合不转成空菜单。合法空集合可发布。存储成功后才发既有 `cp6-menu-updated`，Layout 协议不改。

原步骤使用真实 Vue 页面与 Chrome、隔离 API 夹具复现：原版成功 PUT 后列表 GET 503 时没有 profile GET；另一次成功 PUT 后 profile GET 503 时没有恢复按钮。修复后相同步骤均通过：列表失败仍调用 profile，当前 Layout 侧栏实际刷新；独立导航重试只增加一条 profile GET，草稿保持不变。两次显式保存总共只有两条菜单 PUT。

## 实际验证、失败记录与审查

2026-10-17 本地 Windows 执行；全部日志和截图保留在 `D:/CP6-local-test/WP02-18/menu-refresh-20261017/`。没有更新 package/lock；复用本机已有依赖，实际版本 Vite 8.1.5、Vitest 4.1.9、Vue 3.5.32、TypeScript 6.0.2、vue-tsc 3.2.6、Playwright 1.60.0；不是新执行的 lockfile clean-install 验证。

- `npm test -- src/views/pms/MenuView.spec.ts`：最终 **23 通过、0 失败、0 跳过，退出码 0**，见 `tests-final.log`。覆盖单组/双组读取失败及相互独立、四种菜单写入口的恢复边界、失败写入不冒称成功、取消/失败/并发新建草稿、树写入期间新草稿、读取代次、旧导航成功/失败、卸载/身份变化、非法画像/合法空数组以及存储失败。
- 首轮原实现回归为 17 失败/1 通过，见 `tests-red.log`，明确捕获 #243 和旧响应问题。首轮修复后 18 通过，见 `tests-green-01.log`。集中自查增加树写入竞态覆盖，`tests-review-red.log` 为 1 失败/22 通过；修正只清除与已提交树相同的草稿后，运行上述最终相关测试。不隐去失败或将其计为成功。
- `npm run type-check`：`type-check-01.log` 和最终 `type-check-final.log` 均退出码 0；最终执行对应交付源码。没有执行前端全量测试、生产构建、Dashboard 旧测试或后端测试。
- [check-page.mjs](check-page.mjs)：`node docs/development/evidence/WP02-18/menu-refresh-recovery/check-page.mjs --baseline` 用 Vite 局部加载插件只读加载固定基线 MenuView，保持当前工作树不回退；`page-baseline-02.log` 记录原步骤成功复现。去掉 `--baseline` 验证当前实现，最终 `page-fixed-03.log` **退出码 0、pageErrors/Vue error 均为 0**。浏览器验证原两条故障、GET 恢复、Layout 事件消费和放弃确认取消。人工查看 `menu-navigation-pending.png`；另有 `menu-baseline.png`、`menu-recovery.png`。
- 浏览器早期失败保留：`page-baseline.log` 的权限夹具误用裸数组，使保存按钮不可见；已按既有 rolePerm API 改为 data 信封。`page-fixed-01.log` 使用了不存在的 `.sidebar` 定位，改用实际 `.layout-sidebar-shell`；`page-fixed-02.log` 假定 Element 默认按钮为中文，改为兼容实际 Cancel 的可见按钮定位。均为脚本修正，未跳过验证。原版列表错误有全局 Vue 错误日志，符合复现；两版共同存在语言包及既有 dropdown 指令警告，不是本次新增的未捕获页面错误。
- 脚本仅监听本任务 `127.0.0.1:5178`，拦截全部 API 并拒绝外部请求；finally 关闭自身浏览器和 Vite，未连接业务数据库、停止别人的进程或覆盖数据。夹具不是生产验收。
- 执行者已集中核对一次任务完整 diff、SPEC/QUALITY 与证据范围，修正上述树草稿边界后复查影响范围，无已知阻断项。COORD 的统一交付审查、状态同步、串行合并及远端 main 核对尚待进行；不把作者自查称为 COORD 已审查。

## 推送检查与剩余范围

提交范围仅 MenuView、局部 recovery helper、组件 spec、本报告和页面检查脚本，均在登记白名单内。Git 用单次 `-c core.longpaths=true` 检查时没有历史文件缺失；未修复或纳入默认 Windows Git 报告的长路径假删除。没有修改共享台账、四份状态、Layout、router/store、API/后端、依赖或其他窗口文件。

推送前候选/远端 main 的 `.github/workflows` tree 均为 `3355235636fd45116f5445fca32fc65e49f13003`，与 [上次交付](../dashboard-states/DELIVERY.md) 已分析内容相同；复用其 12 个 workflow_dispatch、1 个只匹配 `v*.*.*` Tag 的结论。本次没有工作流/依赖变化，不推 Tag，不创建或修改 PR。任务分支 Actions 列表为空，没有需取消运行；未触发/重跑 Actions、修改触发器或分支保护。Handoff 私有、Actions disabled、资料分支无 workflow。

服务端不可观察的身份/权限变化、原菜单命令幂等性与 CAS、权限撤销、全租户缓存、scoped KPI、数据库与真实业务验收均不在本窗口。没有执行 SQL Server、PostgreSQL、跨仓宿主、正式制品发布、迁移或部署验证。已有 Dashboard 交付仅作为前置说明引用，没有重跑或提升其验收。Issue #243 保持 Open，待 COORD 正常合并、核对远端 main 并按环境验收范围决定关闭；本执行者不合并 main、不修改共享状态、不关闭 Issue。
