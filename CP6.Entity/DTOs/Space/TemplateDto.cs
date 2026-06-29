namespace CP6.Entity.DTOs.Space;

/// <summary>模板 CRUD DTO（ch01 §F-1）</summary>
public class TemplateDto
{
    public Guid? Id { get; set; }
    public string TemplateCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    /// <summary>1货架 2库区</summary>
    public int TemplateType { get; set; } = 1;
    /// <summary>参数 JSON 快照</summary>
    public string Params { get; set; } = "{}";
}
