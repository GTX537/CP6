# 08 · Pinia、路由、HTTP 与前端安全

## 1. 什么状态应该进 Pinia

放入 store 的典型状态：

- 当前用户/权限。
- 多页面共享选择。
- 需要跨组件长期存在的领域状态。

留在组件：

- 弹窗开关。
- 单页表单草稿。
- 只被一个组件使用的 loading。

不要把所有 API 响应都堆进一个巨型 store。

## 2. Setup Store

```ts
export const usePermissionStore = defineStore('permission', () => {
  const actionKeys = ref<Set<string>>(new Set())
  const loaded = ref(false)

  function has(key: string) {
    return actionKeys.value.has(key)
  }

  return { actionKeys, loaded, has }
})
```

在组件解构状态时使用 `storeToRefs`，否则可能丢响应性。方法可直接解构。

## 3. Store 错误状态

当前权限 store 加载失败时保持 `loaded=false` 且静默。优点是不阻塞页面；风险是 UI 可能一直保留权限元素且无法区分“尚未加载”和“加载失败”。

更细状态：

```text
idle → loading → loaded
               ↘ error
```

安全仍由后端保证，但 UI 可给出清晰重试/降级。

## 4. vue-router

路由负责 URL 与页面树映射。CP6 有静态路由、菜单驱动动态路由和 standalone 页面。

导航守卫能做 UX 路由控制，但不是安全边界。用户可直接调 API，后端必须校验。

## 5. 当前登录路由流程

```text
访问目标页
→ /login、SSO landing、2FA 页面特殊放行
→ 检查 cp6_authed 非敏感信号
→ 平台区 UX 检查
→ 强制改密重定向
→ standalone 放行
→ 若动态路由未加载，从 menus 重建
→ 重新导航匹配
```

`cp6_authed` 不是凭证。即使伪造它，API 仍需认证 Cookie；它只避免前端因为读不到 httpOnly token 而无法判断初始导航。

## 6. 动态路由风险

- 菜单数据损坏或组件映射缺失。
- 重复路由名。
- 刷新时路由尚未加载产生 404/重定向循环。
- 退出登录未彻底重置。
- 前端菜单授权与后端资源权限漂移。

测试应覆盖登录后刷新、退出再登录不同角色、无菜单、菜单含未知路径。

## 7. axios 实例

当前 `http.ts`：

```ts
axios.create({
  baseURL: '/api',
  timeout: 10000,
  withCredentials: true,
})
```

统一实例提供基地址、超时、Cookie、拦截器和错误处理。API 模块只描述业务端点，不重复横切逻辑。

## 8. 请求拦截器

当前行为：

- 非 GET/HEAD/OPTIONS 注入 CSRF 头。
- OA 请求在 acting-as 状态下注入 `X-Acting-As`。

拦截器应保持快速、可预测。异步 token 获取或循环依赖容易造成请求链复杂。

## 9. 响应拦截器和 401 single-flight

状态图：

```text
请求收到 401
├─ login/refresh 自身或已重试 → 清状态 → 登录页 → reject
└─ 普通首次 401
    ├─ 已有 refreshPromise → 等待
    └─ 没有 → 创建一次 refresh
         ├─ 成功 → 标记 _retried → 重放原请求
         └─ 失败 → 清状态 → 登录页 → reject
```

需要补充类型：axios config 的 `_retried` 是自定义字段，最好用 module augmentation，避免 any。

## 10. 非幂等请求重放

POST 在服务端已执行但响应因网络丢失，客户端刷新后重放可能重复创建。解决：

- 服务端请求幂等键。
- 业务唯一约束。
- 只在确定 401 发生于认证拒绝、业务尚未执行时重放。

中间件顺序上认证在端点前，因此真正由认证层产生的 401 通常安全；但代理/自定义端点行为仍需测试。

## 11. CSRF 与 XSS

- httpOnly 防 JS 读取 token，但不阻止恶意脚本以用户身份发请求。
- CSRF token 防跨站伪造，但同源 XSS 能读可读 token。
- CSP、依赖治理、模板转义、避免 `v-html` 不可信内容仍重要。

不要把一个防线描述成全能。

## 12. `v-permission`

指令在 mounted/updated 观察权限 store；无权限移除 DOM。用 WeakMap 保存 watch stop handle，卸载时清理。

当前 loaded=false 时暂时保留元素，避免首屏误删。这是 UI 层可接受的可用性选择，因为后端强校验；若按钮会触发敏感本地展示，可改为加载前隐藏或 skeleton。

## 13. 错误消息与 i18n

响应拦截器把后端稳定错误码交给 i18n；自由文本 key 不存在时回退原文。要避免：

- 同一错误全局 toast 和页面 dialog 重复提示。
- 409 被通用错误吞掉，页面无法做冲突合并。
- 取消请求被当网络故障提示。
- 5xx 只显示“请求失败”而无 trace id。

## 14. 前端缓存和服务端状态

Pinia 不是自动服务端缓存。服务端数据需要定义：

- 新鲜度。
- 重取时机。
- 并发请求合并。
- 乐观更新回滚。
- 分页/筛选 cache key。

当前项目主要手工管理，面试可以讲原则，不要声称使用了未安装的 Query 库。

## 高频陷阱

1. 前端守卫能保护后端接口。
2. localStorage 的 cp6_authed 是 JWT。
3. 401 和 403 都应该 refresh。
4. 所有 POST 自动重放都安全。
5. httpOnly 能防全部 XSS 后果。
6. Pinia 应保存所有组件状态。

## 闭卷验收

- [ ] 给出状态进 store 的判断规则。
- [ ] 画动态路由刷新流程。
- [ ] 解释 single-flight refresh。
- [ ] 分析 POST 重放的幂等风险。
- [ ] 从 UX 和安全两层评价 v-permission。

