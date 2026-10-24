# W95-01：样品历史独立Owner与旧写入口隔离

Issue [500](https://github.com/GTX537/CP6/issues/500)，COORD本聊天单人。[前置和范围](../../dispatch/20261024-wave95/README.md)。Main基线`a64261e5eab9bdcb9e6a1c9c45149100faa1cc6b`；任务`BUG-500-SAMPLE-OWNER`，固定DEV-1 Main、DEV-2 Handoff原仓复用，没有bind或新增目录。实际start2026-10-24T02:49:24.484724Z，原估40–65min＋独立前置约5min，最终按finish计且不重复Handoff。W94完整采用和清理证明已回填，未重做。

## 实际交付

`GET /api/wms/sample-stock/owner/v1/records`及`/{id}`采用`SAMPLE-OWNER/1`，只读当前授权的原SampleStock。现有菜单447新增query定义，不自动授角色；当前租户、动作、1本人/2本部门/3下属/4自定义部门/5全部对象范围每次读取并最终复核。部门2–4只使用明确绑定原UUID的sample-stock对象，不根据客户、名字、当前操作人或仓库推断归属。IAM目录只返回原编号/UUID；原生前向迁移将同租户、非删除原对象、不可换身份约束纳入已有mapping_validate，保留其他kind定义与权限。

仓区范围进一步取当前角色WmsRoleScope：有仓库原码时只匹配当前仓库授权；缺仓库必须通配仓库授权；具体区域必须有同租户有效Warehouse和Location原码对应实际Area。缺位置不能用具体区域授权放行，只能用覆盖全部区域的授权；RoleId1沿用既有全仓策略，仍检查独立动作/对象/字段。这里证明的是历史记录可见范围，不声称旧WarehouseCd/LocationCd已成为Stock来源或正式Location绑定。

授权过滤在总数和分页之前，同一原生Owner事务及SampleStock/仓区表锁读取，registeredAt desc＋UUID稳定排序；默认50、上限100，页码溢出和过长过滤输入拒绝。查询输入复制冻结；CustomerCd/ProductCd隐藏时即使空字符串筛选也403，避免总数侧漏。CustomerCd/Name、ProductCd/Name、LentTo和Remarks按当前字段MIN分别裁剪。所有错误保留403/404/409/503，实际MVC返回no-store；不外发实体TenantId/Creator/审计字段。

原18,4数量以精确十进制字符串表示，原单位、尾空格、空值、旧status/借出/到期/归还/注册时刻保留。数据库过滤沿用既有bpchar PAD SPACE及原collation，不通过JS重新定义代码比较。原记录统一标`LEGACY_OBSERVATION / SOURCE_UNPROVEN / HISTORY_INCOMPLETE`。有旧ReturnedAt只记`RETURN_RECORDED_UNVERIFIED`，不证明Stock实还；没有ReturnedAt且有LentAt或旧status1标`OUTSTANDING_UNKNOWN`，包括旧status3。未返数量保持null，没有旧借出记录也不推断已清零。未取得Site时区/日期政策时明确不计算正式逾期。

所有现有PostgreSQL SampleStockService公开旧入口均在原逻辑前调用Owner。旧读核权及指定对象后409要求新版本；旧add/edit/lend/return/expire/del核真实动作和原对象范围后503 `SAMPLE_WRITE_ADOPTION_UNPROVEN`，未授权403、不可见404，不分配号、不改状态、不覆盖历史、不写库存。没有把expire偷偷改为retire。搜索Core/WebApi全部SampleStocks和ISampleStockService引用，当前业务写路径仅这组服务；SQL原生锁同时覆盖并发直接写。原SQL Server历史路径和原InMemory两测试原义保留，没有新增SQL Server实现/验收。

实际SampleStockView改为真实分页和刷新原详情；身份、撤权、权限目录更新、过滤变更、乱序、卸载后清空并忽略旧响应。数量不转Number、不补默认PCS；未证来源/不完整历史/待核归还对用户可见，不展示旧无前置写按钮。UI当前没有新Loan/Return/观察持久写入，这些继续按原接受合同推进。

## 验证与集中审查

17独立.NET（14原生PG、2既有InMemory样品回归、1两租户权限种子）及10Vue通过，无跳过；相关构建和vue-tsc通过，去重27项。一次作者SPEC/QUALITY审查覆盖完整产品diff、原R2边界、现有所有服务写入口、动作/对象/字段/部门原生约束、计数分页、权限最终门/原生并发锁、实际HTTP与页面时序；没有未解决阻断。

- `owner-red-20261024T025112Z.json`：旧列表、指定详情、overdue三实际HTTP越权红灯，原来200而期望403，失败保留并已转绿。
- `owner-green-20261024T025642Z.json`：新增测试一行var多变量声明不合法，编译阶段失败、没有执行测试；拆开声明后`owner-compile-fix-20261024T025854Z.json`16项全通过。14原生PG覆盖503条分页、原精度/尾空格、原借还知识、跨租户/软删、三部门范围与撤销、自己/仓区/无定位、六敏感字段和筛选侧信道、六旧写入口零变化、冻结输入/最终源故障/错误上下文、原生SQL等待、迁移重复应用/跨租户拒绝和实际Program登录HTTP。另2项原InMemory样品测试保留历史语义，不当作新增PG证据。
- `owner-seed-20261024T030122Z.json`单项通过：两租户重复注册query仍各一条，管理员及其他角色不自动获授。
- `owner-ui.json`10项全通过，覆盖503分页、失效待核、精度/不确定值、query撤销、筛选/身份/目录/卸载/乱序、详情错对象和隐藏字段拒绝。首次`owner-typecheck.log`指出IAM新增kind缺类型联合成员，补齐后`typecheck-result.json`实际通过。所有失败保留。
- 仅修正测试编译、IAM类型声明和C#缩进；已通过的业务输入未改变，复用上述原生结果，不因为提交SHA变化重复跑。2个自有PG55435已停止，无postmaster.pid，ownership/stopped及数据、日志、TRX保留。无Actions、全仓回归、现场浏览器、正式业务验收、既有环境迁移、镜像或生产部署；依赖构建警告原样保留。

## 采用与剩余

待正常Main产品PR采用→Handoff交付/合并/finish→同Main任务分支状态收尾PR/finish→Issue500核对关闭。完成前不声明席位或准确分支已释放/删除。

24/375整卡、125目标0完整、247正式运行NOT_RUN不变。完整WP10-41的Stock Root/range保管移动、SampleSourceBinding、不可变Loan/Return/OccurrenceUse、真实借用者/用途政策、Site期限/延期/退役与失效义务、持久观察与未知恢复仍UNPROVEN/NOT_RUN。七天全目标ACTIVE；D1限定完成，D2–D7部分采用/未完成，本窗口不替代D4/D5/D6真实公共前置。

W89官方跨仓bind自动审批返回`blocked by policy`，CRM未修改；原Main/Handoff分支可靠保存、deferred席位已释放，确认仍待答复，不重试或绕过。历史W51指定已停data删除拒绝边界保留。
