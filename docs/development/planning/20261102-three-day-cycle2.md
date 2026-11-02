# CP6 第二期三天执行计划

> COORD单人，使用executing-plans逐窗口执行；用户已授权继续实施。固定DEV-1/Main，DEV-2串行CRM/Handoff，不启动子代理。上一期三天和七天/W249已完成。

**目标：** 核清真实采用缺口，补CRM审核到期持久终态和新编号域规则版本治理。原375卡按完整scope单独核销。

**架构：** 沿用实际Owner、PostgreSQL事务、原命令恢复及当前授权边界。审核处置与PII清除分别判断；编号配置版本追加，历史分配不变。现有业务编号域保持原分配器，不能因新服务存在就改接。

**技术：** .NET8、PostgreSQL18、现有CRM原生HTTP及Main EF；模拟业务/IAM/外部服务可用于验收，产品与数据库行为实际执行。

容量按3天、每日6小时必做+2小时机动安排；这是排程估计，按依赖连续执行，不等待日历、不因时间到达自动完成。每项只做受影响验证与一次集中审查。

|窗口|范围|交付出口|
|---|---|---|
|D1/W255|WP02-10实际连接关系；WP03-01/02原范围和公共服务适用|明确已采用/未接线及完整卡可否核销，准确更新台账|
|D2/W256|WP03-03审核到期持久终态|到期后Expired持久化，重复/竞争无第二效果，原截止不被延长，真实PG/HTTP读取可见|
|D3/W257|WP02-16新登记域规则CAS和原命令恢复|旧版本冲突、新配置原子生效、旧号码/计数不改、丢响应查原修改结果|

## D1：先消除误判

- [x] 沿`CP6.WebApi/Program.cs`、`Middleware/TenantMiddleware.cs`、`CP6.Core/EFDbContext/CP6Context.cs`和实际Stock Owner追踪共享连接、TenantId及最终授权；对照`CP6.Core/Persistence/Tenancy/`原适配范围。未接线的新schema/RLS不能记成已采用，也不把共享存储自动判为泄露。
- [x] 对照`task-plans/WP03.md`、W102/105/114/135/247原报告与已合并CRM代码，核配置生效、原日历/合法日期、陈旧预览、旧Attempt保留及原结果恢复。只在原scope全覆盖且直接前置实际采用时关闭完整卡。
- [x] WP03-02原冻结合同允许LeadNo未分配且不接附件；记录此路径适用结论，同时保留整卡公共服务前置及未来实际消费，不能拿W254组件替代CRM接入。
- [x] 更新`roadmap.json`、生成页和四状态文件，归档上一期最终PASS一次；文档diff/引用检查，不启动产品测试；本轮实际采用遇到工具只读本仓报告的缺口，按准确仓库/固定SHA支持既有CRM报告，4项回归和全部82项台账测试通过。

D1核对完成，结果见[W255交付](../evidence/W255-01/DELIVERY.md)；合并/finish由同轮最终proof核销。

## D2：审核终态与保留分别实现

直接前置是W253已采用的七天期限、放行/拒绝最终门，及既有CRM当前维护身份、持久租户锁与保留Owner。原规范`CRM-INQ-R3-V15-FINAL-MAIN`状态单调；ReviewExpiresAtUtc=ReceivedAtUtc+7天。Expired处置保留按冻结Bundle的对应政策，从原审核截止推导，后台迟到不得延长。

文件职责：CRM `src/CP6.CRM.Api/Inquiry/CrmInquiryReviewExpiryOwnerV1.cs`负责有界到期事务；`CrmInquiryRetentionOwnerV1.cs`/宿主worker只在既有维护权限准确适用时接入，不能默把PII清除权限扩成放行权限；`src/CP6.CRM.Infrastructure/Inquiry/`保存必要的原终态结果/审计和受保护约束。现有`CrmInquiryIntakeReadOwnerV1.cs`只读原行，不在GET偷偷写终态。测试扩展`InquiryReviewExpiryTests.cs`与相关保留/HTTP夹具。

