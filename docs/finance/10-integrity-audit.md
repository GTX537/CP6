# 10 · 数据完整性与审计：红冲 / 锁期 / maker-checker / 权限

> **让这本账经得起审计。** 财务系统和业务系统最大的区别，是它要**被审计、能打官司、对税务负责**。本章把散落各章的完整性机制收口成一套：不可篡改、可追溯、权限分离、定时对账。本章结束时，你能回答审计师的灵魂拷问"你怎么证明这账没被人偷偷改过"。
>
> 上游：全部章节（本章是它们的"内控收口"）。

---

## 一、题眼：财务可信 = 不可篡改 + 可追溯 + 权责分离

一句话概括整套内控：

> **每一笔账，谁记的、谁审的、改过没有、为什么改，都要留下不可抹去的痕迹；且记账的人不能自己审自己。**

这不是技术洁癖，是财务的立身之本。下面四道防线，每道都在前面章节出现过，这里收口成体系。

---

## 二、防线 1：不可篡改（凭证只能红冲）

[铁律 2](./README.md#铁律-2-凭证不可改不可删只能红冲)：已过账凭证**没有 Update/Delete**，错了只能[红冲](./01-gl-kernel.md#六铁律-2-落地maker-checker-状态机--红冲)。代码层面要挡死：

```csharp
// JournalEntryService 不暴露任何 Update/Delete Posted 凭证的方法
// 数据库层再加一道：触发器禁止 UPDATE/DELETE 已过账凭证行
CREATE TRIGGER trg_JournalLine_NoMutate ON JournalLines
INSTEAD OF UPDATE, DELETE AS
BEGIN
  IF EXISTS (SELECT 1 FROM deleted d JOIN JournalEntries e ON d.EntryId = e.Id
             WHERE e.Status = 2 /*Posted*/)
    THROW 50001, '已过账凭证不可修改/删除，请用红冲', 1;
END
```

> **应用层 + 数据库层双保险**。应用层可能被绕过（直连 DB、bug），数据库触发器是最后一道墙。审计师最爱问"能不能直接改数据库改账"——有这个触发器，答案是"改不了"。

业务实体（发票/付款）可以有状态流转，但**财务凭证一旦 Posted 就是化石**。

---

## 三、防线 2：权责分离（maker-checker）

[01 章决策](./01-gl-kernel.md#六铁律-2-落地maker-checker-状态机--红冲)：手工凭证制单人 ≠ 过账人。落到权限上，是**把财务动作拆成独立权限点**：

| 权限点 | 谁有 | 说明 |
|---|---|---|
| `fin:voucher:create` | 会计 | 录入/提交凭证 |
| `fin:voucher:post` | 财务主管 | 过账（复核），系统强制 ≠ 制单人 |
| `fin:voucher:reverse` | 财务主管 | 红冲 |
| `fin:period:close` | 财务主管 | 月结锁期 |
| `fin:period:reopen` | 财务经理 | 反结账（最高危，最少人有） |
| `fin:ap:pay` | 出纳 | 付款 |

> 复用 CP6 现成的 `Sys_Role`/`Sys_RoleMenu` RBAC，把这些做成独立权限点。**关键是"录"和"审"分授给不同角色**——一个人既能录又能审，maker-checker 就名存实亡。自动凭证（[直过](./05-auto-voucher.md)）的"制单人"是 SYSTEM，不占人工权限。

---

## 四、防线 3：可追溯（审计轨迹 + 凭证连续性）

### 全程留痕

复用 CP6 现成的 `Sys_OperLog` + Kafka 审计流：凭证的录入/过账/红冲、期间的结/反结、发票/付款的每个动作，全部落审计日志（谁、何时、什么动作、改了什么）。财务动作的 OperLog 应**标记为高保留级别**（不随常规 7 天清理）。

### 凭证号连续不断号（gapless）

审计要求凭证号**连续无跳号**——断号意味着"是不是有凭证被偷偷删了"。所以 `FinSequence` 采番必须：

```
- 同一会计期间内凭证号连续：GL-2026-06-00001, 00002, 00003...
- 作废的凭证也保留号（标记作废），不能让号消失
- 采番要处理并发（避免两张凭证抢同一号）——用 DB 序列或行锁
```

> 这和业务采番（断号无所谓）不同。**财务采番断号是审计红旗**。`MesSequence`（已有）可参考，但财务版要保证 gapless + 并发安全。

---

## 五、防线 4：定时对账（自动揪出不一致）

[09 章](./09-cp6-integration.md#四数据一致性最终一致不是强一致)说财务是最终一致，靠对账兜底。把对账做成**每日定时 job**（复用 CP6 `BackgroundServices` HostedService 体系）：

```csharp
// CP6.WebApi/BackgroundServices/FinReconciliationWorker.cs
public async Task RunDailyAsync()
{
    var issues = new List<ReconIssue>();

    // ① 试算平衡（02 章）：借贷必平
    if (!(await _trial.BuildAsync(currentPeriod)).MovementBalanced)
        issues.Add(new("试算不平", Severity.Critical));

    // ② AP 子账 ↔ GL 应付控制科目（03 章勾稽）
    var ap = await _ap.ReconcileApAsync(currentPeriod);
    if (!ap.IsMatched) issues.Add(new($"AP 子账与 GL 差 {ap.Diff}", Severity.Critical));

    // ③ AR 子账 ↔ GL 应收控制科目（对称）
    // ④ 业务勾稽：已确认出货数 vs 已生成 AR 凭证数（防"货出了账没记"）
    var missing = await _ar.MissingInvoiceShipmentsAsync(currentPeriod);
    if (missing.Any()) issues.Add(new($"{missing.Count} 张出货未生成凭证", Severity.High));

    if (issues.Any()) await _notifier.PushAsync(issues);   // SignalR 推财务看板 + 死信告警
}
```

> 这四条对账是财务的"体检"。任何一条不平，**当天就告警**，而不是月底结账才发现差几万对不上。复用你现成的 HostedService + SignalR + DeadLetterNotifier，零新基建。

---

## 六、其他完整性细则

| 细则 | 做法 |
|---|---|
| 财务实体禁软删除 | 发票/付款/凭证不走 CP6 的软删除，只有状态流转（作废/红冲/撤销） |
| 金额精度 | 一律 `decimal`，禁 `double`（[01 章](./01-gl-kernel.md#五铁律-1-落地借贷恒等校验)） |
| 锁期保护 | 已结账期间拒绝任何凭证（[02 章](./02-period-close.md#32-结账动作--锁期)），反结账高权限+留痕 |
| 控制科目保护 | AP/AR/库存等控制科目禁手工记账，只能子账驱动（[01 章](./01-gl-kernel.md#13-控制科目control-account--子账和总账的接缝)） |
| 附件留存 | 发票扫描件/付款凭据可挂附件，审计取证 |

---

## 七、本章自检（也是面对审计师的自检）

- [ ] "能不能直接改数据库改账？"——我有应用层 + DB 触发器双保险挡住吗？
- [ ] "记账的人能自己审自己吗？"——maker-checker + 权限分离做到了吗？
- [ ] "凭证号为什么这里跳了一个？"——我能保证 gapless + 作废留号吗？
- [ ] "你怎么知道账没记漏？"——每日对账 job 覆盖试算/AP/AR/业务勾稽了吗？
- [ ] "上个月结账了还能改吗？"——锁期 + 反结账留痕能答清吗？
- [ ] 财务实体我确认都没走软删除、金额都是 decimal 吗？

全部能答 → **这本账经得起审计**。至此，财务会计模块的完整设计丛书收口：从[总账内核](./01-gl-kernel.md)到[报表](./08-financial-statements.md)，从 [MVP 的 AP](./03-accounts-payable.md) 到[差异化的成本](./06-cost-accounting.md)，从 [Phase 6 集成](./09-cp6-integration.md)到本章的内控。CP6 可以从"进销存 + MES"正式升级为"ERP"了。

---

*生成于 2026-06-10。需求基线：强制 maker-checker / 凭证不可篡改 / 定时对账 / 复用 Sys_OperLog+HostedService。配套实现落于 `CP6.*/.../Fin`。*
