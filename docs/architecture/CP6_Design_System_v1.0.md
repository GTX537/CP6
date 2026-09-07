# CP6 Design System v1.0

> 状态：**已定稿待批准** · 2026-07-04
> 视觉基准：`picture/mockup-final-a-dashboard.html`（仪表盘）、`picture/mockup-final-b-wms-list.html`（出庫指示一覧）
> 适用范围：cp6.web 全部前端模块（ERP / Pur / Plan / MES / WMS / OA / FIN / PMS / WF / Space 3D / Platform）及未来新模块
> 变更管理：本文档为 UI 唯一事实来源（single source of truth）。任何 token 值、组件规范的修改必须先改本文档（版本号 +0.1）再改代码。

---

## 0. 定位与三层架构

CP6 前端 UI 收敛为三层，调整成本逐层递减、影响面逐层递增：

```
第 1 层 Design Tokens（CSS 变量）  改一处 = 全站换肤（色/字/距/圆角/阴影/动画）
第 2 层 基础组件 + 业务模板         改一处 = 一类页面统一变（如所有列表页）
第 3 层 页面级 slot 定制            改一处 = 只动一个页面
```

**铁律：业务页面（`src/views/**`）禁止出现硬编码色值、阴影、圆角。** 一切视觉属性必须来自 token 变量或第 2 层组件。Code review 时 `#[0-9a-fA-F]{3,8}` 出现在 views 目录即打回（图表数据色等豁免场景见 §2.5）。

### 设计气质（一句话）

**专业化科技感的柔和 SaaS**：青绿主色 + 悬浮式侧栏 + 柔和弥散阴影 + 等宽数字；糖果色只出现在图表与状态，数据区保持高密度与克制。

---

## 1. 文件落位与引入顺序

```
cp6.web/src/styles/
  tokens.css              ← 第 1 层全部变量（本文档 §2~§7 的代码化）
  tokens-dark.css         ← 暗色语义层覆盖（§12，v1.0 只建结构）
  element-overrides.css   ← Element Plus 全局变量映射 + 全局组件微调（§10）
  transitions.css         ← 通用过渡/动画类（§7）
cp6.web/src/components/
  base/                   ← 基础封装组件（Cp 前缀，§9）
  templates/              ← 业务模板组件（§10 之前的 §9.2）
```

`main.ts` 引入顺序（后者可覆盖前者）：

```ts
import 'element-plus/dist/index.css'
import '@/styles/tokens.css'
import '@/styles/tokens-dark.css'
import '@/styles/element-overrides.css'
import '@/styles/transitions.css'
import '@/assets/main.css'        // 只保留响应式与遗留全局规则，逐步瘦身
```

`src/assets/base.css` 为脚手架残留（未被引用），实施第一阶段删除。

---

## 2. 颜色规范

所有变量前缀 `--cp-`。两级结构：**primitive（原始值）→ semantic（语义引用）**，暗色模式只覆盖 semantic 层（§12）。

### 2.1 品牌色 Brand

| Token | 值 | 用途 |
|---|---|---|
| `--cp-brand` | `#14B8C4` | 主色：主按钮、选中态、链接强调 |
| `--cp-brand-2` | `#2BD4CD` | 渐变亮端 |
| `--cp-brand-deep` | `#0E93A0` | 深色变体：文字链接、图标着色（白底上对比度优于主色） |
| `--cp-brand-grad` | `linear-gradient(118deg, var(--cp-brand-2), var(--cp-brand))` | 主按钮、侧栏选中态、活动分页 |
| `--cp-brand-glow` | `0 8px 20px rgba(20,184,196,.30)` | 品牌元素专属阴影 |
| `--cp-brand-bg` | `rgba(20,184,196,.08)` | 品牌淡底：hover 背景、选中底、计数徽章底 |

**规则**：白底上的品牌色文字/图标一律用 `--cp-brand-deep`（`#14B8C4` 在白底上对比度不足，只用于大面积填充与渐变）。

### 2.2 中性色 Neutral

