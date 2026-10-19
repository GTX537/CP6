# 第十九轮：身份目录公共基础单对话持续计划

> W19-03候选31项去重通过（14原生PG+17配置），集中作者审查无阻断，四个自有PG停止；[单份DELIVERY](../../evidence/WP02-05/trusted-directory-reader/DELIVERY.md)。正常集成后继续W19-04 HTTP串联。

> W19-02经PR286合入 `c2b808a1e081b20d537a665cef387b90c6b4e164`；Handoff `f201d63ac3f4423c91be83a9b702a255ce9503fa` 回读。W19-03可信连接reader现在START，验证NOT_RUN；下方记录保留历史时点。

> W19-02普通账号三表只读隔离候选已验证，5项原生PG去重通过，包含有效ACL/越权拒绝及普通control下FORCE RLS写入。[单份DELIVERY](../../evidence/WP02-02/identity-directory-isolation/DELIVERY.md)。六个自有PG停止，当前待正常集成；下一窗口为W19-03可信连接目录适配。

> W19-01已通过PR285合入main `85f4776e36f19db16f5c0077f72906d8d3d31441`，#284关闭；Handoff `a2a66b333bf48a9b519bad03461a9a9fd6cc0036` 已回读。W19-02三表普通角色只读隔离真实START，验证尚NOT_RUN；下方待集成状态保留历史时点。

> W19-01 / #284候选已通过11项原生PG及一次集中作者自审，两个真实RED保留。[单份DELIVERY](../../evidence/WP02-05/directory-consistency/DELIVERY.md)。正常PR/远端核对待执行，当前Issue保持Open；之后串行进入W19-02。四个自有PG停止，无新worktree/Actions。

执行者为当前总控，使用 writing-plans / executing-plans，在当前对话独自串行实施。用户要求先列未来24小时任务并设持续目标；目标已于2026-10-18 23:49:21 UTC建立（America/New_York 19:49:21）。检查点为2026-10-19 19:49:21当地时间，估算18～24小时含约3小时修正机动；时间到不等于完成。

## 已核对基线与本轮选择

Main/main/origin/main `4233a204002a57da5fb9355a47c10a4f8d45bbd7`，第十八轮最终收尾PR282已合入。Handoff COORD `decade3db5ed8db54f41d90b216f438e6012cae0`、四窗口Closed，活动BUG查询为空，近期Actions均已完成且无本轮排队/运行。W18身份快照锁后刷新、权限一致读取、租户强审计及串联证据继续复用。

源码已有TenantConnectionManager、可信scope、普通角色保护、ERP/Identity消息RLS；它们不覆盖CP6Context的身份目录读取。IdentitySnapshotReader分页采用PG RepeatableRead，但单快照的就绪检查和数据读取是分离语句，bootstrap及目录的真实PG并发覆盖也不足。优先补这段基础，避免扩大为全Main切换或重做消息表组件。

| 窗口 | 卡片 | 估算 | 输出/完成条件 |
|---|---|---|---|
| W19-01 | WP02-05 | 3～4小时 | 核对并补齐bootstrap、就绪和目录读取的一致性；原生PG并发、分页边界、未就绪与取消验证；单份DELIVERY、集中作者审查、正常合并 |
| W19-02 | WP02-02 | 5～7小时 | 对实际Bootstrap、Snapshot、TenantGeneration目录对象建立有版本的只读RLS/最小授权组件，真实普通A/B账号证明隔离和禁止写入；显式control安装/拒绝未知权限，无启动自动安装 |
| W19-03 | WP02-05 / WP02-02 | 4～6小时 | 用已有可信连接组件读取目录，保留现有响应/游标/边界语义；实际普通角色、事务和scope/catalog失效验证；不以高权限EF连接冒称普通身份采用 |
| W19-04 | 本轮集成 | 3～4小时 | 受影响目录HTTP/PG链路串联，分别注明认证查询与目录读取身份；逐项远端main核对、共享状态和Handoff收尾、停止自有实例 |

每项合入后才从当时最新main开下一任务分支。后续窗口在此计划内可由总控核对实际依赖和写集后连续登记，无需重复运输ACK；范围实质变化重新登记。全任务gate与本轮有限窗口准入分开：不将WP02-02/03/04/05/10或125目标/375任务虚报完成。

