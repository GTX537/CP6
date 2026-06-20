using CP6.Entity.DomainModels.Fin;
namespace CP6.Core.Services.Fin;

public interface IBankStatementImporter
{
    /// <summary>按 Profile 解析文件流为候选行（不落库）。空行跳过，单行失败收集进 Errors 不中断。</summary>
    BankImportParseResult Parse(BankImportProfile profile, Stream file, string fileName);
}
