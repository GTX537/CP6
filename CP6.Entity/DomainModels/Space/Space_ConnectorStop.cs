using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>连接体在某楼层的落点（楼层局部坐标 mm）。一连接体每层至多一落点（Space P4）。</summary>
[Table("Space_ConnectorStop")]
public class Space_ConnectorStop : BaseBizEntity, IAuditable
{
    public Guid ConnectorId { get; set; }
    public Guid FloorId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}
