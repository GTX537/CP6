# F-T1 报告：五语 i18n seed + 菜单/权限 seed 汇总落库（波⑤ 引擎基建六件套收口）

- **分支**：`feat/wfs-engine-infra`　**Commit**：`54c570b9`（已 push）
- **闸门**：后端 **2110 绿 / 5 skip**（基线 2098 +12 新增）· EF drift clean（零迁移零实体零引擎 diff）· 无跨模块污染（仅 Seed/Program.cs/Tests/doc）
- **提交范围**：7 文件 +524（4 新增 seed/test + Program.cs + I18nTenantComplianceSeed + doc；零 .vue/控制器/迁移改动）

## 主控口径纠偏（brief vs 实际，已执行主控指令）
- **brief Step 3「菜单/权限 seed 已在 A-T4/D-T2 落，本任务复核」与实际相反**：A-T4/D-T2 只贴 `[RequirePermission]`，seed 全部延交 F-T1。本任务**实际落库**全部菜单/权限/i18n seed。
- **D-T2 报告「连接器挂菜单 733」为笔误**：真相源 `OawfMenuSeed.Rows` 证 **733=oa-inbox、734=oa-flow-admin**；`FlowTriggerPermissionSeed` 亦用 734。连接器正确挂 **734**。
- **brief 键名草稿多处与代码不符，一律以代码 t() 为准**：`oa.workcalendar.*`→实际 `oa.workcal.*`；`oa.designer.svc.delay.workdays`→实际 `oa.designer.svc.delayMode.workdays`；`oa.tenant.timezone`→实际 `platform.tenant.timeZone`。

## 键面双向对账表（前端 t() 全量 grep ↔ seed，零缺零孤儿）

### I18nOaEngineInfraScreenSeed（46 键）
| 键族 | 键数 | 消费源（实 grep） | 备注 |
|---|---|---|---|
| `oa.workcal.*` | 13 | WorkCalendar.vue | 含动态 `kind.{makeup\|closed\|weekend\|normal}` 全 4 值域（line 38 日格 + line 48 radio）+ `{n}` 插值 `imported` |
| `nav.743` | 1 | MenuTreeItem.vue `te('nav.'+id)?t():menuName` | 新菜单 743 侧栏标签 |
| `oa.connector.*` | 22 | WfConnectorPanel/Dialog.vue + FlowAdmin.vue(tab) | tab/new/empty/authYes/authNo + col.×7 + form.×11 |
| `oa.designer.svc.httpMethod/.httpMethodHint/.timeoutSec` | 3 | NodePropertyPanel.vue（E-T1） | |
| `oa.designer.svc.delayMode.workdays` | 1 | NodePropertyPanel.vue（A-T3） | |
| `oa.designer.timeout.errorEdge` | 1 | designerModel.ts（B-T2 标签） | |
| `oa.designer.errHttpOverride` | 1 | designerModel.ts:201（E-T1 校验） | |
| `oa.designer.errErrorEdgeSource` / `.errTimeoutErrorEdge` | 2 | designerModel.ts:260/265（B-T2 校验） | |
| `E-WF-027` / `E-WF-028` | 2 | 后端错误码（brief 给定文本照用） | |

### I18nTenantComplianceSeed（+3，落平台域家族）
| 键 | 消费源 | 归属依据 |
|---|---|---|
| `platform.tenant.timeZone/.timeZonePlaceholder/.timeZoneHint` | TenantListView.vue（E-T2） | `platform.*` 家族既维护于此 seed（Program.cs:1989 已入 concat），照惯例落对应文件；域内聚 |

### 既有全局键（不重复放）
- `common.edit/cancel/save`：**跨模块既有全局键**（main 上 space/wms/oa 多页既用，`git grep` 实证），非本波欠账 → 不种。
- `取消`/`确定`：既存于 `I18nCnScreenSeed`/`I18nFinScreenSeed` → 不种。

> **对账方法**：对波⑤新增/改动前端文件全量 `grep t()`，逐键与 seed 双向核对；`emit('saved')`/`emit('update:modelValue')` 等被正则误捕的非 i18n 串已剔除。测试 `I18nOaEngineInfraScreenSeedTests` 以独立硬编码 oracle（46 键）锁死键集，missing/orphan 双向断言。

