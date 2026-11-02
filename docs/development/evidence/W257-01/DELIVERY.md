# W257 / 第二期D3编号规则CAS

范围：仅W254已登记的新Main租户/BizKey编号域，新增格式规则版本、当前版本CAS、原修改命令查询与恢复。原375卡、完整WP02-16、历史ERP/WMS/CRM/Common/DocNumber、全局域、CodeGen和实际宿主/管理页面采用保持独立；没有生产DI接线或任意公开领号端点。

前缀、日期格式和最小宽度可修改；域身份、Owner、周期、时区、文化、起始值固定。采用当前读取能力及`pub-seq:edit`，与分配共用原scope行锁，最后核当前编辑身份。旧expectedVersion412、同key异义409、版本名不可重用；原命令恢复原结果和原主体，不因后续版本重写历史。原分配仍返回旧号码/版本，计数不回收、不重置。

规则版本和修改结果同事务只追加。最新连续revision是唯一当前指针，省去重复head定义；原登记bytea不改写，格式冲突在实际分配返回历史重号并回滚。新receipt版本守卫拒绝规则变更后的旧写入者；原Pub目录及旧Next/Controller守卫保持。前向迁移不重写旧receipt，并保留版本键NULL；仅自有本地PG实际迁移，同库宿主升级另行采用，旧二进制不是滚动兼容。

业务/IAM数据及当前会话解析为明确组件模拟；实际运行.NET8/PG18迁移、权限读取、事务、独立连接竞争及提交故障。未将模拟主体解析宣称真实生产登录，未运行新HTTP/UI消费者。

26 relevant PostgreSQL cases PASS,0 skipped:9 rule-change cases,9 allocation cases,8 legacy Seq regressions. Final2 strengthened original-command concurrency/deferred-integrity cases executed;24 unchanged successes reused from this window. Actual PG18 migrations,current permission facts,independent connections,rollback/unknown-commit recovery;mock business/IAM data and host-session resolver. Historical unrelated menu-max migration failure retained as open Issue684;not claimed passed. No Actions/deployment.

证据`D:/CP6-local-test/wave257/foundation/final-validation.json`逐项记录来源。首次RED命中未实现入口；28项宽筛选批次实际27PASS/1FAIL，其中26项是本轮相关场景，另2项为旧工程用例；失败为[历史菜单极值迁移Issue684](https://github.com/GTX537/CP6/issues/684)，只读复现确认未改历史MAX(MenuId)+1溢出，本轮不修、不关闭、不称通过。末尾2项强化回归首次被自有诊断PG占用端口拒绝，停止诊断PG后以同一已编译输入重跑2PASS；所有失败日志/数据保留。24项其余成功输入未变，准确复用。

9项规则场景覆盖同版本不同命令CAS、同原命令并发与后续版本恢复、3000字/NUL版本无损、历史版本重绑定拒绝、当前edit/read撤权与原主体/tenant/Owner、最终权限失败全回滚、journal失败/真实commit后丢响应、分配与修改竞争、只读preview、格式历史碰撞及旧周期继续、原件不可变/缺修改结果提交拒绝/旧写入者守卫、非空W254存储迁移保持。迁移夹具用明确不透明bytea原件验证不重写存储，业务Owner场景使用实际序列化规则和回执。

一次作者完整SPEC/QUALITY集中审查覆盖当前权限最终点、同scope锁、独立事务、不可变版本及原输入恢复、历史计数/号码不变、PG迁移守卫和既有入口兼容边界。产品/迁移/测试完整diff已审；未修改原冻结合同或无关迁移，旧菜单BUG独立处理。

Main/Handoff本提交是合并前快照；正常PR、准确远端采用、条件删除任务分支和官方finish释放由本轮最终proof核销。固定目录复用，不删除用户文件或历史保存引用；工作流/依赖未变时复用已核手动触发内容。本轮未运行/取消Actions、未改触发或保护、未部署生产。

Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/233 已准确采用、分支删除和DEV-2释放。Main最终按本轮PR/finish核销。第二期D1准确关闭WP03-01，D2持久Expired，D3限定规则治理；原375卡27DONE/2IN_PROGRESS/346PLANNED、原59局部卡余56。CRM已匿名化完整PG过期链路/新事件消费者及编号全消费者/全局域/CodeGen仍明确未完成。
