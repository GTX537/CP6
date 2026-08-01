using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceFieldPolicyService(
    SpaceContext context,
    ISpaceExecutionContext execution) : ISpaceFieldPolicyService
{
    private const int MaximumFields = 200;

    public async Task<IReadOnlyList<SpaceFieldPolicyDto>> GetPoliciesAsync(
        string? audienceType,
        string? status,
        CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var query = context.FieldPolicies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(audienceType))
        {
            var parsed = ParseEnum<SpaceExternalOrganizationType>(
                audienceType,
                "audience type");
            query = query.Where(item => item.AudienceType == parsed);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseEnum<SpaceFieldPolicyStatus>(
                status,
                "field policy status");
            query = query.Where(item => item.Status == parsed);
        }

        var policies = await query
            .OrderBy(item => item.AudienceType)
            .ThenBy(item => item.NormalizedName)
            .ToListAsync(cancellationToken);
        var fields = await LoadFieldsAsync(
            policies.Select(item => item.Id).ToArray(),
            cancellationToken);
        return policies.Select(item => ToDto(item, fields)).ToArray();
    }

    public async Task<SpaceFieldPolicyDto> GetPolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var policy = await FindPolicyAsync(
            policyId,
            asTracking: false,
            cancellationToken);
        var fields = await LoadFieldsAsync([policy.Id], cancellationToken);
        return ToDto(policy, fields);
    }

    public async Task<SpaceFieldPolicyDto> CreatePolicyAsync(
        CreateSpaceFieldPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = RequireTenant();
        var fields = NormalizeFields(request.Fields);
        var policy = SpaceFieldPolicy.Create(
            tenantId,
            request.Name,
            ParseEnum<SpaceExternalOrganizationType>(
                request.AudienceType,
                "audience type"),
            request.CanExport);
        context.FieldPolicies.Add(policy);
        AddFields(policy, fields);
        await SaveAsync(cancellationToken);
        return ToDto(policy, fields.Select(ToDto).ToArray());
    }

    public async Task<SpaceFieldPolicyDto> UpdatePolicyAsync(
        Guid policyId,
        UpdateSpaceFieldPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var policy = await FindPolicyAsync(
            policyId,
            asTracking: true,
            cancellationToken);
        var fields = NormalizeFields(request.Fields);
        policy.Update(
            request.Name,
            request.CanExport,
            ParseEnum<SpaceFieldPolicyStatus>(
                request.Status,
                "field policy status"));
        var current = await context.FieldPolicyFields
            .Where(item => item.PolicyId == policy.Id)
            .ToListAsync(cancellationToken);
        foreach (var field in current)
            field.Retire();
        AddFields(policy, fields);

        var organizationIds = await context.ExternalGrants
            .AsNoTracking()
            .Where(item => item.FieldPolicyId == policy.Id)
            .Select(item => item.OrganizationId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var organizations = await context.ExternalOrganizations
            .Where(item => organizationIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        foreach (var organization in organizations)
            organization.TouchAuthorizationSecurityStamp();

        await SaveAsync(cancellationToken);
        return ToDto(policy, fields.Select(ToDto).ToArray());
    }

    private async Task<SpaceFieldPolicy> FindPolicyAsync(
        Guid policyId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        if (policyId == Guid.Empty)
            throw NotFound();
        IQueryable<SpaceFieldPolicy> query = context.FieldPolicies;
        if (!asTracking)
            query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(
                   item => item.Id == policyId,
                   cancellationToken)
               ?? throw NotFound();
    }

    private async Task<IReadOnlyDictionary<Guid, SpaceFieldPolicyFieldDto[]>>
        LoadFieldsAsync(
            Guid[] policyIds,
            CancellationToken cancellationToken)
    {
        if (policyIds.Length == 0)
        {
            return new Dictionary<Guid, SpaceFieldPolicyFieldDto[]>();
        }
        return (await context.FieldPolicyFields
                .AsNoTracking()
                .Where(item => policyIds.Contains(item.PolicyId))
                .OrderBy(item => item.ResourceType)
                .ThenBy(item => item.NormalizedFieldName)
                .ToListAsync(cancellationToken))
            .GroupBy(item => item.PolicyId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(ToDto).ToArray());
    }

    private static NormalizedField[] NormalizeFields(
        IReadOnlyList<SpaceFieldPolicyFieldRequest>? requests)
    {
        var source = requests?.ToArray() ?? [];
        if (source.Length == 0 || source.Length > MaximumFields)
        {
            throw Invalid(
                $"A field policy must contain 1 to {MaximumFields} fields.");
        }

        var fields = source.Select(request =>
        {
            if (request is null)
                throw Invalid("Field policy entries cannot be null.");
            var resourceType = ParseEnum<SpaceResourceType>(
                request.ResourceType,
                "resource type");
            var definition = SpacePortalFieldCatalog.Find(
                resourceType,
                request.FieldName)
                ?? throw Invalid(
                    $"Field '{request.FieldName}' is not in the external Portal catalog.");
            var maskingRule = ParseEnum<SpaceFieldMaskingRule>(
                request.MaskingRule,
                "masking rule");
            if (definition.Kind == SpacePortalFieldKind.Scalar &&
                maskingRule is SpaceFieldMaskingRule.Partial or
                    SpaceFieldMaskingRule.Hash)
            {
                throw Invalid(
                    $"Field '{definition.FieldName}' only supports None or Redact.");
            }
            return new NormalizedField(
                ToPolicyResourceType(resourceType),
                definition.FieldName,
                maskingRule);
        }).ToArray();

        if (fields
                .Select(item => $"{(int)item.ResourceType}:{item.FieldName.ToUpperInvariant()}")
                .Distinct(StringComparer.Ordinal)
                .Count() != fields.Length)
        {
            throw Invalid("Field policy entries must be unique by resource and field.");
        }
        return fields
            .OrderBy(item => item.ResourceType)
            .ThenBy(item => item.FieldName, StringComparer.Ordinal)
            .ToArray();
    }

    private void AddFields(
        SpaceFieldPolicy policy,
        IEnumerable<NormalizedField> fields) =>
        context.FieldPolicyFields.AddRange(fields.Select(item =>
            SpaceFieldPolicyField.Create(
                policy.TenantId,
                policy.Id,
                item.ResourceType,
                item.FieldName,
                item.MaskingRule)));

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.GetBaseException() is SqlException
                  {
                      Number: 2601 or 2627,
                  })
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.FieldPolicyConflict,
                409,
                "The field policy conflicts with current data.",
                recoveryAction: "reload-field-policies");
        }
    }

    private Guid RequireTenant()
    {
        if (execution.TenantId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        }
        return execution.TenantId;
    }

    private static T ParseEnum<T>(string? value, string label)
        where T : struct, Enum
    {
        var input = value?.Trim();
        if (string.IsNullOrEmpty(input) ||
            long.TryParse(input, out _) ||
            !Enum.TryParse<T>(input, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw Invalid($"Unsupported {label}.");
        }
        return parsed;
    }

    private static SpaceFieldPolicyResourceType ToPolicyResourceType(
        SpaceResourceType resourceType) =>
        resourceType switch
        {
            SpaceResourceType.PublishedScene =>
                SpaceFieldPolicyResourceType.PublishedScene,
            SpaceResourceType.Stock => SpaceFieldPolicyResourceType.Stock,
            SpaceResourceType.Task => SpaceFieldPolicyResourceType.Task,
            _ => throw Invalid("Unsupported resource type."),
        };

    private static SpaceFieldPolicyDto ToDto(
        SpaceFieldPolicy policy,
        IReadOnlyDictionary<Guid, SpaceFieldPolicyFieldDto[]> fields) =>
        ToDto(
            policy,
            fields.GetValueOrDefault(policy.Id) ?? []);

    private static SpaceFieldPolicyDto ToDto(
        SpaceFieldPolicy policy,
        IReadOnlyList<SpaceFieldPolicyFieldDto> fields) =>
        new(
            policy.Id,
            policy.Name,
            policy.AudienceType.ToString(),
            policy.CanExport,
            policy.Status.ToString(),
            policy.PolicyVersion,
            fields,
            policy.CreatedAtUtc,
            policy.CreatedBy,
            policy.ModifiedAtUtc,
            policy.ModifiedBy);

    private static SpaceFieldPolicyFieldDto ToDto(
        SpaceFieldPolicyField field) =>
        new(
            field.ResourceType.ToString(),
            field.FieldName,
            field.MaskingRule.ToString());

    private static SpaceFieldPolicyFieldDto ToDto(NormalizedField field) =>
        new(
            field.ResourceType.ToString(),
            field.FieldName,
            field.MaskingRule.ToString());

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.FieldPolicyNotFound,
            404,
            "The field policy was not found.",
            recoveryAction: "select-current-field-policy");

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.FieldPolicyInvalid,
            422,
            "The field policy is invalid.",
            detail,
            "correct-field-policy");

    private sealed record NormalizedField(
        SpaceFieldPolicyResourceType ResourceType,
        string FieldName,
        SpaceFieldMaskingRule MaskingRule);
}
