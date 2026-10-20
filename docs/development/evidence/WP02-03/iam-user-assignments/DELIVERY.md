# W28-01 用户分配：单份交付

2026-10-20，COORD单聊天。基线85fd27e3bf619a541a9fece2be32d6dbf8a89f53，任务分支codex/w28-01；关联[BUG346](https://github.com/GTX537/CP6/issues/346)。本地可合并：Yes；正常PR/远端采用及席位处置由本轮总控接续记录，候选不等于已上线。

## 改动与合同

按S5 SYS-IAM-01 v0.2 §3补齐Main用户主/附角色、部门/经理同tenant有效引用。主角色必须属于角色集合；停用历史角色可保留、不能新授；部门必须有有效祖先和准确路径；经理不能自引或成环。新增白名单继续拒绝绑定平台/认证自有字段。普通空密码保留原hash；明确reset经原服务，将refresh吊销、强审计、IdentityChange/Outbox、准确操作回执同PG提交。

复用既有认证root，等锁后重新核验实际会话、当前主体/权限及tenant；实际SQL引用writer bump revision，包含ABA。用户版本和保守tenant级引用CAS同时比较；不相关引用变化也可能要求重新确认。部门revision迁移是后续组织命令的基础，不声称本窗完成组织管理。

新增/api/iam/users编辑资料/创建/修改/主附角色/组织及原操作查询、关闭。旧用户/角色/用户组织入口采用相同版本合同，缺envelope受控拒绝；PG无版本服务写入关闭。用户硬删除待隐私/保留权威，明确409，不级联破坏历史；可停用。缺实际IdentityCapture runtime返回503，不伪造身份传播。配置/迁移部署仍属环境采用任务。

用户页面一次保存资料和主附角色；412保留非敏感草稿，密码清空，重载后明确重新确认；未知提交只查/关闭原operation。跨Tab Web Locks绑定tenant/actor，存储只含公开操作定位，不含密码或业务输入；身份变化清草稿并丢弃迟到响应。结果仅表示Main提交，未宣称CRM消费成功。

## 实际验证与复用

证据根D:/CP6-local-test/wave28/foundation；日志/TRX原件保留。全部为本机隔离PG18、实际Program HTTP或受控前端夹具，未运行真实租户/CRM业务验收。

- 定向.NET/PG/HTTP最终有效覆盖 **70个唯一用例通过**，0剩余失败/跳过。复用同输入成功结果，不把多次运行相加；来源：iam-user-http-audit-20261020T043102Z, iam-user-reset-20261020T043432Z, iam-user-existing-contracts-20261020T044013Z, iam-user-survivor-review-20261020T044212Z, iam-user-migration-review-20261020T044340Z。其中existing-contracts首次13过/1失败是重复菜单键夹具，survivor-review修正夹具后3/3覆盖该失败及原安全断言。
- 最初红测iam-user-red-20261020T041236Z真实复现无版本失效角色保存；iam-user-http首次实际中间件日志泄露密码失败，修正IAM及旧用户请求体日志排除，audit复查通过。编译/浏览器定位失败保留，未冒充通过。
- 实际PG覆盖角色/跨tenant/部门祖先/经理引用、主附角色和身份Outbox同事务、ABA/并发writer、失权重验、显式密码reset与吊销回滚、回执失败全回滚、提交ack丢失原回执/关闭屏障、旧路由/CSRF/会话、末名平台管理员保护、已有部门数据前向安装不改写原树。
- 相关Vitest 7/7（iam-user-final-component），实际vue-tsc --build通过（iam-user-final-typecheck）；首次--noEmit空references命令不计有效验证。
- 实际Chrome页面夹具2/2（iam-user-browser-label）：主附角色合并请求，跨Tab占用、断网后只查/关闭原操作。初次冷加载及label定位失败保留；修正精确aria-label后通过，截图已检查。
- W27已合并认证跨Tab/SSO批准/首次身份映射证据按未变范围复用，本窗不重跑整套。未运行全仓测试、SQL Server、Actions、真实DEV、生产或CRM投影；既有NU1902/NU1903依赖告警未扩展无关升级。

## 一次作者SPEC/QUALITY与处置

逐项检查当前主线完整范围、白名单/正确tenant、root后权限、版本/准确原回执、原子身份/audit、密码日志与持久化、旧入口适配、平台末名保护、页面重放/身份切换和迁移。发现的日志泄露、旧测试会话/版本合同与最后管理员HTTP夹具问题均修正并定向复查，无剩余阻断。未删除安全断言或跳过失败。

分支处置：候选待正常PR合并后由manage finish准确删除本地/远端任务分支；工作区：DEV-1固定目录复用；席位：本报告提交时W28-01仍占用，完成远端核对/进程停止/finish后才释放。DEV-2仅本轮Handoff。最终合并与释放结果见本轮计划、Handoff及实时登记。

W28-02组织树/影响/引用及W28-03对应前端继续；完整IAM（含角色全生命周期/隐私保留）、CRM/Owner、真实业务及发布均未完成/未提升。