## W19-01 实施步骤

1. 从规划已合入main创建 `codex/wave19-directory-consistency-20261018`，一次真实START；占用Main:identity-projection与Main:identity-directory-reader。
2. 读取IdentitySnapshotReader、IdentityBootstrapService、真实迁移与既有夹具；用实际PostgreSQL在两连接间控制变更时间，证明就绪与内容是否来自同一提交快照。若发现BUG，在GitHub登记原步骤后修复，不用静态猜测宣布缺陷。
3. 保留现有分页遇代际变化返回409并要求重建的合同，不在此改成冻结目录新协议。补齐cursor租户绑定/过期、pageSize边界、当前generation、空目录、未bootstrap/错误bundle和取消的实际相关行为，避免逐字段镜像测试。
4. 使用最小改动修正真实失败，验证bootstrap不会通过旧跟踪状态误判；保护调用者事务和取消，不添加自动重试/默认租户。
5. 形成 `docs/development/evidence/WP02-05/directory-consistency/DELIVERY.md`，复用未受影响W18证据，集中SPEC/QUALITY作者自审一次，正常PR合并及远端包含性核对后关闭对应BUG并释放窗口。

W19-01允许产品路径为 `CP6.Core/Services/CrmIdentity/IdentitySnapshotReader.cs`、`IdentityBootstrapService.cs`；验证为 `CP6.Tests/CrmIdentityDirectoryPostgreSqlTests.cs` 与必要的既有 `CP6.Tests/Platform/TenantManagementPostgresFixture.cs` 扩展。共享台账/四状态由本总控在本任务交付提交内同步。端口55435仅在确认无监听且新自有permission-consumption-*根下使用。

## W19-02/03的边界

实现前逐表核对实际模型/迁移、列类型、主键、RLS、Owner和有效ACL。保留现有TenantId、全局/租户键、版本和原字节，不新增第二套权威身份目录、不修改历史迁移、不迁移既有库。租户来源仅为可信scope与受保护login目录；runtime不拥有表、不具superuser/BYPASSRLS、不得写入身份目录或直接读登录映射。新组件的profile/model/domain严格有限，不能把自由schema或connection字符串交给请求方。

普通只读角色不是新的认证协议；应用主体、服务token资格、租户绑定仍按既有合同检查。目录读取有权限并不意味着已加入所有Owner提交前撤权门。W19-03在真实组件和明确测试宿主中采用；若默认Program/完整宿主切换需要额外对象权限或部署配置，记录并留给单独窗口，不扩大runtime授权求通过。

冻结完整目录的不可变原字节/重建协议、CRM消费和健康对账、全Main普通账号切换、所有Owner最终撤权fence、既有库迁移/环境切换、真实业务与生产验收均保持后续范围。本文不会把分页一致性升级成完整WP02-05完成。

## 目录、验证与交付规则

复用 `D:/CP6-worktrees/wave17-dispatch-20261018`，每任务独立codex分支；规划前已核旧HEAD在远端、tracked/untracked/ignored为空、无本目录进程。根7项用户未跟踪资料保留。新增/删除worktree、对话、子代理均0；沿[存储规则](../../planning/20261018-workspace-storage.md)，不开新全仓副本、不清缓存或历史证据。只读观测D盘约50.59GiB可用；不是新释放容量。

PostgreSQL唯一目标，复用NuGet cache与已释放的串行构建输出，证据在 `D:/CP6-local-test/wave19/`。局部开发验证、一次任务级作者集中审查，修正后仅复查受影响部分；作者自审不称独立审查。真实失败保留，实际请求的PG验证必须0跳过；已有有效结果标明复用来源，不因提交或SHA变更重跑。

规划仅做diff、路径/链接、台账和生成视图校验，不运行产品测试。每次push/PR检查候选及main工作流引用和路径过滤，复用内容未变的分析；禁止Actions与生产部署，不变更分支保护。正常逐任务PR集成，远端核对后才关闭Issue/窗口。最终每窗口一份报告、总控汇总链接、Handoff固定提交回读与自有进程停止记录。
