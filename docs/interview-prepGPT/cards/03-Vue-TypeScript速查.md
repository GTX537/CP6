# Vue 3 / TypeScript 速查

## JS/TS

- TS 类型擦除，不自动验证 JSON。
- const 只禁重绑，对象仍可变。
- `===`；`??` 只 null/undefined，`||` 包含所有 falsy。
- spread/record with 都是浅复制。
- sort 原地且默认字符串；数字传 comparer。
- Promise continuation 是 microtask，不等于线程。
- any 关闭检查；unknown 先收窄；never 穷尽。
- number 是 double；关键金额计算服从后端权威规则。

## 响应式

- ref：`.value`/整体替换；reactive：Proxy 对象。
- reactive 解构丢响应，用 toRefs/直接访问。
- computed 纯派生、有缓存；watch 做副作用。
- watch 清 timer/请求；异步竞态用 abort/requestId。
- 大型第三方对象 shallowRef/markRaw。

## 组件

- props down，emits up，v-model 双向契约。
- slots 布局，provide 深上下文，Pinia 跨页面共享。
- v-if 创建销毁；v-show display。
- v-for 稳定业务 key，不用可变列表 index。
- 卸载清 timer/listener/observer/connection。

## Pinia/Router

- 共享长期状态进 store，局部弹窗/草稿留组件。
- storeToRefs 解构状态。
- 路由守卫是 UX，不是安全。
- cp6_authed 是信号，不是 token。
- 动态路由覆盖刷新、退出、不同角色、未知菜单。

## HTTP

- 当前 `withCredentials` + httpOnly Cookie。
- 写方法读 CSRF cookie → header。
- 401 single-flight refresh，auth 端点不递归，每请求重放一次。
- POST 重放需服务端幂等。
- 409 留页面处理冲突。
- 取消不当错误 toast。

## Element Plus

- 表格服务端分页、row-key、减少重 cell slot。
- 表单前端验证 UX，后端/DB 再验证。
- Dialog 打开初始化、取消丢弃、关闭 reset。
- 通用模板 + slots/escape hatch。
- 测试 loading/error/empty/权限/i18n/竞态，不只 snapshot。

## 当前库存页

`CpPageShell → CpListPage → fetchList → stockApi → http → Controller`

columns 处理普通列，slots 处理 Available/QC/action，Dialog 处理历史/QC。

