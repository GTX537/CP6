# BUG419：当前Main身份目录与固定业务函数兼容

Issue：https://github.com/GTX537/CP6/issues/419 。COORD本聊天单人，在W52实际Main→CRM联通时发现；独立 `codex/bug-419-directory-owner-outbox` 从远端核实main `4f3f874fb18183b8bc1a432e981518e377fbabb8` 建立，DEV-1复用，W52总控原分支已推送/finish deferred，CRM DEV-2原任务保留。无额外工作树。

## 问题与修复

最新完整Core迁移含订单/独立入口/Machine部门映射、OEE、Main原操作日志与Stock Outbox共14个原生trigger函数；旧身份目录安装初始白名单没有这些函数，因此合法新库被失败关闭，尚未进入HTTP采集。错误被安装器统一映射为execution-rejected；仅类型/栈诊断定位首个原生布局检查，诊断代码已删除。

仅为这些已冻结迁移添加精确函数签名、规范化CRLF后的prosrc SHA-256、plpgsql/trigger返回类型、固定pg_catalog search_path核对，保留所有原有owner、非SECURITY DEFINER、ACL/未知对象/行安全准入检查。五个业务schema不允许PUBLIC EXECUTE；普通身份目录角色仍无这些函数、业务表/写权限。固定来源为20261020134000、20261022053000、20261022061500、20261022083500、20261022093500、20261023003000；Machine覆盖后的实际mapping_validate定义单独固定。未改历史迁移、业务表、现有部署或全局权限；没有允许任意同名/新函数自动入场。

## 真实验证与审查

6项相关本地PG/.NET通过，无跳过：新增正向准入与七类负向攻击（未知函数、改函数体、SECURITY DEFINER、search_path、PUBLIC授权等）；实际配置Main HTTP普通身份A→B→A、原始PG/HTTP/collector完整集合、目录/快照/末次目录之间写入拒绝及重试、原始1MiB数据精确保留。不同collection使用分别新建的自有PG目录，避免误复用。

回归在修复前实际失败：bug419-red（2项：1pass/1fail）；最初仅准入原操作/Outbox 8函数仍失败，后核齐另外6个映射/OEE函数。一次合并筛选两个PG collection的运行因共同ROOT被夹具拒绝，明确为测试启动配置失败；拆分后成功。所有失败、日志、TRX保留。成功来源：`D:\CP6-local-test\wave52\foundation\bug419-full-known-functions-20261023T015553Z.log`；`D:\CP6-local-test\wave52\foundation\bug419-actual-source-retest-20261023T015719Z.log`。复用同一已构建产品的第二组--no-build结果；不会因提交/合并重跑相同业务验证。

一次完整作者SPEC/QUALITY审查：变更限现行显式目录安装的固定业务函数准入与专项验证；原函数无安全定义提权，指纹封印仍绑定实际目录，异常仍回滚；未增临时日志/机器配置/凭据，未触发Actions。只证明本地新建隔离数据库兼容，不证明CRM整体身份/Owner采用、既有实例升级或生产验收。W52跨仓后续继续。

## 收尾

候选验证已通过；正常PR采用和远端核对后关闭Issue，再manage finish定向删除本任务准确分支并释放DEV-1，resume已保存W52总控原分支。最终远端与席位证据记入该PR/本机bug419-delivery.json与finish记录，七天持续目标保持ACTIVE。
