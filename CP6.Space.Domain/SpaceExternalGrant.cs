namespace CP6.Space.Domain;

public enum SpaceExternalGrantStatus : short
{
    Active = 0,
    Suspended = 1,
    Revoked = 2,
}

public sealed class SpaceExternalGrant : SpaceTenantEntity
{
    private SpaceExternalGrant()
    {
    }

    public Guid OrganizationId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid? FieldPolicyId { get; private set; }
    public bool CanExport { get; private set; }
    public DateTime ValidFromUtc { get; private set; }
    public DateTime? ValidToUtc { get; private set; }
    public SpaceExternalGrantStatus Status { get; private set; }
    public long GrantVersion { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceExternalGrant Create(
        Guid tenantId,
        Guid organizationId,
        Guid siteId,
        Guid? fieldPolicyId,
        bool canExport,
        DateTime validFromUtc,
        DateTime? validToUtc,
        SpaceExternalGrantStatus status)
    {
        RequireId(organizationId, nameof(organizationId));
        RequireId(siteId, nameof(siteId));
        if (fieldPolicyId == Guid.Empty)
            throw new ArgumentException(
                "Field policy identity cannot be empty.",
                nameof(fieldPolicyId));
        if (status == SpaceExternalGrantStatus.Revoked)
        {
            throw new SpaceExternalAccessStateException(
                "A grant must be created as Active or Suspended.");
        }
        ValidateValidity(validFromUtc, validToUtc);

        var grant = new SpaceExternalGrant
        {
            OrganizationId = organizationId,
            SiteId = siteId,
            FieldPolicyId = fieldPolicyId,
            CanExport = canExport,
            ValidFromUtc = validFromUtc,
            ValidToUtc = validToUtc,
            Status = status,
            GrantVersion = 1,
        };
        grant.SetTenant(tenantId);
        return grant;
    }

    public void Update(
        Guid siteId,
        Guid? fieldPolicyId,
        bool canExport,
        DateTime validFromUtc,
        DateTime? validToUtc,
        SpaceExternalGrantStatus status)
    {
        if (Status == SpaceExternalGrantStatus.Revoked)
        {
            throw new SpaceExternalAccessStateException(
                "A revoked external grant cannot be changed.");
        }
        RequireId(siteId, nameof(siteId));
        if (fieldPolicyId == Guid.Empty)
            throw new ArgumentException(
                "Field policy identity cannot be empty.",
                nameof(fieldPolicyId));
        ValidateValidity(validFromUtc, validToUtc);

        SiteId = siteId;
        FieldPolicyId = fieldPolicyId;
        CanExport = canExport;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        Status = status;
        GrantVersion = checked(GrantVersion + 1);
    }

    private static void ValidateValidity(
        DateTime validFromUtc,
        DateTime? validToUtc)
    {
        RequireUtc(validFromUtc, nameof(validFromUtc));
        if (!validToUtc.HasValue)
            return;
        RequireUtc(validToUtc.Value, nameof(validToUtc));
        if (validToUtc.Value <= validFromUtc)
        {
            throw new ArgumentException(
                "Grant ValidToUtc must be later than ValidFromUtc.");
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                $"{parameterName} must be UTC.",
                parameterName);
        }
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException(
                $"{parameterName} is required.",
                parameterName);
    }
}

public abstract class SpaceExternalGrantScope : SpaceTenantEntity
{
    public Guid GrantId { get; private set; }

    protected void InitializeScope(Guid tenantId, Guid grantId)
    {
        if (grantId == Guid.Empty)
            throw new ArgumentException("Grant is required.", nameof(grantId));
        SetTenant(tenantId);
        GrantId = grantId;
    }

    public void Retire() => MarkEntityDeleted();
}

public sealed class SpaceExternalGrantFloor : SpaceExternalGrantScope
{
    private SpaceExternalGrantFloor()
    {
    }

    public Guid FloorLogicalId { get; private set; }

    public static SpaceExternalGrantFloor Create(
        Guid tenantId,
        Guid grantId,
        Guid floorLogicalId)
    {
        if (floorLogicalId == Guid.Empty)
            throw new ArgumentException(
                "Floor is required.",
                nameof(floorLogicalId));
        var scope = new SpaceExternalGrantFloor
        {
            FloorLogicalId = floorLogicalId,
        };
        scope.InitializeScope(tenantId, grantId);
        return scope;
    }
}

public sealed class SpaceExternalGrantZone : SpaceExternalGrantScope
{
    private SpaceExternalGrantZone()
    {
    }

    public Guid ZoneLogicalId { get; private set; }

    public static SpaceExternalGrantZone Create(
        Guid tenantId,
        Guid grantId,
        Guid zoneLogicalId)
    {
        if (zoneLogicalId == Guid.Empty)
            throw new ArgumentException(
                "Zone is required.",
                nameof(zoneLogicalId));
        var scope = new SpaceExternalGrantZone
        {
            ZoneLogicalId = zoneLogicalId,
        };
        scope.InitializeScope(tenantId, grantId);
        return scope;
    }
}

public sealed class SpaceExternalGrantOwner : SpaceExternalGrantScope
{
    private SpaceExternalGrantOwner()
    {
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string NormalizedOwnerId { get; private set; } = string.Empty;

    public static SpaceExternalGrantOwner Create(
        Guid tenantId,
        Guid grantId,
        string ownerId)
    {
        var value = SpaceExternalGrantScopeValue.RequireText(
            ownerId,
            100,
            nameof(ownerId));
        var scope = new SpaceExternalGrantOwner
        {
            OwnerId = value,
            NormalizedOwnerId = value.ToUpperInvariant(),
        };
        scope.InitializeScope(tenantId, grantId);
        return scope;
    }
}

public sealed class SpaceExternalGrantObject : SpaceExternalGrantScope
{
    private SpaceExternalGrantObject()
    {
    }

    public string BusinessObjectType { get; private set; } = string.Empty;
    public string NormalizedBusinessObjectType { get; private set; } =
        string.Empty;
    public string BusinessObjectId { get; private set; } = string.Empty;
    public string NormalizedBusinessObjectId { get; private set; } =
        string.Empty;

    public static SpaceExternalGrantObject Create(
        Guid tenantId,
        Guid grantId,
        string businessObjectType,
        string businessObjectId)
    {
        var type = SpaceExternalGrantScopeValue.RequireText(
            businessObjectType,
            50,
            nameof(businessObjectType));
        var id = SpaceExternalGrantScopeValue.RequireText(
            businessObjectId,
            200,
            nameof(businessObjectId));
        var scope = new SpaceExternalGrantObject
        {
            BusinessObjectType = type,
            NormalizedBusinessObjectType = type.ToUpperInvariant(),
            BusinessObjectId = id,
            NormalizedBusinessObjectId = id.ToUpperInvariant(),
        };
        scope.InitializeScope(tenantId, grantId);
        return scope;
    }
}

internal static class SpaceExternalGrantScopeValue
{
    internal static string RequireText(
        string? value,
        int maxLength,
        string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"{parameterName} must contain 1 to {maxLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
