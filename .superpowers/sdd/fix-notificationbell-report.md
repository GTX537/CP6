# Fix: NotificationBell 角标渲染 `[object Object]` 与控制台错误

分支：`feat/ui-restyle` · 组件：`cp6.web/src/views/oa/notification/NotificationBell.vue`

## 症状
顶栏通知铃铛右上角渲染出字面量红色标签 `[object Object]`，且红点/角标始终显示（即使无未读）。每次加载控制台报 Vue 渲染警告（NotificationBell → ElHeader → LayoutView）。UI 翻新前既存（约 447f3b0 引入）。

## 根因（systematic-debugging）
沿数据流反向定位：`el-badge :value="unreadCount"` ← `unreadCount.value` ← `refreshUnread()`。

1. 后端 `NotificationController.UnreadCount()`（`CP6.WebApi/Controllers/Oa/NotificationController.cs:44`）返回：
   ```json
   { "code": 0, "message": "OK", "data": { "count": N } }
   ```
   注意未读数嵌在 `data.count`，比 list 端点多一层（list 的 `data` 直接就是数组）。
2. `http.ts:62` 响应拦截器 `return response.data`，已把 axios 响应解包成 body。故 `notificationApi.unreadCount()` resolve 出来的 `res` = 整个 body 对象 `{ code, message, data: { count: N } }`。
3. 组件原代码：
   ```ts
   unreadCount.value = (res as any)?.data ?? (res as unknown as number) ?? 0
   ```
   `res.data` = `{ count: N }` —— 是**对象**，非数字。该对象被直接赋给 `unreadCount`。

后果：
- `el-badge :value` 收到对象 → 渲染 `[object Object]`（Vue 对非原始值的插值/badge value 报渲染警告）。
- `:hidden="unreadCount === 0"` 永远为 false（对象永不 `=== 0`），角标恒显。

一句话：未读数嵌在 `res.data.count`，代码却取了上一层 `res.data`（整个对象），把对象绑到 badge 的 :value 上。

## 修法（最小）
仅改 `refreshUnread()` 的取值，取到正确的嵌套字段并强制为数字：
```ts
const res = await notificationApi.unreadCount()
const count = (res as any)?.data?.count
unreadCount.value = typeof count === 'number' ? count : 0
```
- 取 `res.data.count`（真正的数字）。
- `typeof … === 'number'` 兜底：非数字/undefined 一律归 0 → 无未读时 `:hidden="unreadCount === 0"` 生效，红点不显。
- 未动组件其他部分、未改 API、未加 i18n key。

## 验证
- `npm run type-check` → 0 error（vue-tsc --build 通过）。
- `npm run build` → `✓ built in 7.25s`，0 error（仅既有 chunk-size 警告，与本改无关）。
- dev server 保持 5173 运行，vite 热更新未杀。

修复后角标显示正确未读数字，无未读时不显示红点，NotificationBell 相关控制台渲染错误消除。
