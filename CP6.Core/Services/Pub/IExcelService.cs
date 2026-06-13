namespace CP6.Core.Services.Pub;

/// <summary>通用 Excel 导入导出 —— PUB 章07。列配置驱动；导出过字典翻译；导入逐行校验+错误回写。</summary>
public interface IExcelService
{
    /// <summary>按列配置导出数据（DictType 列翻译为标签、Format 列格式化）。</summary>
    Task<byte[]> ExportAsync<T>(IEnumerable<T> data, List<ExcelColumn> cols);

    /// <summary>生成导入模板（仅 Import 列；必填标题加 * 标红）。</summary>
    byte[] Template(List<ExcelColumn> cols);

    /// <summary>导入：列匹配 + 必填/字典校验 + mapper 业务转换；有错产出标红错误文件。</summary>
    Task<ImportResult<T>> ImportAsync<T>(Stream excel, List<ExcelColumn> cols,
        Func<Dictionary<string, string?>, T> mapper) where T : class;
}
