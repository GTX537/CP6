namespace CP6.Core.Services.Pub;

/// <summary>
/// Excel 列配置 —— PUB 章07。一份配置驱动导出/模板/导入。
/// </summary>
public class ExcelColumn
{
    /// <summary>实体属性名（反射取值/赋值）</summary>
    public string Field { get; set; } = "";

    /// <summary>列标题</summary>
    public string Title { get; set; } = "";

    /// <summary>字典类型（非空则导出翻译值→标签、导入反查标签→值）</summary>
    public string? DictType { get; set; }

    /// <summary>格式化串（如 yyyy-MM-dd / N2），用于 IFormattable 值导出</summary>
    public string? Format { get; set; }

    /// <summary>导入是否必填</summary>
    public bool Required { get; set; }

    /// <summary>是否参与导入（模板/导入列）。默认 true</summary>
    public bool Import { get; set; } = true;

    /// <summary>列宽</summary>
    public double Width { get; set; } = 15;
}

/// <summary>导入单行错误。</summary>
public class ImportError
{
    /// <summary>Excel 行号（含表头，从 1 起；数据行从 2 起）</summary>
    public int Row { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>导入结果。</summary>
public class ImportResult<T>
{
    public List<T> ValidRows { get; set; } = new();
    public List<ImportError> Errors { get; set; } = new();
    /// <summary>有错时回写的标红错误文件（原表 + "错误原因"列），无错为 null。</summary>
    public byte[]? ErrorFile { get; set; }
    public bool HasError => Errors.Count > 0;
}
