# W125 / BUG-537 主体导出归属修复交付

[Issue537](https://github.com/GTX537/CP6/issues/537)记录活动缺陷。原主体导出忽略全局租户过滤后仅按UserId查询安全日志，并仅按UserName查询操作日志。隔离PostgreSQL在主线0723de10a3aa99a89325436560b50c72a88270ca复现：期望1条目标安全事件/0条无法证明归属的操作记录，实际2/2，跨租户/同名数据进入结果。

修复按所选用户的目标TenantId+稳定UserId过滤安全日志，历史改名仍可归属；平台操作者所在租户不替换目标租户。旧Sys_OperLog没有稳定主体键，名称与独立ImpersonatorId均不构成归属证据，故保持兼容空数组并明确UNKNOWN_SCOPE，不能解释为该主体无操作数据。版本化scope说明有限覆盖、独立读取时间和既有字段过滤边界，删除旧完整性暗示。页面提供组件本地五语范围提示，已有全局词条不变。控制器现有Authorize/RequirePlatformAdmin和擦除路径未改，实际数据库和日志不删除。

23项唯一相关本地检查通过：1项真实PostgreSQL主体归属回归、2项原主体导出检查、20项现有平台权限过滤检查，必需0跳过。Vue SFC/TypeScript编译与五语SSR通过（UI壳组件替身，不称浏览器或HTTP验证）。

原失败：`D:/CP6-local-test/wave125/foundation/baseline-20261025T020402Z.json`；修复23项：`fixed-20261025T020630Z.json`。审查去掉新加在历史通用夹具中的PostgreSQL断言，避免破坏旧选择方式；只重跑本项原生回归，`reviewed-native-20261025T021016Z.json`通过1项，其余22项复用原成功，不重复计数。唯一汇总`D:/CP6-local-test/wave125/foundation/final-validation.json`及TRX保留源码blob与逐例来源。当前只执行PostgreSQL，无新增SQL Server要求。

语言全仓检查实际失败1054个既存缺项；扫描器和生成快照未变，本轮全部被扫描全局字面量输入与主线相同，`i18n-existing-gaps.json/log`保存比较和失败，不称全仓通过。新提示为组件本地五语，直接Vue编译和SSR验证，不伪造数据库语言快照。初始验证工具尝试引用缓存未安装的esbuild失败，改用缓存现有TypeScript编译后通过；无安装/包更改。

完整SYS-PRIV Owner目录/审批/共同权限门、嵌套字段分类、FILE operation/当前下载、人类撤回、CRM导出及原生outbox发布仍待实际采用。 当前读取非一致快照，scope已明确；字段过滤仍限原标量规则，不把嵌套字符串认证/分类称为已证。没有既有环境迁移、真实数据导出/删除、部署、Actions或分支保护改动。

自有PG55451已按身份及零活跃连接停止，证据数据、缓存和用户资料保留。Main/Handoff最终合并、准确分支删除和官方finish待本轮正常收尾；以最终`wave125-final-verification.json`核对，下轮台账统一核销。原估60–100分钟；截至本交付准备11.37分钟，最终按Main START到最后两仓finish计一次，不重复加算。

24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY不变。D6PARTIALLY_ADOPTED，七天ACTIVE，本限定缺陷不等于完整WP02-15或隐私业务验收。
