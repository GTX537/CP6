# M-OA/WF T4 执行报告：fail-closed 权限反射测试

## 状态：DONE

新增 `CP6.Tests/OawfPermissionAttributeTests.cs`（4 用例），零生产改动。真相源 `docs/seeds/oawf-permission-keys.md`。

## 扫描面收口对账（与真相源 §七逐项吻合）

| 项 | 真相源 | 测试断言 | 核对 |
|---|---|---|---|
| 扫描控制器 | 16（Oa 11 + Wf 5，双命名空间） | `OawfControllers_AreDiscovered` == 16 | ✅ |
| 非 GET 端点总 | 33 | 31 贴 + 2 豁免 = 33 | ✅ |
| 真·写贴点 | 31 | `Assert.Equal(31, taggedCount)` | ✅ |
| 只读 POST 豁免 | 2 | `Assert.Equal(2, exemptHit.Count)` | ✅ |
| menu-key 去重 | 7（全 oa-*） | `^oa-[a-z0-9-]+$` 正则 | ✅ |

## 贴点计数（从控制器源码逐词读出，31=31）

grep `RequirePermission(` 于 Oa+Wf 共 **31** 处，distinct action **14 词**，全部落入 `ActionVocabulary`：
add(3) edit(5) del(2) favorite(1) read(4) submit(4) enable(1) withdraw(1) approve(2) transfer(1) sendback(2) addsign(1) delegate(3) form-save(1) = 31。
menu 全部 `oa-*`（含 Wf 5 控制器——键锚定 OA 消费页菜单，真相源 §一/§二，本波无 wf-* 键）。

## 豁免对账（2 条，精确）

| 键 | 真相源 | 依据 |
|---|---|---|
| `ForecastController.Preview` | §四#1 | ForecastService 全类无写，POST 仅传 varsJson 复杂体 |
| `QueryController.Search` | §四#2 | InboxService.QueryAsync 纯读投影，POST 仅传 FormQueryFilter |

两方法均为 HttpPost 且无 [RequirePermission]，已逐条读源码确认。`ReadOnlyPostExemptions_AreAllStillUntaggedMutatingEndpoints` 防腐守卫锁死其「实存·仍是变更端点·仍未贴键」。

## 基类继承链自查（DeclaredOnly 安全前提）

逐类 grep 类头结果：
- **Oa 11 控制器全部** `: LocalizedControllerBase`
- **Wf**：AdvancedFlow/Approval/Flow/Form `: LocalizedControllerBase`；TaskController `: ControllerBase`
- 即：15 个经 `LocalizedControllerBase`，1 个（Wf.TaskController）直继 `ControllerBase`。

已读 `CP6.WebApi/Controllers/LocalizedControllerBase.cs`：抽象基类，仅惰性暴露 `Localizer` 属性，**零 [HttpXxx] action 声明**。故所有写端点均为子类手写声明方法，`BindingFlags.DeclaredOnly` 反射不漏扫。注释已按此实况准确记录（区别于 MES 版「全直继 ControllerBase」，沿用 ERP 版「混合基类」措辞）。

## 反向验证证据（fail-closed 真闸）

1. 临时删 `InboxController.Batch` 的 `[RequirePermission("oa-inbox","approve")]`（审批族最高危写路径）。
2. `dotnet test --filter OawfPermissionAttributeTests` → **红**：
   `InboxController.Batch：变更端点缺 [RequirePermission] 且不在只读 POST 豁免清单`
   Failed: 1, Passed: 3（核心闸 offender + taggedCount 31→30 双重触发）。
3. 恢复贴点 → **绿**：Passed 4/4。
4. `git status --porcelain` → 仅 `?? CP6.Tests/OawfPermissionAttributeTests.cs`，工作树干净（生产文件无残留改动）。

## 验证结果

- `dotnet test --filter OawfPermissionAttributeTests`：**Passed 4/4**。
- 全量：**Passed 1775, Failed 0, Skipped 5**（1771 基线 + 4 新增，无回归）。

## concerns

无。真相源 §六头号命门（洁净首启 OA MenuKey 回填时序 403）属 T2 职责，非 T4 范围；本测试为编译期/属性级反射守卫，不涉运行期 aggregator，故不受该命门影响。