| Token | 值 | 用途 |
|---|---|---|
| `--cp-ink` | `#10343C` | 标题、强调文字（带青调的墨色，不用纯黑） |
| `--cp-text` | `#47616B` | 正文 |
| `--cp-muted` | `#8CA3AB` | 次要文字、表头、说明 |
| `--cp-faint` | `#C2D2D7` | 占位符、禁用、装饰性文字 |
| `--cp-line` | `#E6EFF1` | 标准边框、分隔线 |
| `--cp-line-soft` | `#EFF6F7` | 表格行分隔等更弱的线 |
| `--cp-bg` | `#F2FAFB` | 页面底色（叠加 §2.4 的氛围渐变） |
| `--cp-card` | `#FFFFFF` | 卡片/表面 |
| `--cp-bg-th` | `#FBFDFE` | 表头底、输入框底 |
| `--cp-bg-hover` | `#F6FCFC` | 表格行 hover |

### 2.3 语义色 Semantic

每个语义色配一个淡底（bg 变体），组成状态 pill、通知图标底、KPI 图标底。

| Token | 值 | bg 变体 | 用途 |
|---|---|---|---|
| `--cp-ok` | `#22B573` | `--cp-ok-bg: #E7F8F0` | 成功/已完成/已出库 |
| `--cp-warn` | `#F0940A` | `--cp-warn-bg: #FEF3E2` | 警告/待处理/未出库 |
| `--cp-danger` | `#E5484D` | `--cp-danger-bg: #FDEBEC` | 错误/超期/库存预警/删除 |
| `--cp-info` | `#4E80EE` | `--cp-info-bg: #EBF1FE` | 进行中/信息类状态 |
| `--cp-violet` | `#8B7CF0` | `--cp-violet-bg: #F0EEFD` | 图表第 5 色 / 特殊状态补充 |

### 2.4 页面氛围背景

页面 body 底色 = `--cp-bg` + 右上角一枚青绿弥散光斑（radial-gradient），仪表盘可加第二枚蓝紫光斑。数据密集页只保留一枚，避免干扰阅读：

```css
background: radial-gradient(1000px 520px at 92% -8%, rgba(43,212,205,.10), transparent 55%), var(--cp-bg);
```

### 2.5 图表色板（唯一豁免场景）

图表系列色按**固定顺序**取用，禁止循环取色、禁止随机色：
`--cp-brand` → `--cp-info` → `--cp-violet` → `--cp-warn` → `--cp-ok`。
第 6 个系列起合并为「其他」（`--cp-faint`）。单系列图直接用品牌色。状态类图表（如制造进度环）用语义色并必须带文字图例。头像渐变（客户/担当首字母块）允许使用预设渐变组合，视为图表数据色。

---

## 3. 字体与字号体系

### 3.1 字体栈

```css
--cp-font: 'Nunito', -apple-system, 'HarmonyOS Sans SC', 'PingFang SC',
           'Microsoft YaHei', 'Noto Sans SC', sans-serif;
```

- Nunito 只承担拉丁字符与数字（圆润气质来源）；中文回退系统栈。Nunito 通过本地字体文件（`src/assets/fonts/`，woff2，权重 600/700/800）打包，**不依赖 Google Fonts CDN**（生产环境可能无外网）。
- **所有数据数字必须 `font-variant-numeric: tabular-nums`**（工具类 `.num`），保证表格列、KPI 对齐。

### 3.2 字号阶梯

| Token | 值 | 用途 |
|---|---|---|
| `--cp-fs-2xs` | `11px` | 表头字母间距标签、时间戳、徽章 |
| `--cp-fs-xs` | `12px` | 辅助说明、tag、图例 |
| `--cp-fs-sm` | `12.5px` | 次级按钮、面包屑、链接按钮 |
| `--cp-fs-base` | `13px` | 表格正文 |
| `--cp-fs-md` | `13.5px` | 正文、导航项 |
| `--cp-fs-lg` | `14.5px` | 卡片标题 |
| `--cp-fs-xl` | `16px` | 区块标题 |
| `--cp-fs-2xl` | `21px` | 列表页标题（PageShell h1） |
| `--cp-fs-3xl` | `24px` | 仪表盘问候标题 |
| `--cp-fs-num-lg` | `31px` | KPI 大数字 |

### 3.3 字重

