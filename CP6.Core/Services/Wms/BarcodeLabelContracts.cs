namespace CP6.Core.Services.Wms;

public sealed class BarcodeProfileDto
{
    public Guid Id { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Pattern { get; init; } = string.Empty;
    public string MappingJson { get; init; } = "{}";
    public int Priority { get; init; }
    public bool IsEnabled { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class UpsertBarcodeProfileRequest
{
    public Guid? Id { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string Format { get; set; } = "CUSTOM";
    public string Pattern { get; set; } = string.Empty;
    public string MappingJson { get; set; } = "{}";
    public int Priority { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class ParseCompoundBarcodeRequest
{
    public string RawBarcode { get; set; } = string.Empty;
}

public sealed class CompoundBarcodeResult
{
    public bool Matched { get; init; }
    public string? ProfileName { get; init; }
    public string RawBarcode { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>();
}

public sealed class LabelTemplateDto
{
    public Guid Id { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string TemplateBody { get; init; } = string.Empty;
    public string? Language { get; init; }
    public bool IsEnabled { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class UpsertLabelTemplateRequest
{
    public Guid? Id { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string Format { get; set; } = "ZPL";
    public string TemplateBody { get; set; } = string.Empty;
    public string? Language { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class CreateLabelJobRequest
{
    public Guid OperationId { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string? PrinterName { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class LabelJobCommand
{
    public Guid OperationId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string? ResultMessage { get; set; }
}

public sealed class LabelJobDto
{
    public string JobNo { get; init; } = string.Empty;
    public Guid OperationId { get; init; }
    public string WarehouseCd { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string TemplateBody { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = "{}";
    public string? PrinterName { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? RequestedDeviceId { get; init; }
    public string? RequestedBy { get; init; }
    public DateTime RequestedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int AttemptCount { get; init; }
    public string? ResultMessage { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}
