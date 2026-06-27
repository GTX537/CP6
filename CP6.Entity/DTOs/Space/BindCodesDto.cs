namespace CP6.Entity.DTOs.Space;

/// <summary>D7 绑码请求（ch01 §I-1）</summary>
public class BindCodesDto
{
    public List<BindCodePairDto> Pairs { get; set; } = new();
}

/// <summary>单个库位绑码映射</summary>
public class BindCodePairDto
{
    public Guid LocationId { get; set; }
    public int Col { get; set; }
    public int Level { get; set; }
    public int Depth { get; set; }
}
