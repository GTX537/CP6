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