- [x] 首先固定维护actor/用途与原状态机的准确关系；若冻结签名合同不含所需动作，采用明确的版本化内部维护边界，不扩写冻结外部枚举，不绕过当前身份/tenant门。
- [x] 新增真实失败场景：到期后执行处理仍NeedsReview。通过实际公开提交创建完整原图，以已有受控时钟到原七天边界，不更新不可变原件。
- [x] 在同一当前身份/租户与行锁事务内重读状态及期限；仅NeedsReview且now>=原截止进入Expired，历史、行版本、非PII结果与必需审计共同提交。已终态恢复原结果，不能覆盖放行/拒绝，也不能创建Lead。
- [x] 独立核Expired保留截止、ConsentFact和匿名化职责；原Receipt/Attempt/历史时间保留。晚到worker不能用执行时间重新加保留时长；LegalHold只影响清除，不使业务重新可审核。
- [x] 验证期限前/等于/后、重复/重启恢复、真实并发放行与到期、租户/撤权、故障全回滚、HTTP列表/详情和公开回执不泄露。页面未变时复用原Expired展示证据。

```powershell
dotnet test tests/CP6.CRM.IdentityPostgreSqlTests/CP6.CRM.IdentityPostgreSqlTests.csproj --no-restore --filter "FullyQualifiedName~Review_expiry|FullyQualifiedName~Review_terminal"
```

由现有隔离引导设置自有`CP6_CRM_PG_SETTINGS`；要求实际PG/HTTP、必需场景0skip。权限合同/存储适用检查在本窗口完成，不以缺企业数据阻塞，也不把仅组件测试写成生产部署。


D2准确结果见[W256](../evidence/W256-01/DELIVERY.md)：已匿名化分支仅内存投影，新事件消费者独立，正常采用/finish按最终proof核销。

## D3：新登记编号域规则治理

直接前置为W254 `NumberAllocationOwnerV1`，只处理已明确登记的新Main tenant/BizKey域。先完成可安全采用的规则版本控制；历史ERP/WMS/Common/DocNumber域切换、全局域和CodeGen仍独立余项。

文件：`CP6.Core/Services/Pub/NumberAllocationContractV1.cs`增加版本读取/修改命令和原结果；Owner/Store保存规则历史并与分配共用登记规则锁；`NumberAllocationAuthorityV1.cs`采用实际`pub-seq:edit`当前动作；新增Core PG前向迁移，保留W254原scope/分配原件；`CP6.Tests/NumberAllocationPostgreSqlTests.cs`覆盖实际并发/故障。修改合同说明`docs/development/number-allocation-v1.md`，不默认注册生产DI或开放任意领号接口。

- [ ] 固定可变格式与不可变编号域/周期身份边界，明确版本对新分配和原结果恢复的不同作用；合法旧输入不缩水。
- [ ] 先测两命令竞争同expectedVersion只能一个成功，以及修改后原分配仍返回原号码/旧版本。规则版本不可重复绑定不同内容。
- [ ] 同事务保存规则新版本、当前指针/CAS、修改命令原输入/结果；同key异义409，旧expectedVersion412；提交结果未知只查原命令，不能猜测修改失败。
- [ ] 原周期计数与号码历史不清零、不删除、不改写；旧管理入口DB/服务守卫保持，格式改动引起历史号码冲突明确失败。
- [ ] 验证当前edit/read权限、跨tenant/Owner、preview无副作用、配置/分配并发、提交前故障及提交后丢响应。普通未登记域复用/补跑受影响旧Seq回归。

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter "FullyQualifiedName~Number_rule|FullyQualifiedName~Number_allocation|FullyQualifiedName~SeqServicePostgreSqlTests"
```

使用已有隔离PG夹具和自有`CP6_PERMISSION_PG_ROOT`；0skip，不使用InMemory证明锁或提交恢复。

## 每窗口交付与固定席位

- [ ] 按最新远端基线官方status/start，独立准确任务分支；中断resume原任务。
- [ ] DEV-2从Handoff固定START提交保存后deferred finish释放，再bind CRM；CRM完成后bind回Handoff并resume原分支交付。不得并行增加目录。
- [ ] 一份报告记录真实执行、复用证据、模拟范围和未完成原scope；四状态和台账同步。提交前快照与最终PR/finish核销分开。
- [ ] 工作流/依赖未变时复用原手动触发结论，只核当前引用和路径；本期不触发Actions、不部署、不迁移既有环境。
- [ ] 正常PR合并、远端准确采用、停止自有进程、条件删除准确任务分支、官方finish释放；每日报告分支/工作区/席位结果。

D1核对发现实质缺实现时记录精确缺口，不把核销窗口变成全库改造；D2/D3必做边界如需扩大，先记录真实原因及重新分配容量，不能删原合同换完成。候补仅在必做完成后安排，不预先占席位。
