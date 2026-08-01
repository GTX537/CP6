namespace CP6.Space.Domain;

public enum SpaceExternalOrganizationType : short
{
    Customer = 0,
    Supplier = 1,
    ThirdPartyLogistics = 2,
}

public enum SpaceExternalOrganizationStatus : short
{
    Active = 0,
    Suspended = 1,
    Closed = 2,
}

public enum SpaceExternalMembershipRole : short
{
    Viewer = 0,
    OperationsViewer = 1,
    OrgAdmin = 2,
}

public enum SpaceExternalMembershipStatus : short
{
    Invited = 0,
    Active = 1,
    Suspended = 2,
    Revoked = 3,
}

public sealed class SpaceExternalOrganization : SpaceTenantEntity
{
    private SpaceExternalOrganization()
    {
    }

    public SpaceExternalOrganizationType Type { get; private set; }
    public string? BusinessPartnerType { get; private set; }
    public Guid? BusinessPartnerId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public SpaceExternalOrganizationStatus Status { get; private set; }
    public long SecurityStamp { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceExternalOrganization Create(
        Guid tenantId,
        SpaceExternalOrganizationType type,
        string code,
        string name,
        string? businessPartnerType = null,
        Guid? businessPartnerId = null,
        SpaceExternalOrganizationStatus status =
            SpaceExternalOrganizationStatus.Active)
    {
        var organization = new SpaceExternalOrganization();
        organization.SetTenant(tenantId);
        organization.Type = type;
        organization.SetIdentity(code, name);
        organization.SetBusinessPartner(
            businessPartnerType,
            businessPartnerId);
        organization.Status = status;
        organization.SecurityStamp = 1;
        return organization;
    }

    public void Update(
        string code,
        string name,
        string? businessPartnerType,
        Guid? businessPartnerId,
        SpaceExternalOrganizationStatus status)
    {
        if (Status == SpaceExternalOrganizationStatus.Closed &&
            status != SpaceExternalOrganizationStatus.Closed)
        {
            throw new SpaceExternalAccessStateException(
                "A closed external organization cannot be reopened.");
        }

        SetIdentity(code, name);
        SetBusinessPartner(businessPartnerType, businessPartnerId);
        Status = status;
        IncrementSecurityStamp();
    }

    public void TouchMembershipSecurityStamp()
    {
        if (Status == SpaceExternalOrganizationStatus.Closed)
        {
            throw new SpaceExternalAccessStateException(
                "A closed external organization cannot change memberships.");
        }

        IncrementSecurityStamp();
    }

    private void SetIdentity(string code, string name)
    {
        Code = RequireText(code, 50, nameof(code));
        NormalizedCode = Code.ToUpperInvariant();
        Name = RequireText(name, 200, nameof(name));
    }

    private void SetBusinessPartner(
        string? businessPartnerType,
        Guid? businessPartnerId)
    {
        if (string.IsNullOrWhiteSpace(businessPartnerType) !=
            !businessPartnerId.HasValue)
        {
            throw new ArgumentException(
                "Business partner type and identity must be supplied together.");
        }

        if (!businessPartnerId.HasValue)
        {
            BusinessPartnerType = null;
            BusinessPartnerId = null;
            return;
        }

        if (businessPartnerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Business partner identity cannot be empty.",
                nameof(businessPartnerId));
        }

        BusinessPartnerType = RequireText(
            businessPartnerType!,
            50,
            nameof(businessPartnerType));
        BusinessPartnerId = businessPartnerId;
    }

    private void IncrementSecurityStamp()
    {
        SecurityStamp = checked(SecurityStamp + 1);
    }

