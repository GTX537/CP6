# 09 · Element Plus、页面工程、测试与性能

## 1. 管理系统页面的状态模型

一个 CRUD/list 页面至少有：

```text
query state    筛选、分页、排序
server state   rows、total、loading、error
selection      选中行
editor state   create/edit/detail、draft、validation
feedback       confirm、toast、conflict
permission     button/action visibility
```

状态分清后再选组件，避免模板里散落业务逻辑。

## 2. 表格

`el-table` 常见问题：

- 没有稳定 row-key，选择/展开状态错乱。
- 前端分页却先加载全量数据。
- cell slot 每次渲染做昂贵计算。
- 数字、日期、状态格式化不统一。
- 固定列过多，窄屏不可用。

服务端分页要把 page、size、filter、sort 明确传后端，并以响应 total 驱动分页。

## 3. 表单验证

前端验证改善反馈，不是数据安全。后端重复验证关键规则，数据库约束兜底。

验证层：

- 字段格式：必填、长度、范围。
- 跨字段：开始日期 <= 结束日期。
- 业务状态：期间是否开放、库存是否足够。
- 并发：RowVersion 是否仍匹配。

Element Plus `validate()` 是异步的。提交时防双击并在 finally 恢复 loading。

## 4. Dialog 生命周期

明确：

- 打开时初始化草稿。
- 取消是否丢弃。
- 关闭后是否 resetFields。
- 编辑下一行是否残留上一行数据/错误。
- 提交成功先关窗还是先刷新。

不要直接编辑列表中的原对象后取消，否则列表已被本地修改。创建浅/深副本，按数据结构选择。

## 5. 反馈组件

- 成功 toast 简短。
- 破坏性动作 confirm 明确对象与后果。
- 业务冲突用 dialog 给刷新/覆盖/取消选项。
- 系统错误展示 trace id 便于支持。
- 不把每个网络失败都 toast 多次。

## 6. CP6 页面模板架构

当前库存页面：

- `CpPageShell`：标题、计数和页面壳。
- `CpListPage`：列表、筛选、分页通用机制。
- columns 配置：常规列。
- named slots：特殊列和 toolbar。
- 原生 `el-dialog`：复杂历史/QC 交互逃生舱。

这比复制粘贴整页减少重复，同时没有强迫所有特殊交互塞进配置 DSL。

### 模板组件的危险

- props/slots 过多，API 难懂。
- 业务页面为绕模板写大量 hack。
- 一个改动影响几十页。
- 配置对象失去类型安全。

标准化的验收不是“代码行更少”，而是新页面开发更快、一致性更高、特殊需求仍可表达、回归测试充分。

## 7. i18n

不要把语言字符串当代码 key 到处复制。稳定 namespace + 类型生成能减少缺 key。

computed 生成列 label 的原因：语言切换时 t 的依赖更新，列标题随之更新。若在模块加载时一次性调用 t，可能不会响应语言变化。

工业级 i18n 还包括：

- 日期/数字/币种格式。
- 复数。
- 文本长度扩张。
- RTL（若支持）。
- 后端错误码与前端翻译版本同步。
- fallback 语言和缺 key 监控。

## 8. CSS 与布局

优先使用设计 token：颜色、间距、圆角、字号。不要在每页散落魔法值。

Flex 常考：

- 主轴/交叉轴。
- `flex: 1`。
- 子项文本溢出时 `min-width: 0`。

Grid 适合二维布局。响应式不是只写一个 767px media query，还要考虑触控尺寸、横屏、表格替代视图和可访问性。

## 9. 前端性能

先测再改。常见瓶颈：

- 过大 JS bundle。
- 首屏串行请求。
- 大表格 DOM 节点。
- 频繁深 watch。
- 大对象深响应式。
- 路由页面未懒加载。
- 图片/字体未优化。

大表格方案：服务端分页、虚拟表格、减少 slot 计算、稳定 key、避免每格创建重组件。

## 10. Vite 与构建

开发服务器快并不代表生产 bundle 小。构建验收：

```powershell
npm run type-check
npm run test:unit
npm run build
```

路由 `() => import(...)` 形成动态 chunk。也要检查过度切 chunk 和公共大依赖。

## 11. 测试金字塔

### 单元测试

适合纯函数、composable、store 状态转移、格式化和权限判断。

### 组件测试

验证 props、emit、用户交互、loading/error/empty。不要只 snapshot 整个 Element Plus DOM。

### E2E

验证真实关键链：登录、查询、编辑、权限、冲突、退出。数量少但覆盖高价值流程。

### 契约测试

前后端类型不会自动同步。可用 OpenAPI 生成、schema 验证或契约测试防字段漂移。

## 12. StockQueryView 测试矩阵

| 场景 | 预期 |
|---|---|
| 首次加载 | hasStockOnly=true，服务端分页 |
| 筛选仓库/产品 | query 参数正确，页码重置 |
| 切换仅有库存 | reload，参数变化 |
| 数量为负 | danger 样式 |
| 无权限 | QC 按钮最终移除；API 仍 403 |
| 权限加载慢 | UI 暂时状态符合约定 |
| 历史为空 | 空表，不报错 |
| QC 保存失败 | dialog 保留，loading 恢复，错误一次 |
| 保存成功 | 本地状态与列表刷新一致 |
| i18n 切换 | 列名和状态文本更新 |

## 13. 可访问性

Element Plus 提供基础组件不等于页面自动可访问：

- 表单 label 关联。
- 键盘焦点和 dialog focus trap。
- 颜色不是唯一状态信号。
- 图标按钮有可读名称。
- 对比度。
- 错误消息被辅助技术读到。

## 高频陷阱

1. 使用组件库就不需要页面架构。
2. 前端表单验证能保护数据库。
3. Dialog 关闭会自动清空所有状态。
4. snapshot 越大测试越充分。
5. 路由懒加载自动解决所有性能问题。
6. i18n 只翻译字符串。

