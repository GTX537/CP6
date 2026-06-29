namespace CP6.Entity.DTOs.Space;

public class ConnectorDto
{
    public Guid SiteId { get; set; }
    public string ConnectorCode { get; set; } = string.Empty;
    public int ConnectorType { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
}

public class ConnectorStopDto
{
    public Guid FloorId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public class ConnectorView
{
    public Guid Id { get; set; }
    public string ConnectorCode { get; set; } = string.Empty;
    public int ConnectorType { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ConnectorStopView> Stops { get; set; } = new();
}

public class ConnectorStopView
{
    public Guid FloorId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}
