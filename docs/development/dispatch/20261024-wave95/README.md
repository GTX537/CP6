# W95 样品历史独立Owner及旧借还写入隔离

COORD本聊天单人，Main `a64261e5eab9bdcb9e6a1c9c45149100faa1cc6b`，Handoff `9579a98b3bade479e0f1ab8a5d75c51ec40afbc4`；Issue[500](https://github.com/GTX537/CP6/issues/500)。W94 Main498/499、Handoff69、Issue497已完整收尾，19.45min＋独立前置约4min，准确分支删除、固定席位释放。W89跨仓确认待答复，不重试bind。

## 前置和限定交付

最新AGENTS/POLICY哈希未变，Main/Handoff已fetch核对，官方status三席位空闲，无重复开放任务。已全文读取当前SampleStock实体、接口、服务、Controller、页面、两项原测试，原WMS-SAMPLE-01 R2共148行与手册115行、WP10-41适用范围；全仓SampleStocks/ISampleStockService引用核对。正式Stock Root保管转移、稳定实物发生、当前Site期限政策和真实借用者仍未采用。

- [x] 保留旧HTTP未经独立query即可读取的PostgreSQL失败复现。
- [x] SAMPLE-OWNER/1：租户、当前独立query、自身/明确原对象部门映射及已记录仓区范围；授权后计数、50默认100上限稳定分页；冻结查询、敏感字段MIN与隐藏字段过滤拒绝、最终权限/原生锁。无仓区依据只允许覆盖全部仓区的授权，不推断Site或Stock来源。
- [x] 精确保存旧18,4数量为字符串；旧原字段、历史不完整/来源未证/待核归还分列。status3仍无实还证据者可检索，返回时间仅历史记录不等于Stock归还；没有Site日期依据不计算正式逾期。
- [x] 旧读取核权后要求版本，旧add/edit/lend/return/expire/del经真实动作和原对象范围校验后明确拒绝；不重解释expire为retire、不覆盖旧历史。原非PG历史实现与两项InMemory历史测试保留原义。
- [x] 实际页面采用新分页/详情，身份、撤权、目录刷新、过滤变更、乱序和卸载失效；去除无前置的旧写操作。
- [ ] 相关原生PG/实际HTTP/迁移约束及Vue/类型检查，一次集中SPEC/QUALITY；正常产品合并、Handoff交付finish、同任务分支文档收尾合并、核对远端、Issue关闭、准确分支删除/席位释放。

预计实际40–65min：Owner15–25、页面10–15、验证审查交付15–25；本轮独立前置约5min。Main实际start `2026-10-24T02:49:24.484724+00:00` 至finish计时，不重复Handoff。DEV-1/DEV-2固定目录原仓复用，隔离PostgreSQL55435，停止自有进程、保留数据及全部失败证据。无Actions、既有库迁移或生产部署。

WP10-41完整Loan/部分归还/不可变观察写入/期限政策/Stock同UoW仍UNPROVEN/NOT_RUN；本次只交付已核历史Owner和兼容隔离，不声称Observation持久写入已实现。24/375整卡、125目标0完整、247正式运行NOT_RUN不提升。七天全目标ACTIVE，本窗口不替代D4/D5/D6真实公共前置。

## 验证完成

17独立.NET（14原生PG、2既有InMemory样品回归、1两租户权限种子）及10Vue通过，无跳过；相关构建和vue-tsc通过，去重27项。一次集中审查通过，PG停止，待正常采用/收尾。
