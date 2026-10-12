# S01-02 严格 JSON、NullOnly、结构相等

本任务实现Main的`CP6.Entity.Contracts.Wire`边界工具，消费已交付`CP6-S01-CONTRACT-INPUTS/1.0.0`中的3个profile/21条向量。共用输入保持原版本、摘要和NOT_RUN字段；真实局部运行证据单独保存于本目录。协调分配、精确来源和步骤见[准入检查](entry-check.json)及[实施计划](PLAN.md)。

[交付报告](delivery.md)固定实现提交及责任窗口；[运行结果](validation.json)、[源码提交字节核对](source-receipt.json)和[独立审查](review.md)分别记录实际适用范围，远端main交付由COORD确认。

## 提供的能力

- `StrictJson.Parse(ReadOnlySpan<byte>)`先复制原字节，校验UTF-8及JSON语法，再按解码后的属性名逐对象拒绝重复键。比较区分大小写，不做Unicode正规化；任何嵌套层及转义等价键均适用。
- `StrictJson.Deserialize<T>`以及解析结果的`Deserialize<T>`在严格预检完成后调用目标反序列化器。`NullOnly`只接受null；必填字段仍须声明`required`或`JsonRequired`，不能仅靠转换器推断存在性。
- `JsonSemanticEquality.AreEqual`与`Key`用于Schema的const/enum/uniqueItems语义。数字采用准确规范化十进制系数串和BigInteger指数；对象键序无关，数组顺序相关，不同JSON类型不混同。
- `StrictJsonDocument.GetOriginalUtf8()`返回原字节的独立副本，保留空白、转义和数值写法。结构相等的Key只是在进程内比较用的不透明值，不是摘要、签名、持久化或命令重放编码协议。

示例（目标合同及业务授权仍由实际消费者定义）：

```csharp
using CP6.Entity.Contracts.Wire;
using System.Text.Json.Serialization;

using var input = StrictJson.Parse(utf8Bytes);
var request = input.Deserialize<ExampleContract>();
byte[] originalWire = input.GetOriginalUtf8();

public sealed class ExampleContract
{
    [JsonPropertyName("fixedNull")]
    public required NullOnly FixedNull { get; init; }
}
```

解析结果拥有JsonElement的生命周期，使用结束后Dispose；需要独立生命周期时克隆元素。原始数据应以接收的UTF-8字节进入边界，不能先做宽松解析或字符串替换后再声称验证了原报文。

## 本地验证

实际SDK为10.0.302，叶子项目目标及测试进程为net8.0 / .NET8.0.29。叶子项目只引用CP6.Entity及指定的Test SDK17.14.1、xunit2.9.3、runner3.1.4，未编辑既有项目或解决方案。

```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~StrictJsonTests
# 若需保留新一轮控制台/TRX/源码和制品摘要，用新的唯一Stage名，避免覆盖已有记录：
pwsh -File docs/development/evidence/S01-02/implementation/run-tests.ps1 -Stage <新的唯一名称>
```

已保存的测试过程：

| 阶段 | 实际结果 | 证据 |
|---|---|---|
| RED | 77执行，76因未实现接口桩失败，1环境断言通过 | [日志](implementation/red.log)、[TRX](implementation/red.trx)、[源码/制品摘要](implementation/red-metadata.json) |
| RED-final | 接受JsonException子类型的测试修正后仍76失败、1通过；尚未实现生产逻辑 | [日志](implementation/red-final.log)、[TRX](implementation/red-final.trx)、[摘要](implementation/red-final-metadata.json) |
| GREEN | 77通过、0失败、0跳过；无编译警告 | [日志](implementation/green.log)、[TRX](implementation/green.trx)、[摘要](implementation/green-metadata.json) |

21条原向量按原ID逐项执行，另外56条覆盖无效语法、invalid UTF-8、转义/Unicode名称、转换器调用顺序、required/optional区别、原字节所有权、2048层嵌套、巨大正负指数和实际运行时。编译失败或零测试运行没有冒充RED/通过。初次证据脚本的目录错误发生在dotnet启动前，事实和修正保留于[实施记录](implementation/SELF-REVIEW.md)。

运行时源码尚未提交，报告中的base_head仅是基线；准确测试输入是每个文件的SHA-256。后续交付提交必须逐项匹配这些字节，不能把未执行过的后续SHA当成本次执行HEAD。结果复用只限源码、依赖及环境输入未变的范围。

## 明确边界

严格读取与结构相等不新增应用级长度/深度上限；读取深度显式设置为Int32.MaxValue，遍历使用堆栈。连续数组、字符串、框架元数据和可用内存仍有限，未承诺无限输入或未经基准测试的吞吐能力。嵌套结构Key存在重复复制开销；部署入口的资源预算没有在本任务中改动。

转义的孤立UTF-16 surrogate在严格扫描和结构相等中按原code unit区分，不替换成U+FFFD。目标System.Text.Json字符串转换器/字符串访问器可能拒绝此类值；语法解析通过不等于任意业务目标类型都接受。调用者明确指定的非零MaxDepth和目标转换器语义继续适用，默认64不会无声限制共享入口。

本任务局部通过不代表真实Owner、CRM/Platform采用、页面、权限/事务、PostgreSQL存储或生产验收。台账、生成页、四份共享状态以及串行合并由COORD处理；执行对话仅写分配的三个目录。无Actions启动/取消、触发变更、数据库或部署。
