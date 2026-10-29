# W216 — 库位本域封锁与指定解除

## 结果

真实库位可通过独立block/unblock/query权限保存本域封锁，记录绑定准确Block、当前操作者和控制前驱的解除决定，并应用指定解除。原生IsBlocked镜像、控制修订、不可变决定、审计和Main原结果在同一PostgreSQL事务；并发过期前驱、撤权、跨租户/范围和无原结果写入均受阻。实际API及页面接入真实库位列表、草稿和规则页。

解除仅移除本项本域限制。多封锁、其他来源贡献及初始化隔离保留；当前操作者决定不冒充OA批准、质量解除或所有Owner已停止。本轮没有启用Location或调拨，没有库存数量写入。完整D2仍未关闭。

## 验证与审查

10 actual PostgreSQL/Program HTTP (8local-control +2initialization);28Vue/type/build PASS;0skip。10项相关实际PostgreSQL18/ProgramHTTP：8项本域控制（多封锁与准确解除、不可变原结果和同键冲突、其他来源重复true/原限制/隔离保留、准确当前决定与独立动作权限、DB原件和镜像保护、未知原键封闭、最终撤权全回滚、并发版本竞争、实际HTTP封闭DTO/If-Match/no-store），另复查2项既有Location初始化/旧写入口隔离。9项新页面行为及19项既有规则页测试通过，类型检查和构建通过。

来源 `D:/CP6-local-test/wave216/foundation/final-validation.json`。首轮7项中6通过，1项测试在新授权后复用旧创建AuthorityVersion，被CREATION_AUTHORITY_CHANGED拒绝；刷新测试的真实创建上下文后定向通过。集中审查补上控制头原决定关联约束，相关10项中9项通过，新原件约束测试在预期拒绝提交后又回滚已终止事务，清理报NpgsqlTransaction已完成；改为释放该终止事务后仅复测此1项，合计10项实际行为通过。产品代码未再变化，9项复用真实成功来源。前端先26通过，随后补强分页后的当前解除决定入口和响应身份校验，最终28通过。原失败日志保留，不冒充首次全通过。相关既有Owner表锁/最终授权证据沿用，没有无关全仓测试、Actions或既有环境迁移。

COORD一次任务级SPEC/QUALITY审查覆盖权限/租户/原件/事务/并发/其他来源限制保留、初始化兼容、当前解除依据、原键恢复、UI身份和迟到响应。只采用相关已验证范围。

## 生命周期与剩余

W215仅归档一次，有限wave215_stock_assessment记录按远端finish证据关闭；W216新增WP10-01/WP10-10有限本域控制记录。125目标、247验收、375任务状态、D1/D4之外原阶段出口不变。

Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/192，源`cef012718fb3ab0afe52db0056fd3534429ccc35`，通信目标`dfc6402306d33819464bb2e2c3b260dcd13bc53a`，已合并并finish。Main正常PR/官方finish在本提交后进行；最终证据为外部wave216-final-verification.json，下一真实准入统一回写。本报告不提前宣称自身合并。

登记至报告25.55分钟，初估60–100；最终时间按Main START到finish，Handoff不重复相加。固定DEV-1/DEV-2复用、主目录用户文件和依赖缓存保留。

剩余：MASTER完整正式定义/资格、真实源与容量占用、全部效果Owner保护、08E、Transfer/Stock/REP/RMA；原D3/D5/D6/D7继续。本域控制不能替代任何缺件。

[范围](../../dispatch/20261029-wave216/README.md) · [本域控制合同](../../location-operational-control-v1.md)