## 菜单段位选择
- 新菜单 **743**（工作日历）：`OawfMenuSeed.Rows` 止于 742（741/742 双栈收编行），全库 grep `743` 无占用 → 就近连续取 743。
- 结构：`(743, "工作日历", "/oa/work-calendar", "Calendar", ParentId=740, OrderNo=743, MenuKey="oa-work-calendar")`。
- **MenuKey 插入时显式赋值**（全仓时序命门）：Program.cs:1005「无 MenuKey RoutePath 自动回填」块只填 null 行，显式赋值即免疫时序；且路由派生键恰 = `oa-work-calendar`，即便回落亦一致（双保险）。
- 落地位置：不改 `OawfMenuSeed`（避免其测试 7→8/10→11 重基线），改由新 `WorkCalendarConnectorPermissionSeed` 就地插入 Sys_Menu 743 + Sys_RoleMenu（共享表只播一次），MenuAction/RoleAction 逐租户——自包含、零触既有 seed/守卫。

## 五语样例（术语系与既有 I18nOa* 一致）
- `oa.workcal.imported`：ZhCN「已导入 {n} 个假日」/ Ja「{n} 件の祝日を取り込みました」/ Ko「공휴일 {n}건을 가져왔습니다」（`{n}` 具名插值保真）
- `oa.designer.svc.httpMethodHint`：ZhCN「留空＝用连接器默认」/ Ja「空欄＝コネクタの既定を使用」/ Ko「비우면 커넥터 기본값 사용」
- `E-WF-028`：ZhCN「超时配置或时区非法」/ Ja「タイムアウトまたはタイムゾーンが無効です」/ Ko「타임아웃 또는 시간대가 잘못되었습니다」（brief 给定文本照用）
- `platform.tenant.timeZoneHint`（自愈口径）：ZhCN「改时区不批量重算既有触发器到期时刻，下次发火后按新时区自愈（最多一次按旧时区发火）。」五语真译。

## 守卫确认
- `OawfPermissionAttributeTests`（19 控制器/47 非GET/45 贴点/2 豁免）**未触动**：本任务零控制器改动，seed 不入守卫扫描面。全量套件内该守卫绿。
- 键面 insert-only 安全：全部 49 新键（46 + 3 platform）经全库 grep + 测试 `Items_NoCollisionWithSiblingSeeds` 证与既有 seed 零撞键 → 部署即套用，零 SQL 补丁（波①T10 教训规避）。

## TDD 产出（12 新测，全绿）
- `I18nOaEngineInfraScreenSeedTests`（7）：键集精确匹配 oracle（missing/orphan 双向）· 无重复键 · 五语非空 · Ja/Ko 真译（谚文强信号 + 中日共用汉字「操作」白名单）· 仅 imported 用花括号插值（防 vue-i18n 破形）· 兄弟 seed 零撞键 · 时区键落 compliance seed。
- `WorkCalendarConnectorPermissionSeedTests`（5）：菜单 743 显式 MenuKey + admin RoleMenu · 逐租户 4 元组精确 · 幂等二次零增 · RoleId=1 显式 TenantId · 无租户仍建菜单但零 action。

## concerns（复核人知会）
1. **`common.edit/cancel/save` 全库未落 Sys_Lang 却被 10+ 页（含 main 上 space/wms/oa）消费**：i18n 纯 DB 驱动（`/lang/{lang}`，无前端静态字典），故这些键当前**回退裸显 key**。这是**跨模块既有缺口**（预 main、非本波引入），且主控口径明示「common.* 不重复放」——故不种，记为待裁跨切票（建议后置全局 `common.*` seed 任务，一次覆盖 space/wms/oa 全模块）。连接器沿用此既有模式，与先例一致。
2. **doc §八为增量追加而非重写**：`oawf-permission-keys.md` §一–§七 是 M-OA/WF 16 控制器/33 非GET 快照，波③④⑤ 增量在新增 §八 收口（19/47/45/8 menu-key），与守卫逐字对齐；未改动历史快照表避免误伤。
