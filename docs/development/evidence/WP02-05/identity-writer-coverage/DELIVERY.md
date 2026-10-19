# W22-01 Main身份写者覆盖 / BUG304交付

## 结论与适用范围

用户要求本对话单独持续补公共基础。基线 `0b400c8670dbba7a23c6d8f57d0d28e7f9669d43`（规划PR303后），分支 `codex/bug-304-identity-menu-fanout-20261019`。本窗口完成实际写者盘点、P0 [#304](https://github.com/GTX537/CP6/issues/304)修正、真实服务/原生PG/普通身份HTTP完整采集证据与一次作者SPEC/QUALITY。**本地可合并：Yes**；Issue待正常合并及远端核对，当前Open。不是完整WP02-05/全IAM覆盖/CRM授权发布/真实业务完成。

## 根因与修正

事务A新增角色操作权并生成旧菜单键快照，保持未提交；事务B更改全局菜单键。原ExpandBeforeSave查询看不到A的新关联，B保存菜单后未刷新该角色；A随后提交，快照永久保留旧键。原生RED预期 `crm-w22-new`，实际 `crm-w22-old`。

唯一产品改动为 `IdentityChangeCapture.ExpandBeforeSaveAsync` 的PostgreSQL分支：在查询受影响角色、业务保存之前，对 `public."Sys_RoleAction"` 取得 `SHARE ROW EXCLUSIVE` 事务锁。角色操作权增删改本身持有 `ROW EXCLUSIVE`，两者互斥；锁等待结束后再按实际已提交关联展开角色，随后现有快照/版本/Outbox/审计管线保持同事务。无需新增表、迁移、协议、包或SQL Server实现。

菜单fanout仅接受ReadCommitted（以及PG等价ReadUncommitted）。调用方RepeatableRead/Serializable可能已固定旧快照，等待锁无法刷新，明确抛 `C02_MENU_FANOUT_REQUIRES_READ_COMMITTED`，由现有保存点回滚；调用方仍持有可用事务。此限制只针对菜单身份变更，不改变bootstrap原有Serializable或普通用户/部门/权限写者隔离策略。没有自动重试或降低隔离级别。

这是全局菜单的正确性边界：菜单变更事务期间会阻塞角色操作权增删改，包含其他租户；普通读取不因此被此锁模式阻塞。生产吞吐/最长事务尚未压测。调用方应缩短菜单事务，复杂多次SaveChanges可能发生锁等待/超时，按原失败合同整次处理。锁冲突、事务结束和保存点回滚语义参见[PostgreSQL18官方说明](https://www.postgresql.org/docs/18/explicit-locking.html)。

## 实际写者盘点

本表区分源码路径、此次运行与原有效证据。源码扫描不是运行验收；没有把所有历史非EF/维护工具纳入全覆盖声明。

| 写入入口 | 捕获/事务路径 | 本轮证据与边界 |
|---|---|---|
| `Program`注册的CP6Context、CrmIdentityConfiguration | 启用C02且租户映射配置后注入runtime；SaveChanges同步/异步在业务保存前捕获，最终事实/快照/事件/审计同事务或调用方savepoint | 源码核对；本次原生运行走实际异步管线。未启动完整Main生产/DEV宿主 |
| UserController Add/Update/Delete、AuthController改密/2FA、LoginSecurityService | User身份字段变化与删除进入user snapshot；密码/epoch/2FA/锁定/停用关联撤销browser grants | 源码核对并复用[W18捕获/撤权范围](../identity-consistency/DELIVERY.md)、[当前权限消费](../../WP02-03/current-permission-consumption/DELIVERY.md)。本轮未重新执行完整登录/2FA链路 |
| UserRoleService Save/Migrate | 主/附角色最终集合；附角色重定向捕获旧/新user | 本轮实际Save调用，去重、移除、主角色与原字节/版本；原实体重定向一致性用例在当前候选重验。Migrate入口只读核对，不运行迁移 |
| DeptService Create/Move/SetLeader/SetUserOrg/Delete | 部门路径/父节点/负责人/启用/墓碑，用户部门/经理；子树移动逐行捕获 | 实际服务创建、子树移动、用户组织/负责人和删除；显示字段Email不增身份版本。叶子墓碑版本、原字节和Outbox一致 |
| RoleController Add/Update/Delete、RolePermService SaveRolePerm/DataScopes/FieldPerms | role enable/删除；操作、范围与字段最终投影。role snapshot只含当前C02约定的crm-*动作 | 本轮实际RolePermService调用、范围4/部门集合/字段限制/权限移除/同号外租户隔离；角色启停与旧新关联矩阵重验。未改变菜单Enable/动作定义的既有权限语义 |
| MenuController Add/Update/Delete及同类tracked菜单写入 | MenuKey/新增/删除展开所有已配置租户关联；树位置/名称单独修改不生成权限变化 | 本轮原生两个并发顺序、rename/delete、真实RolePermService与最终collector；修正#304。菜单控制器源码复核，本轮没有新菜单UI/HTTP写入验收 |
| TenantAdminService、TenantSeed | 租户enable/expiry进入snapshot；停用撤销令牌，资格控制按原专业路径 | 复用[W18管理入口](../../WP02-04/tenant-management-audit/DELIVERY.md)与[W18集成](../../WAVE18/foundation-integration/DELIVERY.md)的实际范围；Seed和同步路径只读核对 |
| RefreshTokenService | 轮换ExecuteUpdate是旧令牌被替换，不是终止族；终止撤销/BrowserSession退出由tracked捕获或专用grant存储处理 | 源码核对，沿既有已接受区别，不为批量操作机械增加错误撤权；本轮未重跑全部会话源路径 |
| SqlCrmOidcGrantStore / CrmServiceTokenRecordStore | 专用当前事务调用RevokeTokenAsync，token tombstone与业务撤销同事务 | 源码核对；复用原服务token持久资格/撤权证据，本轮菜单锁不进入token writer路径 |
| GdprService anonymize/purge | anonymize经tracked保存；purge先收集主体/部门/角色ID及撤权，在批量删除后同事务补tombstones | 源码核对及原范围复用；未对任何既有租户执行GDPR或清除 |
| SsoService / TwoFactorService及策略配置 | SSO JIT/首次绑定经tracked用户保存；2FA服务变更由调用者保存。策略/issuer/region/config不是现有五类data的完整权威轴 | 本轮只读核对。策略切换、全writer epoch/final fence和完整来源向量仍待后续，不以当前目录完整替代 |

当前UserUpdateRequest/RoleController更新不接收普通租户迁移；角色新增显式盖当前租户。此次没有为了未证明的任意tracked/脱离跟踪TenantId改写而新增全局迁移规则。直接SQL、维护工具、未启用runtime的宿主、既有环境升级及所有授权epoch仍需各自验证；不能称数据库已自动阻断全部绕过写者。

## 验证记录（精简复用）

证据根 `D:/CP6-local-test/wave22/writers`，各次同名JSON/log及results/TRX保留；隔离PG18真实迁移，不用InMemory冒充PG。最终**21项去重有效通过**：10项本轮写者/故障、9项受影响既有一致性、2项实际服务→普通PG/HTTP→产品collector。它们来自下列有明确适用范围的运行，未将重复项累加。

| 运行前缀 | 实际结果与处理 |
|---|---|
| `red-menu-fanout-20261019T055234Z` | 1失败，真实旧快照复现，原始证据保留 |
| `green-menu-fanout-20261019T055639Z` | 原步骤1通过 |
| `writer-boundaries-20261019T055943Z` | 新测试params数组目标类型编译错误，无PG执行；改显式Field类型 |
| `writer-boundaries-build-fix-20261019T060034Z` | 14通过、5失败；失败均为新夹具跨案例复用全局唯一MenuKey，未进入目标断言；改按menuId分配键 |
| `fixture-key-recheck-20261019T060156Z` | 0执行/4跳过（method-only过滤器未激活自有PG配置），不算通过；runner增加显式--pg并拒绝NotExecuted完成 |
| `fixture-key-native-recheck-20261019T060332Z` | 6实际执行/通过，覆盖上述5失败及同一夹具受影响RR正反断言；与原14通过有1重叠，合计19去重 |
| `actual-service-baseline-20261019T060452Z` | 2通过/0跳过：实际服务写入、完整原字节目录，A写入不改变B内容/边界；真实并发修正进入产品collector |

PG测试断言实际阻塞状态，不以固定sleep当作锁成立。正常与优先Outbox合并核对连续版本、当前snapshot原JSON字节、data摘要与最新envelope.data相同。菜单锁取消后调用方保存点可继续查询/提交；普通Outbox故障在自有/调用方事务均回滚菜单与snapshot，释放后新上下文可成功。原9项一致性重验涵盖聚合锁并发、陈旧跟踪版本、用户/部门/权限与外租户边界。

两个HTTP测试使用W20真实JWT/可信租户/ordinary LOGIN注册及W21公开collector；请求读取Owner查询0，完成后lease0。准备与IAM业务写入使用测试Owner连接，这是现有写者采用范围，**不代表Main写入已改普通账号**。UserRole/Dept/RolePermService是真实实现；cache invalidation接口使用mock，未冒称端到端登录授权验收。原生服务测试不等同浏览器UI验收。

未变W21 collector/schema/完整性/取消证据按[W21采集器](../baseline-collector/DELIVERY.md)、[W21原生串联](../baseline-integration/DELIVERY.md)复用，不重跑全仓或重复发布。既有NuGet/analyzer告警未升级/抑制；测试DDL约束名只由固定前缀和测试Guid生成，不来自用户输入。

## 集中审查、资源与剩余

一次作者SPEC/QUALITY审查覆盖相对最新主线全部产品及测试diff、实际写者调用点、锁模式/顺序与RC新观察、原生回滚/取消、异常隔离、租户谓词、合同不变、失败/跳过真实来源。无剩余阻断，不声称独立审查。原生夹具/编译/运行选择器错误仅局部复查；文档diff、链接、台账与四状态作为提交前检查。

5个自有PG根均有stopped记录，无postmaster.pid；55435/HTTP/testhost释放。复用原唯一工作树、既有串行build/NuGet缓存，无新树/代理。失败根、日志、原用户资料未删除。无Actions触发/取消、workflow/保护变更、既有库操作、SQL Server验收或部署。

完整公共基础仍有：所有Main非EF/维护/策略写者与授权epoch、CRM基线/增量投影与发布、Owner新业务效果最终授权/撤权门、普通写账号采用及既有安装升级/恢复、真实DEV/业务/生产。本轮只闭合所列有限写入覆盖与#304；14包/125目标/375任务及247运行NOT_RUN不提升。