只用三档：**600**（正文）、**700**（次强调/导航）、**800**（标题、数字、按钮）。禁用 400（在浅青底上过淡）与 900。

---

## 4. 间距体系 Spacing

4px 基数，token `--cp-sp-N`（N 为像素值）：**4 / 8 / 12 / 16 / 20 / 24 / 28 / 32 / 40**。

| 场景 | 规范 |
|---|---|
| 卡片内边距 | 数据卡 `16~20px`；展示卡 `18~22px` |
| 卡片之间 | 列表页纵向 `16px`；仪表盘网格 `16~22px` |
| 页面内容区 | `padding: 22~24px 28px`，`max-width: 1400~1420px` 居中 |
| 表格单元格 | `9~11px 14~20px`（列表页取紧，仪表盘取松） |
| 表单字段间 | `12px`；label 与控件间 `5px` |

奇数值（如 5px、9px）只允许出现在第 2 层组件内部微调，业务页只用 4 的倍数。

---

## 5. 圆角体系 Radius

| Token | 值 | 用途 |
|---|---|---|
| `--cp-r-xl` | `20px` | 展示型大卡（仪表盘焦点卡、条幅） |
| `--cp-r-lg` | `16px` | 标准数据卡片 |
| `--cp-r-md` | `12px` | 列表页卡、快捷入口、侧栏项 |
| `--cp-r-sm` | `8px` | 小控件（头像块、小图标底） |
| 控件专用 | `9~11px` | 输入框 9、按钮 10~11、工具按钮 11（第 2 层组件内固化） |
| 全圆 | `999px / 50%` | pill、头像、圆点 |

原则：**容器大、控件小、数据密的地方收敛**。禁止在数据表格区使用 20px 以上圆角。

---

## 6. 阴影体系 Shadow

| Token | 值 | 用途 |
|---|---|---|
| `--cp-shadow-1` | `0 1px 2px rgba(16,52,60,.04), 0 6px 20px rgba(16,52,60,.05)` | 静止卡片、按钮、输入类表面 |
| `--cp-shadow-2` | `0 10px 30px rgba(16,52,60,.08)` | hover 浮起 |
| `--cp-brand-glow` | 见 §2.1 | 品牌渐变元素专属（主按钮、侧栏选中、活动分页） |

规则：阴影色相必须带墨青调（`16,52,60`），禁用纯黑阴影；**边框与阴影二选一为主、弱边框（`--cp-line`）+ 弱阴影（shadow-1）可并存**；不叠两层强阴影。

---

## 7. 动画规范 Motion

| Token | 值 | 用途 |
|---|---|---|
| `--cp-t-fast` | `.1s ~ .13s` | 表格行 hover、小控件反馈 |
| `--cp-t-base` | `.15s ~ .18s` | 按钮、卡片、导航 hover |
| 缓动 | `ease`（默认） | 全部场景，不引入弹跳曲线 |

固定手势语言：
- **hover 浮起**：按钮 `translateY(-1px)`、快捷入口/KPI 卡 `translateY(-3px)` + shadow-2
- **进入动画**：v1.0 不做页面级入场动画；弹窗用 Element Plus 默认
- 必须尊重 `prefers-reduced-motion: reduce`（transitions.css 内统一关停 transform 过渡）

---

## 8. 图标规范

- 唯一图标库：`@element-plus/icons-vue`（线性风格）。**禁止 emoji、禁止混入第二套图标库**；确无对应图标时以 24×24 viewBox、`stroke-width:1.8`、圆头圆角的内联 SVG 补充，收敛到 `components/base/icons/` 统一管理。
- 着色：默认 `--cp-muted`，hover/激活 `--cp-brand-deep`，选中态白色；语义场景用语义色。
- 尺寸阶梯：`13~14px`（输入框内）、`15~17px`（按钮/导航）、`21px`（快捷入口）、`22px`（条幅）。
- `main.ts` 现有「全量注册全部图标」保留（v1.0 不动），后续性能优化再按需引入。

---

## 9. 组件规范

### 9.1 基础组件（`components/base/`，Cp 前缀）

对 Element Plus 的薄封装，只做视觉与默认值收口，**不改行为 API**（原 props/events 透传）。

