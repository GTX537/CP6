# S01-02 集中审查

审查基线：`4ab979e2f391c5a5645da0d1586781e824a9f8cd`。一次完整任务级独立审查先核规范符合性，再核代码质量，范围为三个授权目录的新增文件；审查时尚未提交，准确源码身份为[green metadata](implementation/green-metadata.json)的五份源码/测试文件SHA-256。

结论：通过，无阻止交付的P0/P1/P2发现。未修改工作树或执行全仓测试。

- NullOnly的值校验与required存在性分离；严格JSON先完成解码后重名检查再调用目标转换器。嵌套层、转义等价键和默认深度64均有实际覆盖。
- 结构相等采用准确数字系数串与BigInteger指数，不经浮点、不展开指数；对象键序、数组顺序、类型标签和长度编码未发现歧义或碰撞。
- 原始字节独立持有，返回防御性副本，不把语义Key当摘要、签名或重放格式。
- 原21固定case ID直接消费已冻结的输入与预期；green TRX实为77执行/77通过/0跳过，red及red-final各为76个未实现桩失败和1个运行时断言通过。
- 七份来源pin匹配Git blob；五份当前源码/测试文件与三份构建产物摘要匹配green报告；测试输出的vectors.json和dispatch.json与仓库原文件逐字节相同。
- 测试项目为net8.0，依赖版本与仅引用CP6.Entity的范围准确。既有项目、共用输入和共享状态无改动。

审查另在TEMP使用现有程序集与.NET8.0.29定向检查外部JsonElement含非法UTF-8的路径：GetRawText抛InvalidOperationException，未被替换后形成错误的语义键。该探针不计入77项叶子测试统计。

已披露限制继续保留：深层Key可能重复复制；目标反序列化受目标合同、自定义转换器及调用者显式MaxDepth约束。未验证业务Owner、跨仓消费者采用、数据库或生产验收；通过审查不等于COORD已完成共享状态同步、合并或远端main包含性核对。

收尾只对最终证据、链接、白名单差异及提交字节做检查，不重复运行输入未变的业务/全仓验证。日志/TRX虽被根忽略规则匹配，实际交付将显式纳入，不省略失败记录。

## 独立探针的复现范围

原探针使用TEMP中的net8.0 console项目，直接引用green产物`CP6.Entity.dll`，执行`dotnet run --project <TEMP>/cp6-s01-02-independent-review/Probe.csproj`，退出码0、运行时8.0.29。原输出仅保留在审查工具记录，未伪称存在独立日志文件。复现核心代码如下：

```csharp
using System.Text.Json;
using CP6.Entity.Contracts.Wire;
Console.WriteLine(Environment.Version);
foreach (var input in new[] {
    new byte[] { 34, 255, 34 }, new byte[] { 34, 254, 34 },
    new byte[] { 123, 34, 255, 34, 58, 49, 125 } })
{
    try {
        using var d = JsonDocument.Parse(input);
        Console.WriteLine("Parse accepted");
        Console.WriteLine("Raw=" + d.RootElement.GetRawText());
        Console.WriteLine("Key=" + JsonSemanticEquality.Key(d.RootElement));
    }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}
```

三个输入均先输出`Parse accepted`，随后为`InvalidOperationException: Cannot transcode invalid UTF-8 JSON text to UTF-16 string.`。异常发生在显式GetRawText，后续Key未执行；结合被审查Key代码同样先调用GetRawText，得到不会发生替换后键碰撞的结论。这是定向框架探针加代码分析，不扩称所有外部DOM情形已运行。
