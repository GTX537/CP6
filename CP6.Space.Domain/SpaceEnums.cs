namespace CP6.Space.Domain;

public enum SpaceModelMode : short
{
    Legacy = 0,
    DesignV1 = 1,
}

public enum SpaceModelCutoverState : short
{
    LegacyOpen = 0,
    FreezeRequested = 1,
    Frozen = 2,
    Bootstrapping = 3,
    Verified = 4,
    DesignV1 = 5,
    FailedFrozen = 6,
}

public enum SpaceVersionStatus : short
{
    Draft = 0,
    Validating = 1,
    Ready = 2,
    Publishing = 3,
    Published = 4,
    Superseded = 5,
    ReconciliationRequired = 6,
}

public enum SpaceSourceType : short
{
    Dwg = 0,
    Dxf = 1,
    Pdf = 2,
    Png = 3,
    Jpg = 4,
    Excel = 5,
    Editor = 6,
    Template = 7,
}

public enum SpaceFileState : short
{
    Uploading = 0,
    Quarantined = 1,
    Scanning = 2,
    Clean = 3,
    Rejected = 4,
    Deleted = 5,
}

public enum SpaceFileRetentionClass : short
{
    Source = 0,
    Artifact = 1,
    Temporary = 2,
}

public enum SpaceSourceState : short
{
    Uploaded = 0,
    Scanning = 1,
    Ready = 2,
    Parsing = 3,
    PreviewReady = 4,
    Imported = 5,
    Rejected = 6,
}

public enum SpaceArtifactType : short
{
    CadIr = 0,
    LayerInventory = 1,
    PreviewSet = 2,
    Thumbnail = 3,
    ExcelErrorReport = 4,
    CanonicalSnapshot = 5,
    SceneChunk = 6,
}