    private static string RequireText(
        string value,
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

public sealed class SpaceExternalMembership : SpaceTenantEntity
{
    private SpaceExternalMembership()
    {
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public SpaceExternalMembershipRole Role { get; private set; }
    public DateTime ValidFromUtc { get; private set; }
    public DateTime? ValidToUtc { get; private set; }
    public SpaceExternalMembershipStatus Status { get; private set; }
    public Guid? InvitedBy { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public long SecurityStamp { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceExternalMembership Create(
        Guid tenantId,
        Guid organizationId,
        Guid userId,
        SpaceExternalMembershipRole role,
        DateTime validFromUtc,
        DateTime? validToUtc,
        SpaceExternalMembershipStatus status,
        Guid? invitedBy,
        DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException(
                "Organization is required.",
                nameof(organizationId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (status is not (
                SpaceExternalMembershipStatus.Invited or
                SpaceExternalMembershipStatus.Active))
        {
            throw new SpaceExternalAccessStateException(
                "A membership must be created as Invited or Active.");
        }

        RequireUtc(nowUtc, nameof(nowUtc));
        ValidateValidity(validFromUtc, validToUtc);

        var membership = new SpaceExternalMembership
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            ValidFromUtc = validFromUtc,
            ValidToUtc = validToUtc,
            Status = status,
            InvitedBy = invitedBy == Guid.Empty ? null : invitedBy,
            AcceptedAtUtc = status == SpaceExternalMembershipStatus.Active
                ? nowUtc
                : null,
            SecurityStamp = 1,
        };
        membership.SetTenant(tenantId);
        return membership;
    }

    public void Update(
        SpaceExternalMembershipRole role,
        DateTime validFromUtc,
        DateTime? validToUtc,
        SpaceExternalMembershipStatus status,
        DateTime nowUtc)
    {
        if (Status == SpaceExternalMembershipStatus.Revoked)
        {
            throw new SpaceExternalAccessStateException(
                "A revoked external membership cannot be changed.");
        }

        RequireUtc(nowUtc, nameof(nowUtc));
        ValidateValidity(validFromUtc, validToUtc);
        EnsureTransition(Status, status);

        Role = role;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        if (Status != SpaceExternalMembershipStatus.Active &&
            status == SpaceExternalMembershipStatus.Active &&
            AcceptedAtUtc is null)
        {
            AcceptedAtUtc = nowUtc;
        }
        Status = status;
        SecurityStamp = checked(SecurityStamp + 1);
    }

    private static void EnsureTransition(
        SpaceExternalMembershipStatus current,
        SpaceExternalMembershipStatus next)
    {
        var allowed = current switch
        {
            SpaceExternalMembershipStatus.Invited => next is
                SpaceExternalMembershipStatus.Invited or
                SpaceExternalMembershipStatus.Active or
                SpaceExternalMembershipStatus.Suspended or
                SpaceExternalMembershipStatus.Revoked,
            SpaceExternalMembershipStatus.Active => next is
                SpaceExternalMembershipStatus.Active or
                SpaceExternalMembershipStatus.Suspended or
                SpaceExternalMembershipStatus.Revoked,
            SpaceExternalMembershipStatus.Suspended => next is
                SpaceExternalMembershipStatus.Suspended or
                SpaceExternalMembershipStatus.Active or
                SpaceExternalMembershipStatus.Revoked,
            SpaceExternalMembershipStatus.Revoked =>
                next == SpaceExternalMembershipStatus.Revoked,
            _ => false,
        };

        if (!allowed)
        {
            throw new SpaceExternalAccessStateException(
                $"Membership transition {current} -> {next} is not allowed.");
        }
    }

    private static void ValidateValidity(
        DateTime validFromUtc,
        DateTime? validToUtc)
    {
        RequireUtc(validFromUtc, nameof(validFromUtc));
        if (validToUtc.HasValue)
        {
            RequireUtc(validToUtc.Value, nameof(validToUtc));
            if (validToUtc.Value <= validFromUtc)
            {
                throw new ArgumentException(
                    "Membership ValidToUtc must be later than ValidFromUtc.");
            }
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
}

public sealed class SpaceExternalAccessStateException :
    InvalidOperationException
{
    public SpaceExternalAccessStateException(string message) : base(message)
    {
    }
}