| 组件 | 封装要点 |
|---|---|
| `CpButton` | 三种形态：`primary`（品牌渐变+glow，hover 浮起）、`ghost`（白底+line 边框，hover 变品牌色）、`link`（无底色品牌深色文字）。高度：默认 34px、小 28px。圆角 10~11px，字重 800 |
| `CpInput` / `CpSelect` / `CpDatePicker` | 高 34px、圆角 9px、底色 `--cp-bg-th`、边框 `--cp-line`，hover/focus 边框变品牌色；placeholder 用 `--cp-faint` |
| `CpTable` | 表头：11px/800/`--cp-muted`/底色 `--cp-bg-th`/字母间距 .8px；行 hover `--cp-bg-hover`；选中行品牌淡底 + 首列 `inset 3px 0 0 var(--cp-brand)` 指示条；单号列品牌深色 800 字重；数字列右对齐 + tabular-nums |
| `CpTag`（状态 pill） | 结构固定：**圆点 + 文字**（颜色永不单独传达状态），淡底 + 语义色文字，圆角 999px，11.5px/800。状态→色映射在组件内集中维护（如 已出库→ok、未出库→warn、拣货中→info、已取消→muted、今日/超期→danger） |
| `CpCard` | 白底 + `--cp-r-lg` + shadow-1；标准 card-head（标题 14.5px/800/ink + 左侧 16px 品牌深色图标 + 右侧 link 动作），下边线 `--cp-line-soft` |
| `CpPagination` | 30px 方块、圆角 9px；活动页品牌渐变 + glow；含条数/页码/跳页 |
| `CpDialog` | 圆角 `--cp-r-lg`，标题 16px/800/ink，footer 右对齐 ghost+primary |
| `CpEmpty` | 统一空状态：线性插图 + muted 文案 + 可选 CTA |

### 9.2 业务模板（`components/templates/`）

| 组件 | 用途与结构 |
|---|---|
| `CpPageShell` | 每个业务页的壳：面包屑（顶栏内）、页标题（21px/800 + 计数 pill）、右侧操作按钮组、内容区（纵向 16px 间距）。slots：`title-extra`、`actions`、默认 |
| `CpListPage` | 查询列表页一体化模板 = 状态速览条（可选，点击即筛选）+ 搜索区（字段配置驱动，支持「展开更多」折叠）+ 表格卡（批量操作工具栏 + CpTable + CpPagination）。props：`columns`、`searchFields`、`fetch`（数据源函数）、`statusTabs`；slots：`toolbar`、每列 `col-<prop>`、`expand`。**目标：覆盖 130+ 个现存 el-table 查询页** |
| `CpFormDialog` | 新增/编辑弹窗模板：字段配置驱动 + 校验 + 提交回调；复杂表单用默认 slot 自组 |
| `CpDetailPanel` | 详情描述卡：label/value 栅格，含状态 pill、单号等格式化 |
| `CpStatCard` | KPI 卡：标签 + 语义色图标 chip + 大数字（31px/800/tabular）+ 副文本 + 可选 7 日 sparkline（单系列、2px 线 + 10% 透明面积填充） |
| `CpSectionHeader` | 区块标题行（标题 + 查看全部链接） |
| `CpFilterBar` | 搜索区独立版（供不用 CpListPage 的页面复用） |
| `CpStatusStrip` | 状态速览条独立版（`统计数组 → pill 卡`，点击 emit 筛选） |

特殊页面（Space 3D 编辑器、工作流设计器、Konva 画布、MES Control Tower）不强制套模板，但必须消费 token 与 CpButton/CpTag 等基础件。

### 9.3 图表规范（仪表盘类）

- 系列色遵循 §2.5 固定顺序；分段图形之间留 2px 表面缝隙
- 环形图：中心放合计数（ink/800/tabular），右侧图例 = 色块 + 名称 + 数量 + 百分比；≥2 系列必须有图例，文字永远用文本色而非系列色
- sparkline：单系列免图例，2px 圆头线 + 淡面积填充，无坐标轴
- 双轴图禁止；两个量纲 = 两张图

---

## 10. Element Plus 二次封装规范

### 10.1 全局变量映射（`element-overrides.css`）

