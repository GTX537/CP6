# Space P3 · SP1 — P1 Viewer e2e 跑通 记录

> 日期 2026-06-28。Plan `docs/superpowers/plans/2026-06-28-space-p3-sp1-viewer-e2e.md`(T1~T4)。分支 `feat/space-p3-hardening`。

## 环境

| 部件 | 详情 |
|---|---|
| 前端 | vite 5173(`npm run dev`) |
| 后端 | 5177,读 `appsettings.Local.json` → `CP6DB_SpaceQA`(已迁移到合并分支 schema + Space 07/08 演示数据)。本机无 RabbitMQ 也能稳跑(见下「坑」澄清) |
| Playwright | `@playwright/test`(已装)+ `npx playwright install chromium` |
| 账号 | admin / 123456 |
| 数据 | Site QAWH `F31F48C2…` / Floor `5C92E6A8…` / 已发布编码 `A-01-01-01…A-01-02-02` |

## 运行

```
cd cp6.web
npx playwright test e2e/space-viewer.spec.ts --project=chromium
```

`chromium` project 自动先跑 `setup`(`auth.setup.ts` 登录存 storageState),再跑 viewer 用例,串行复用会话。

## 结果

**13 passed / 3 skipped / 0 failed**(总用时 ~3min)。
- 通过:setup(鉴权)+ N1-a/b、M3、N3-a/b/c、N4-a~f(12 个 viewer 用例)。
- 有意 skip:`N1-c`(QA 仅 1 楼层 → 跳切层)、`CROSS`(无 `SPACE_OTHER_FLOOR_CODE` env)、`Manual inspection`(无 `SPACE_MANUAL` env)。

## 整合做法

- `tests/space-viewer.e2e.ts` → `e2e/space-viewer.spec.ts`(进既有 config testDir,被 `chromium` project 收录,鉴权复用既有 `auth.setup.ts` storageState;删自带 cp6_at 说明)。
- `FloorList.vue` 列表项加 `:data-floor-id`(切层用例定位)。
- env 默认 IDs = 真实 QA 值,可经 env 覆盖。

## 🔴🔧 跑绿过程抓到/修复的问题

1. **既有 `auth.setup.ts` 对当前登录页失效**(全 e2e 共用,ERP e2e 同样受影响):
   - `.fill()` 不触发 el-input 的 v-model → 表单校验视空值挡提交 → 用 `click()+pressSequentially()` 逐字符输入修复。
   - 断言 `localStorage.getItem('token')` 恒 null:token 在 httpOnly cookie `cp6_at`(storageState 已捕获),前端仅存 `cp6_authed='1'` 标志 → 断言改 `cp6_authed`。
2. **`openViewer` 不真正等待加载**:`.viewer-loading` 由 `v-if=loading` 控制,初次渲染 loading=false 不在 DOM,`waitForSelector(hidden)` 立即返回 → 测试在场景未加载时就操作。改为「先等出现(可忽略)再等消失」。
3. **🐞 真 viewer bug(e2e 抓到):`Locator.locate` floorId 比较大小写敏感** — locate API 返回小写 GUID,`currentFloorId` 来自 URL 可能大写 → 同层定位被误判跨层 → 多余场景重载。改 `toLowerCase()` 比较(`Locator.ts`)。
4. **N3-a InfoCard 等待 3s 偏紧**:locate = API 往返 + flyTo(~800ms)+ 渲染,冷/争用栈下 >3s → 放宽到 12s(spec §失败兜底)。

## 坑

- **RabbitMQ 澄清(经实测纠正,无需修复)**:OA-D1 `NotificationConsumer.ExecuteAsync` **已把连接+消费整段包在 `try/catch(Exception)` 里**(`NotificationConsumer.cs` L56~131),broker 不可达时 `BrokerUnreachableException` 被捕获并 log「通知 Consumer 异常退出」,ExecuteAsync 正常返回 → **不触发 `StopHost`,宿主存活**。**实测**:不带任何 env override(`appsettings.Local.json` 配了 RabbitMQ HostName)启动后端,等 broker 连接失败窗口后 5177 仍 LISTENING、login 200。所以**合并后端在无 RabbitMQ 的 DEV 上能稳跑,无需 `RabbitMQ__HostName=` 规避,也无需改 OA 代码**。早先误判"会崩"实为首个 `nohup dotnet run` 进程被进程管理回收(非 consumer),换启动即稳。
- IDs 绑 QA GUID,QA 库重灌则需改默认或传 env(spec D1;API 动态发现留后续鲁棒性升级)。
- el-input + Playwright:`.fill()` 不更新 v-model,用 `pressSequentially`(登录与搜索框均如此)。

## 留点 / 后续

- N1-c/CROSS 需 ≥2 楼层 / 跨层编码种子才覆盖(当前 skip)。
- SP1.5(可选):扩 e2e 覆盖 07 库存叠加 / 08 拣货路径·热图(本 SP1 范围(a)最小未含)。
- OA `NotificationConsumer` 无 broker 崩宿主的鲁棒性修复(非 Space 范围,建议反馈 OA)。
