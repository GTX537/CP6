# WP02-02 补充独立审查

**PASS — 可以 PREPARATION_ONLY 范围合并。P1 0 项，P2 0 项。** WP02-02 仍为 **BLOCKED / NOT_CHECKED**；本结论不代表正式发布、包信任、Main真实采用、PGT通过或主任务完成。

候选 `f395d92ab8e15a5a06d00deeb2f47a9ec583baaf`，Main 基线 `1ab2d031e3c501662ecb89a33839fac1cba55d33`。工作树 `D:/CP6-worktrees/wp02-02-prep-20261013` 开始及结束均干净，HEAD一致。本报告补充COORD的完整源码/提案阅读，独立核查SyntheticRuntime、两组Python测试、输入边界、生成证据与真实归档来源；没有重启原测试循环。

## 字节和来源核对

125个新增文件全部位于获准的 `eng/wp02-02/consumption/` 和 `docs/development/evidence/WP02-02/consumption-preparation/`；每份文件候选Git字节与工作树一致，JSON可解析，Python AST可解析。生产项目/锁/NuGet/Program/DI/EF/迁移、原台账与完成门均未修改。候选完整diff检查通过；常见私钥/PAT/访问密钥模式无命中。仅写指定scratch报告，没有源码、Git、远端、Actions或数据库变更。

- 17个 `final-verification.sourceHashes` 全部与候选字节一致。
- 87份保留原件的原始文件、候选副本、清单hash和长度全部一致，共2,311,280字节。早期RED/错误报告未被清除。
- 45个准入指纹（含Git blob ID）及100项项目/依赖输入均重新读取准确Main/Platform Git对象，hash相符。这里不是35项，也不能把重叠视图相加为业务验收数量。
- 原正式publication与本地consumer-lock从Platform `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`读取，原始字节与候选副本分别匹配 `17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181` 和 `1fabaecefb24050d801a9fd894dcd908b8428f7bd486fe4d0a634ad97ffe0b01`。旧七包检查用的派生approval逐项等于原publication身份，没有由被检包反向制造期待值。
- 实际14个nupkg均符合独立原记录SHA256、输入manifest和原始archive SHA512；32份schema（旧15、本地17）与各自producer Git原始字节完全一致，未规范化BOM/换行。包路径、source/version、归档/schema hash全部写入同名JSON。
- a3报告列出的52个产物hash全部对已保留文件或仍存在的原始本地产物核对。四个被使用的DLL另从实际包读取并核hash，公共签名记录的loaded DLL身份与之相符。未加载或执行归档代码。

Platform当前阅读基线与实际包producer严格分开：旧正式0.10.2为 `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，旧本地wp02.27为 `7c59256e13c217fc0e8a66b8f3f46905222fdcbc`。没有把旧本地包说成当前集成main的产物。一次内部只读检查最初假设不存在的 `D:/CP6.Platform` 路径而中断，改用现存wp02-27工作树的Git对象后完成；这不是候选验证失败，未修改来源。

## 输入与合成运行边界

verify.py强制准备模式和封闭输入、准确七producer/五runtime集合、独立hash、schema集合/字节、来源/版本/feed和受支持namespace；重复/畸形/浮动或混版输入、未知能力/兼容、静态伪信任均受拒绝检查。ZIP检查不解压，限制大小/数量/压缩比，拒绝路径/特殊文件/重复项；JSON重复键/非有限数及XML DTD受拒绝。`admission=BLOCKED`、`entryGate=NOT_CHECKED`和trust阻塞/host-runtime-business NOT_RUN没有成功旁路，CLI固定exit2。

run_probe.py的独立hash文档只允许本地检查；在七包hash/nuspec来源版本匹配后仅取四个context DLL，输出须新目录。私有props/targets/NuGet.Config隔离产品，配置无feed；没有机器专属生产ProjectReference。Program的metadata-only检查不加载被检DLL；runtime明确进行已知实际DLL的局部合成调用并核loaded hash。旧包缺20个所需类型的PASS只表示元数据检查完成。

SyntheticRuntime的verifier/authority从固定fixture tenant/time/registration产生数据，没有真实principal/权限注册或Owner查询。10场景为：不可公开构造/设置可信context、DI/middleware成功及finally/captured-context清理、legacy不产生trusted、tenant mismatch、缺source、authority不可得、取消前/authority后、handler异常及message executor。所有结果逐条标SYNTHETIC_FIXTURE，runtime输出同样固定formalAdmission BLOCKED、Main host/business NOT_RUN。被调用的实际包实现可证明这些局部路径，但不能证明Main鉴权/租户/数据库或正式trust。

构建的MSB3277（DiagnosticSource 8/10）原日志和报告保留；完整包依赖兼容性仍Unknown。没有抑制warning或将10个局部成功扩大为兼容资格化。

## 原结果复用

本次仅做只读身份、完整集合、AST与差异检查。未重新运行下列checker/parser/probe/捕获工具或数据库测试。

| 原证据 | 可复用结果 | 与当前候选的绑定及限制 |
|---|---|---|
| checker-namespace-green-20261014 | 44测试PASS | 最终verify和test_verify hash相同；原日志/exit0相符；全部是合成plumbing |
| root-validation/probe-parser-tests | 8测试PASS | test_runner与run_probe hash相同；原命令为Python3.13，2026-10-14T05:41:03.229193至05:41:03.688308Z；非真实发布正例 |
| probe-20261014-a3 | 10合成场景PASS | runner和8份probe源hash一致，52产物核对；SDK8.0.424/.NET8.0.30；真实Main与完整依赖未验 |
| namespace-green旧正式/本地报告 | 均exit2/BLOCKED | 最终checker和两个输入hash一致；旧正式缺ctx，旧local不满足正式feed/publication；Current未跑 |
| optimized-input-check | python -O错误来源hash拒绝 | capture脚本最终hash一致；只是错误输入定向证据，不是重新采集或正式采用 |

原44测试日志未保存精确执行时钟，报告不补造时间。完整命令/已知时点、源码、包/制品hash、环境和适用范围见同名JSON `evidence_reuse`。初始31、41及namespace RED与旧误拒报告按其历史版本保留；只有namespace-green是最终checker结果。资料采集/inventory由assert改显式ValueError后未伪称重跑原成功采集，本次独立原字节比对支持正确输入结果继续复用。

## 未完成事项

原17 PGT的完整actor/preconditions/request/steps/expected/evidence_required与固定原文件逐项相等，全部NOT_RUN、execution_evidence=[]。没有将其替换成10个探针场景，也没有满足WP02-28。依赖清单为51项目、19含Platform锁，其中16份未来需真正restore生成，3份历史P10 0.10.1工具锁独立排除；WebApi实际静态运行闭包是五包。早期inventory分类保留，final为提案范围依据。

后续必须分别完成：COORD准备交付状态同步、正常合并及远端main核对；最终producer和0.10.3登记独占窗口；具体正式release的新增授权；受保护批准、one-build、RFC3161/当前撤销、原字节immutable readback；真实Main consumer身份与完整依赖兼容、16锁生成；真实host/verifier/authority/worker/storage和17项PostgreSQL验收。0.10.3仅为提案且不是预留，本轮未授权或运行任何pipeline；源码采用例外仍未批准。

没有独立Current密码学验证、实时feed认证/查回、真实Main宿主/Owner/PG/并发迁移恢复运行或生产业务验收。远端工作流的新鲜检查由COORD在集成时处理，本次无Actions触发、重跑、取消或配置修改。这些明确保留的后续阻塞不阻止准备资料合并，也不构成WP02-02完成。