用 CSS 变量覆盖（不引入 SCSS 编译定制），核心映射：

```css
:root {
  --el-color-primary: var(--cp-brand);
  --el-color-primary-dark-2: var(--cp-brand-deep);
  /* light-N = 品牌色向白混合 N×10%（Element Plus 生成规则），静态预算值： */
  --el-color-primary-light-3: #5BCDD6;
  --el-color-primary-light-5: #8ADCE2;
  --el-color-primary-light-7: #B9EAED;
  --el-color-primary-light-8: #D0F1F3;
  --el-color-primary-light-9: #E8F8F9;
  /* success/warning/danger/info 的 light-N 变体按同法预生成，实施时一次性写入本文件 */
  --el-color-success: var(--cp-ok);
  --el-color-warning: var(--cp-warn);
  --el-color-danger: var(--cp-danger);
  --el-color-info: var(--cp-info);
  --el-text-color-primary: var(--cp-ink);
  --el-text-color-regular: var(--cp-text);
  --el-text-color-secondary: var(--cp-muted);
  --el-text-color-placeholder: var(--cp-faint);
  --el-border-color: var(--cp-line);
  --el-border-color-lighter: var(--cp-line-soft);
  --el-fill-color-blank: var(--cp-card);
  --el-bg-color-page: var(--cp-bg);
  --el-border-radius-base: 9px;
  --el-border-radius-small: 8px;
  --el-border-radius-round: 999px;
  --el-box-shadow-light: var(--cp-shadow-1);
  --el-font-family: var(--cp-font);
}
```

覆盖不到的部分（如 el-table 表头、el-button 渐变）在同文件内以最小选择器补写，**集中在这一个文件**，禁止散落到页面。

### 10.2 使用层级约定

1. **优先用第 2 层组件**（Cp 模板/基础件）
2. 模板覆盖不了的场景，直接用 Element Plus 原子组件（此时全局 overrides 已保证观感一致）
3. 同一 el-* 视觉微调在 ≥3 个页面重复出现 → 上升为 Cp 基础组件或并入 overrides，禁止复制粘贴 scoped 样式

---

## 11. Vue 组件与代码规范

- **命名**：封装组件一律 `Cp` 前缀 + PascalCase（`CpListPage`）；页面组件 `<业务名><形态>View.vue`（`ShipmentListView.vue`、`LocationEditView.vue`）；组合式函数 `use` 前缀（`useListPage.ts`）
- **目录**：`components/base/`（基础）、`components/templates/`（模板）、`components/<domain>/`（业务专用）；页面在 `views/<module>/`
- **样式**：业务页 `<style scoped>` 只写布局（栅格/间距），视觉属性来自 token；新增全局样式只允许进 §1 列出的四个文件
- **模板组件必须有注释头**：用途、props/slots 一览、使用示例（≤10 行）
- 现存 `VolTable.vue` / `VolForm.vue`：功能被 `CpListPage` / `CpFormDialog` 取代后删除（6 个使用页随迁移批次切换）；`HelloWorld.vue`、`TheWelcome.vue`、`WelcomeItem.vue`、`icons/` 脚手架残留在第一阶段删除

---

## 12. 暗黑模式预留方案（v1.0 只建结构，不实现）

1. **两级 token**：§2 的 primitive 值不动；页面与组件只消费 semantic 变量（`--cp-bg`、`--cp-card`、`--cp-ink`…）
2. `tokens-dark.css` 中以 `html.dark { --cp-bg: …; --cp-card: …; }` 覆盖 semantic 层；同时引入 Element Plus 官方暗色变量文件（`element-plus/theme-chalk/dark/css-vars.css`）并映射
3. 切换机制预留：`useDark()`（VueUse 或自实现，写 `html.dark` class + localStorage），顶栏预留按钮位（v1.0 不显示）
4. **现在必须遵守的纪律**：任何新代码禁止消费 primitive 色值，只消费 semantic 变量——这是暗色模式未来「零重构」上线的唯一前提
5. 阴影在暗色下整体降不透明度、品牌 glow 保留；图表色板暗色版届时按验证流程重新取值

---

## 13. 迁移策略（摘要，详见实施计划）

