# W21-01：完整身份目录基线合同与核验器

在首轮目录、每份原始快照和末轮目录全部一致后，产出不可变的SOURCE_SNAPSHOT基线。来源、原UTF-8字节及其SHA-256、类型/版本/删除标记均固定；失败或取消后不能继续发布残缺结果。当前只交付核验组件，真实HTTP/PG读取和采集留W21-02/03/04。

基线 `2c461f0ddea83bd0deabc469d13aeb17c73c5e9d`，分支 `codex/wave21-baseline-contract-20261019`。实际START为Handoff `9ab6f805645fc8b31d0d7633016881c7de071c9c`；同一总控单独实现及一次作者SPEC/QUALITY审查，不声称独立审查。

## 合同与行为

- `cp6-identity-baseline-v1`来源为TenantId、Issuer、Region、ContractBundleSha256、StorageIdentitySha256。预期来源须由可信配置提供；核验器核对已加载runtime/bundle/tenant，逐份快照必须与预期来源完全相同。安装身份摘要不是从首个响应自动信任。
- 版本化目录页包含同一观察的TotalCount；核验器要求首轮及末轮准确数量、aggregate唯一、已知五种事件类型、正int版本及小写SHA-256。opaque boundary只等值比较，允许合法Unicode且不归一化，不作为全局水位排序。完整空目录可形成结果，但不代表授权或具备主体依赖。
- 原data以canonical base64传输，先核对原字节hash，再经严格UTF-8、原JSON重复/敏感属性检查以及已固定事件schema和tenant/aggregate/version/deleted语义核验。只为复用schema构造临时验证载体，不保存/输出其messageId或envelope摘要，不伪造事件接收或撤权时间。
- 每份原data上限1MiB，目录最多100万项，累计原字节默认256MiB；调用者可降低运行预算。临时验证载体的开销不缩小原data合法边界；旧事件运输入口仍保留原1MiB上限。
- 任一Add/Complete错误或取消后该候选关闭，无法补救为部分成功。结果复制并只读封装集合；成员仅含不可变记录/字符串/值类型，无可变JsonElement树或调用者数组别名。
- 内容身份为SHA-256：每个字段严格按UTF-8编码、4字节大端长度前缀，依次Contract/TenantId(D)/Issuer/Region/BundleHash/StorageHash/Boundary/十进制数量，再按Ordinal aggregate排序的aggregate/type/version/hash/deleted(1或0)。原字节hash已验证，因此摘要绑定真实data；原空白变化也改变内容身份。

## 实际验证及复用

证据根 `D:/CP6-local-test/wave21/w21-01/` 保存原log/JSON/TRX：

| 运行 | 真实结果 |
|---|---|
| red-baseline-20261019T042154Z | 新功能RED：1项失败于尚未实现的核验器 |
| initial-baseline-20261019T042413Z | 39通过：基本基线行为1项、现有事件合同38项；无失败/跳过 |
| baseline-boundaries-20261019T042600Z | 23通过、1失败：null aggregate触发Dictionary通用异常；其余有效边界证据保留 |
| input-recheck-20261019T042638Z | 修正空键固定拒绝和合法Unicode边界后局部9通过，0失败/跳过 |

按最终用例去重为25项基线行为及38项事件合同通过，无剩余失败或跳过。没有重复全套测试；原失败保留。局部修正只影响空键拒绝和UTF-8边界接受，复用其他23项边界和38项事件合同成功。覆盖五种类型、原始空白、敏感/重复/损坏JSON、错误hash/源/版本/删除标记、重复/缺项/最终变更、摘要稳定性、只读结果、预算/取消和原1MiB边界。

复现：现有依赖缓存下 `dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter "FullyQualifiedName~IdentityBaselineTests|FullyQualifiedName~CrmIdentityContractTests"`。本项无需PG，未启动数据库/HTTP。

## 集中审查与剩余范围

一次作者SPEC/QUALITY完整diff审查核对S6 §4.5的源/边界/全集协议、原字节与验证载体分离、不可变结果、上限、失败毒化及现有事件合同兼容；空键问题修正并局部复查，无剩余阻断。源端TotalCount补足完整性识别，已同步下一窗口新版本页接口计划；旧接口保持兼容，不改schema或既有profile。

待正常PR合并/远端核对后关闭本有限窗口。W21-02版本化真实读取、W21-03实际HTTP采集和W21-04PG串联仍未完成。完整WP02、全IAM写者覆盖、CRM授权采用、Owner最终写授权门及真实DEV/业务/生产均未提升。无新工作树/代理/Actions/既有数据库操作或部署；共享台账及四状态同窗口同步。
