# 开发工作方式

## 每次接手

1. 读 `10-AI-Handoff.md`、`PROJECT_STATE.md`、当前 plan 和最近 Git log。
2. 执行 `git status`，保留用户未提交修改。
3. 查同域代码、测试和既有实现先例。
4. 先确定任务边界和事实源，再修改。

## 后端

- 依赖保持 `WebApi → Core → Entity`。
- Controller 薄、业务规则入 Service、实体保持数据模型职责。
- 新写端点明确租户、权限、错误码、事务和幂等。
- 权限键同步种子、反射 oracle、前端按钮和 i18n。
- 修改实体时生成迁移并检查 model drift。
- 测试优先使用真实 Service + EF 测试上下文；避免只测 mock 调用次数。

## 前端

- 复用 `src/api`、`types`、stores、composables 和设计系统。
- 页面文案使用 i18n 键，不硬编码多语文本。
- `v-permission` 使用字面量 `menu-key:action`，与后端逐字一致。
- 保留业务 `v-if`；权限只是并列条件。
- 提交前运行 type-check、Vitest 和 build。

## Git

- 一项任务一个清晰提交，避免夹带无关格式化。
- 不重写用户修改，不使用 `reset --hard`。
- 数据库 `.bak` 只通过 Git LFS；Secrets 永不进入 Git。
- 当前工作分支为 `feat/general-role-vperm`，不要将未完成波直接混入 main。

## 验证层级

- 文档/模板小改：diff check + 对应前端三连。
- 服务/权限变更：定向测试 + 全量后端测试 + drift check。
- 跨域/迁移/引擎变更：全量测试、构建、专项 QA、部署冒烟和回滚方案。

更详细的搭建教程见根目录 `DEVELOPMENT-GUIDE.md`。