1. **阶段一**：tokens.css + element-overrides.css + LayoutView 重做（悬浮侧栏/毛玻璃顶栏/语言切换）+ Dashboard 重做 → 全站观感先变 70~80%
2. **阶段二**：base/ 与 templates/ 组件落地，选 1~2 个 WMS 页试点验证 CpListPage API
3. **阶段三**：分模块批量迁移（WMS 39 → ERP 42 → OA 25 → FIN 22 → PMS 22 → MES 18 → 其余），同步清除硬编码色值；每模块一个 PR
4. 新功能（波次拣货、Space 3D 发布 UI、OA 后续）自阶段二完成后直接按本规范开发

---

## 附录 A：Token 速查（tokens.css 完整清单）

```css
:root {
  /* Brand */
  --cp-brand:#14B8C4; --cp-brand-2:#2BD4CD; --cp-brand-deep:#0E93A0;
  --cp-brand-grad:linear-gradient(118deg,var(--cp-brand-2),var(--cp-brand));
  --cp-brand-glow:0 8px 20px rgba(20,184,196,.30);
  --cp-brand-bg:rgba(20,184,196,.08);
  /* Neutral */
  --cp-ink:#10343C; --cp-text:#47616B; --cp-muted:#8CA3AB; --cp-faint:#C2D2D7;
  --cp-line:#E6EFF1; --cp-line-soft:#EFF6F7;
  --cp-bg:#F2FAFB; --cp-card:#FFFFFF; --cp-bg-th:#FBFDFE; --cp-bg-hover:#F6FCFC;
  /* Semantic */
  --cp-ok:#22B573; --cp-ok-bg:#E7F8F0;
  --cp-warn:#F0940A; --cp-warn-bg:#FEF3E2;
  --cp-danger:#E5484D; --cp-danger-bg:#FDEBEC;
  --cp-info:#4E80EE; --cp-info-bg:#EBF1FE;
  --cp-violet:#8B7CF0; --cp-violet-bg:#F0EEFD;
  /* Radius */
  --cp-r-xl:20px; --cp-r-lg:16px; --cp-r-md:12px; --cp-r-sm:8px;
  /* Shadow */
  --cp-shadow-1:0 1px 2px rgba(16,52,60,.04), 0 6px 20px rgba(16,52,60,.05);
  --cp-shadow-2:0 10px 30px rgba(16,52,60,.08);
  /* Typography */
  --cp-font:'Nunito',-apple-system,'HarmonyOS Sans SC','PingFang SC','Microsoft YaHei','Noto Sans SC',sans-serif;
  --cp-fs-2xs:11px; --cp-fs-xs:12px; --cp-fs-sm:12.5px; --cp-fs-base:13px;
  --cp-fs-md:13.5px; --cp-fs-lg:14.5px; --cp-fs-xl:16px; --cp-fs-2xl:21px;
  --cp-fs-3xl:24px; --cp-fs-num-lg:31px;
  /* Spacing */
  --cp-sp-4:4px; --cp-sp-8:8px; --cp-sp-12:12px; --cp-sp-16:16px;
  --cp-sp-20:20px; --cp-sp-24:24px; --cp-sp-28:28px; --cp-sp-32:32px; --cp-sp-40:40px;
  /* Motion */
  --cp-t-fast:.12s ease; --cp-t-base:.16s ease;
}
```

## 附录 B：布局壳规范（LayoutView 重做基准）

- 侧栏：238px 常驻（≤767px 转抽屉），无底色悬浮式；品牌区（渐变 logo 块 + 产品名 + 字母副标）、分组导航（组标签 10.5px/800/faint/字母间距 2px）、选中项品牌渐变 + glow、计数徽章 danger 淡底、底部环境徽标（生产环境 + 版本 + 呼吸圆点）
- 顶栏：sticky + 毛玻璃（`rgba(242,250,251,.72)` + `backdrop-filter: blur(14px)`）；左面包屑，右侧全局搜索（⌘K 提示，仪表盘等宽裕页显示）、语言切换器（中/日双语必备）、通知铃（红点）、用户胶囊
- 菜单数据仍来自现有 `localStorage.menus` 递归渲染（`MenuTreeItem` 重写为新样式，数据结构不动）
