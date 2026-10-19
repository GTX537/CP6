# 第二十轮：目录实际宿主与安装公共基础

> W20-03实际HTTP宿主候选已验证：18项配置及1条原生PG/HTTP采用链路通过，Owner请求查询0，作者集中审查无阻断；自有PG停止。[单份DELIVERY](../../evidence/WP02-05/directory-host/DELIVERY.md)。待正常集成后继续W20-04矩阵收尾。

> W20-02经PR292合入 `73ddf0b9374e5f09140be4280895d150749f87c5`；Handoff `c155fa0a87ff9e139ebacf8504b33e4c5f427f20` 回读。W20-03实际目录HTTP宿主真实START，验证NOT_RUN。

> W20-02候选已验证：显式安装/只读inspect/身份与结构漂移拒绝、UNKNOWN恢复边界。2条原生PG/CLI生命周期及4个旧CLI入口smoke通过，作者集中审查无阻断；5个自有PG停止。[单份DELIVERY](../../evidence/WP02-02/directory-installation/DELIVERY.md)。待正常集成后继续W20-03实际宿主。

> W20-01经PR291合入 `1b03afcb8ba9b86ea6a9ccc8d45ad5392ba71364`；Handoff `65961b0f8bbf1685fbac07fc93817863d240ad44` 回读。W20-02显式安装/inspect现在真实START，验证NOT_RUN。

> W20-01候选已完成：普通PostgreSQL身份当前授权与v2只读profile，7项原生PG去重通过，集中作者审查无剩余阻断。[单份DELIVERY](../../evidence/WP02-02/directory-authorization/DELIVERY.md)。四个自有PG停止，待正常集成后继续W20-02。

当前总控单对话串行实施；持续目标已于2026-10-19 02:30:42 UTC建立，检查点为America/New_York 2026-10-19 22:30:42（UTC 2026-10-20 02:30:42）。预计16～23小时并留机动；完成就交付，时间到不等于完成。使用brainstorming / writing-plans / executing-plans，用户已授权总控自行决定歧义并连续执行；不另建对话、代理或工作树。

## 已确认基线与范围

Main/main/origin/main `d70d40efc6c85601d8c71e9c8b6a9393d2c93a3b`；第十九轮四项及PR289已收尾，Handoff `dca163c4087c5912a4aa6960ec668ebba9c31ea6`一致，活动Issues为空，近期Actions无执行中/排队。旧测试证据直接引用，不重做W19。

现有目录普通角色只能读Bootstrap、Snapshot、Generation，HTTP当前租户/服务令牌授权仍使用控制身份，可信source/authority仅有夹具串联。现有只读安装组件没有正式identity/inspect合同。本轮补这条实际采用链路。

| 窗口 | 任务 | 估计 | 完成条件 |
|---|---|---|---|
| W20-01 | WP02-02：当前授权最小身份 | 5～7小时 | 独立v2目录读取profile，在三表只读隔离基础上仅开放Sys_Tenants/ServiceToken所需列，并按可信login映射RLS；普通身份读取当前资格、撤销/过期令牌，保留v1；原生PG故障与边界证据 |
| W20-02 | WP02-02：安装和检查 | 4～6小时 | 显式fresh安装、绑定目标数据库身份、只读inspect/漂移拒绝、CLI和UNKNOWN/回滚恢复边界；拒绝未知现状/自动升级 |
| W20-03 | WP02-05：实际宿主 | 4～6小时 | opt-in严格冻结配置、实际JWT之后创建可信scope/authority、普通身份当前授权和目录读取；错误配置/失效/取消不回退控制连接 |
| W20-04 | 受影响串联与收尾 | 3～4小时 | 实际HTTP+原生PG与CLI相关验证；每任务集中作者审查、正常PR集成、远端核对、共享状态/Handoff及自有资源收尾 |

每项合入并核对远端后，复用原目录从最新main创建下一任务独立分支。后续窗口按本计划连续登记，无需重复START/ACK；只在依赖/范围实质改变时重记。

## 设计选择与实施边界

选择单独有版本的v2授权读取profile，在同一个可信租户LOGIN内完成最少授权列与目录读取。优于继续用Owner查询授权，也避免多套tenant-login目录造成第二权威。v1合同和已有数据保留；v2明确fresh-only，未知profile、已安装v1、混合service-token profile均拒绝自动采用或升级。

W20-01先核对实际Sys_Tenants/ServiceToken模型、列/键/触发器、有效ACL和Owner；事务内复用已验证v1 fresh安装后附加v2所需RLS和列授权，任何失败整体回滚。runtime无写入、DDL、角色切换、控制表映射读取和Owner回退。读取仅接受当前可信scope的tenant及已验证服务主体事实，租户禁用/过期、令牌撤销/缺失/过期拒绝；取消和存储错误安全返回。沿用现有60秒令牌偏移和租户到期的实际时间语义；不虚构最终提交撤权保证。

W20-02检查目标host/database/OID/system identifier、安装manifest/profile与实际catalog；seal是配置漂移证据，不抵御有权重写全部数据库和manifest的Owner。inspect只读，不自动修复；安装失败事务回滚，网络提交不确定写UNKNOWN并只读核查，不宣称幂等重试。CLI敏感输入从受保护文件/stdin读取，报告不输出密码或连接串。

W20-03沿用真实C02.Services认证器及现有issuer/client/scope/tenant/jti限制，冻结host配置和受保护authority；不使用fixture verifier冒称生产实现。未配置时保留历史路径，配置存在但不合法必须拒绝；启用后失败不得回退旧Owner目录/授权查询。控制连接仅用于现有Owner任务/显式安装，不参与新目录请求。受影响GET响应/游标/409/503/no-store合同保持。

W20-04在新建隔离PG上验证普通A/B身份、交替连接、当前撤销/资格、篡改/漂移拒绝、实际JWT/HTTP与CLI。未受影响测试复用W19各单份DELIVERY；不凭SHA变化重跑。每窗口形成一份DELIVERY，集中作者SPEC/QUALITY审查一次，不冒称独立审查。

不包括既有库迁移/环境切换、自动升级或全面恢复工具、全Main普通账号切换、冻结完整目录及CRM消费、Owner最终撤权fence、真实DEV/业务/生产验收。完整WP02/125目标/375任务/247运行验收状态保持真实未完成。

## 目录与资源

复用 `D:/CP6-worktrees/wave17-dispatch-20261018`；规划前tracked/untracked/ignored为空，旧HEAD已在远端main。根D:/CP6原7项用户资料保留。沿[存储规则](../../planning/20261018-workspace-storage.md)，不删除旧树/数据库/证据，不新增工作树或依赖副本。D盘观测余48.72GiB。

继续用既有NuGet缓存和串行构建输出，新增证据仅在 `D:/CP6-local-test/wave20/`。PG唯一，仅在无监听的55435启动新自有permission-consumption-*根并最终停止；既有PG副本和服务不操作。无Actions触发、部署或分支保护修改。每次push/PR检查工作流引用与事件过滤，内容未变复用既有分析。规划只检查diff/链接/台账生成视图，不编译产品。
