# E07-S05 存量 WMS 采纳与绑定实施计划

1. 先增加领域单元测试，覆盖采纳状态机和库位绑定不可变约束。
2. 增加 `SpaceWmsAdoption`、状态枚举、错误码和 API contracts。
3. 增加 `SpaceContext` 映射、唯一索引、rowversion 和 Migration。
4. 增加服务测试，覆盖刷新、查询、单项/批量绑定、放置和差异问题同步。
5. 实现仓库解析器、`SpaceWmsAdoptionService` 和依赖注入。
6. 增加 Design V1 Controller 端点、权限与 OpenAPI/SDK 契约测试。
7. 增加前端 API、WMS 采纳面板及单元测试，接入 Design V1 楼层编辑器。
8. 运行聚焦测试，修复后运行 Space 后端、前端和 SQL 门禁。
9. 生成 Migration SQL，验证 Up/Down 和 EF pending-model。
10. 完整构建、审查差异、提交功能分支并 no-ff 合入受控集成分支。
11. 在合并态重跑关键门禁，更新交付报告和项目记忆。

