using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadMapping
{
    private const int MaximumProfileNameLength = 200;
    private const int MaximumDimensionMillimeters = 1_000_000;

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static SpaceCadMappingProfileV1 Seal(SpaceCadMappingProfileDraftV1 draft)
    {
        ValidateDraft(draft);
        var rules = draft.Rules
            .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
            .ToArray();
        var withoutHash = new SpaceCadMappingProfileV1(
            draft.SchemaVersion,
            draft.ProfileId,
            draft.Version,
            draft.Name,
            draft.Scope,
            draft.TenantId,
            draft.IsEnabled,
            draft.BasedOnProfileId,
            draft.BasedOnVersion,
            rules,
            DefinitionSha256: string.Empty);
        return withoutHash with
        {
            DefinitionSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
    }

    public static SpaceCadMappingProfileV1 CreateTenantCopy(
        SpaceCadMappingProfileV1 systemProfile,
        Guid tenantId,
        Guid newProfileId,
        string name)
    {
        Validate(systemProfile);
        RequireId(tenantId, nameof(tenantId));
        RequireId(newProfileId, nameof(newProfileId));
        if (systemProfile.Scope != SpaceCadMappingScope.System)
            throw new InvalidOperationException("Only a system CAD mapping profile can be copied.");
        return Seal(new SpaceCadMappingProfileDraftV1(
            SpaceCadMappingVersions.SchemaVersion,
            newProfileId,
            Version: 1,
            name,
            SpaceCadMappingScope.Tenant,
            tenantId,
            systemProfile.IsEnabled,
            systemProfile.ProfileId,
            systemProfile.Version,
            systemProfile.Rules));
    }

    public static SpaceCadMappingProfileV1 CreateNextTenantVersion(
        SpaceCadMappingProfileV1 current,
        Guid tenantId,
        IReadOnlyList<SpaceCadMappingRuleV1> rules,
        string? name = null,
        bool? isEnabled = null)
    {
        ValidateForTenant(current, tenantId);
        if (current.Scope != SpaceCadMappingScope.Tenant)
        {
            throw new InvalidOperationException(
                "System CAD mapping profiles are read-only; copy one before editing.");
        }
        return Seal(new SpaceCadMappingProfileDraftV1(
            SpaceCadMappingVersions.SchemaVersion,
            current.ProfileId,
            checked(current.Version + 1),
            name ?? current.Name,
            SpaceCadMappingScope.Tenant,
            tenantId,
            isEnabled ?? current.IsEnabled,
            current.BasedOnProfileId,
            current.BasedOnVersion,
            rules));
    }

    public static void Validate(SpaceCadMappingProfileV1 profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var draft = new SpaceCadMappingProfileDraftV1(
            profile.SchemaVersion,
            profile.ProfileId,
            profile.Version,
            profile.Name,
            profile.Scope,
            profile.TenantId,
            profile.IsEnabled,
            profile.BasedOnProfileId,
            profile.BasedOnVersion,
            profile.Rules);
        ValidateDraft(draft);
        if (!IsSha256(profile.DefinitionSha256)
            || !profile.Rules.SequenceEqual(
                profile.Rules.OrderBy(rule => rule.RuleId, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("CAD mapping profile is not canonically sealed.");
        }
        var expected = Seal(draft).DefinitionSha256;
        if (!profile.DefinitionSha256.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("CAD mapping profile hash does not match its definition.");
    }

    public static string SerializeProfile(SpaceCadMappingProfileV1 profile)
    {
        Validate(profile);
        return JsonSerializer.Serialize(profile, CanonicalJsonOptions);
    }

    public static SpaceCadMappingPreviewV1 Preview(
        Guid tenantId,
        SpaceCadInventoryV1 inventory,
        SpaceCadMappingProfileV1 profile,
        IReadOnlyList<SpaceCadLayerMappingOverrideV1>? layerOverrides = null)
    {
        RequireId(tenantId, nameof(tenantId));
        SpaceCadInventory.Validate(inventory);
        ValidateForTenant(profile, tenantId);
        if (!profile.IsEnabled)
            throw new InvalidOperationException("The selected CAD mapping profile is disabled.");

        var overrides = NormalizeOverrides(layerOverrides ?? [], inventory);
        var overrideByLayer = overrides.ToDictionary(
            item => item.LayerId,
            StringComparer.OrdinalIgnoreCase);
        var issues = new List<SpaceCadMappingIssueV1>();
        var decisions = new List<SpaceCadMappingDecisionV1>();
        var matchedRuleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var layer in inventory.Layers)
        {
            if (overrideByLayer.TryGetValue(layer.LayerId, out var layerOverride))
            {
                decisions.Add(OverrideDecision(layer, layerOverride));
                continue;
            }
            decisions.Add(Resolve(
                SpaceCadMappingSourceKind.Layer,
                layer.LayerId,
                layer.LayerId,
                layer.Name,
                layer.EntityCount,
                attributes: null,
                profile.Rules,
                matchedRuleIds,
                issues));
        }

        foreach (var block in inventory.Blocks)
        {
            var attributes = inventory.BlockReferences
                .Where(reference => reference.BlockName.Equals(
                    block.Name,
                    StringComparison.Ordinal))
                .SelectMany(reference => reference.Attributes)
                .ToArray();
            decisions.Add(Resolve(
                SpaceCadMappingSourceKind.Block,
                block.Name,
                layerId: null,
                block.Name,
                block.ReferenceCount,
                attributes,
                profile.Rules,
                matchedRuleIds,
                issues));
        }

        foreach (var required in profile.Rules.Where(rule => rule.IsRequired))
        {
            if (matchedRuleIds.Contains(required.RuleId))
                continue;
            issues.Add(new SpaceCadMappingIssueV1(
                "SPACE_CAD_MAPPING_REQUIRED_SOURCE_MISSING",
                SpaceCadIssueSeverity.Blocking,
                required.SourceKind,
                RuleId: required.RuleId));
        }

        decisions = decisions
            .OrderBy(decision => decision.SourceKind)
            .ThenBy(decision => decision.SourceKey, StringComparer.Ordinal)
            .ToList();
        issues = issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.SourceKind)
            .ThenBy(issue => issue.SourceKey, StringComparer.Ordinal)
            .ThenBy(issue => issue.RuleId, StringComparer.Ordinal)
            .ToList();
        var sourceStructureHash = SourceStructureSha256(inventory);
        var reuseKey = ComputeSha256(string.Join(
            '|',
            tenantId.ToString("D"),
            profile.ProfileId.ToString("D"),
            profile.Version,
            profile.DefinitionSha256,
            inventory.SourceSha256,
            sourceStructureHash,
            CanonicalJson(overrides)));
        var summary = Summary(decisions, issues);
        var withoutHash = new SpaceCadMappingPreviewV1(
            SpaceCadMappingVersions.SchemaVersion,
            tenantId,
            profile.ProfileId,
            profile.Version,
            profile.DefinitionSha256,
            inventory.SourceSha256,
            inventory.InventorySha256,
            sourceStructureHash,
            reuseKey,
            overrides,
            decisions,
            issues,
            summary,
            ReadyForSemanticParsing: summary.BlockingCount == 0,
            PreviewSha256: string.Empty);
        var preview = withoutHash with
        {
            PreviewSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        ValidatePreview(preview);
        return preview;
    }

    public static void ValidatePreview(SpaceCadMappingPreviewV1 preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(preview.LayerOverrides);
        ArgumentNullException.ThrowIfNull(preview.Decisions);
        ArgumentNullException.ThrowIfNull(preview.Issues);
        ArgumentNullException.ThrowIfNull(preview.Summary);
        if (preview.SchemaVersion != SpaceCadMappingVersions.SchemaVersion
            || preview.TenantId == Guid.Empty
            || preview.ProfileId == Guid.Empty
            || preview.ProfileVersion <= 0
            || !IsSha256(preview.ProfileDefinitionSha256)
            || !IsSha256(preview.SourceSha256)
            || !IsSha256(preview.InventorySha256)
            || !IsSha256(preview.SourceStructureSha256)
            || !IsSha256(preview.ReuseKeySha256)
            || !IsSha256(preview.PreviewSha256))
        {
            throw new InvalidDataException("CAD mapping preview identity is incomplete.");
        }
        if (!preview.LayerOverrides.SequenceEqual(
                preview.LayerOverrides.OrderBy(item => item.LayerId, StringComparer.Ordinal))
            || !preview.Decisions.SequenceEqual(
                preview.Decisions
                    .OrderBy(decision => decision.SourceKind)
                    .ThenBy(decision => decision.SourceKey, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("CAD mapping preview records are not canonical.");
        }
        var overrideLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in preview.LayerOverrides)
        {
            ValidateLayerOverride(item);
            if (!overrideLayers.Add(item.LayerId))
                throw new InvalidDataException("CAD mapping preview overrides are duplicated.");
        }
        var decisionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in preview.Decisions)
        {
            if (!Enum.IsDefined(decision.SourceKind)
                || !Enum.IsDefined(decision.Status)
                || !Enum.IsDefined(decision.DecisionSource))
            {
                throw new InvalidDataException("CAD mapping decision enum is invalid.");
            }
            RequireToken(decision.SourceKey, nameof(decision.SourceKey));
            if (!decisionKeys.Add($"{decision.SourceKind}:{decision.SourceKey}")
                || decision.ObjectCount < 0
                || (decision.SourceKind == SpaceCadMappingSourceKind.Layer
                    && !decision.SourceKey.Equals(decision.LayerId, StringComparison.Ordinal))
                || (decision.SourceKind == SpaceCadMappingSourceKind.Block
                    && decision.LayerId is not null))
            {
                throw new InvalidDataException("CAD mapping decision identity is invalid.");
            }
            if (decision.Status == SpaceCadMappingDecisionStatus.Mapped)
            {
                ValidateTarget(
                    decision.SourceKind,
                    ignore: false,
                    decision.Target,
                    decision.TargetSubtype,
                    decision.GeometryRule,
                    decision.DefaultHeightMillimeters,
                    decision.DefaultThicknessMillimeters,
                    decision.ConfidenceWeight);
                if (decision.DecisionSource == SpaceCadMappingDecisionSource.ProfileRule)
                    RequireToken(decision.RuleId!, nameof(decision.RuleId));
                else if (decision.DecisionSource != SpaceCadMappingDecisionSource.LayerOverride
                         || decision.RuleId is not null)
                    throw new InvalidDataException("CAD mapped decision source is invalid.");
            }
            else if (decision.Target is not null
                     || decision.TargetSubtype is not null
                     || decision.GeometryRule is not null
                     || decision.DefaultHeightMillimeters is not null
                     || decision.DefaultThicknessMillimeters is not null
                     || decision.ConfidenceWeight is not null
                     || decision.RuleId is not null)
            {
                throw new InvalidDataException("CAD non-mapped decision carries mapping output.");
            }
        }
        foreach (var issue in preview.Issues)
        {
            RequireToken(issue.Code, nameof(issue.Code));
            if (!Enum.IsDefined(issue.Severity)
                || (issue.SourceKind is { } sourceKind && !Enum.IsDefined(sourceKind)))
            {
                throw new InvalidDataException("CAD mapping issue enum is invalid.");
            }
            if (issue.SourceKey is not null)
                RequireToken(issue.SourceKey, nameof(issue.SourceKey));
            if (issue.RuleId is not null)
                RequireToken(issue.RuleId, nameof(issue.RuleId));
            if (issue.DetailToken is { Length: > SpaceCadConversionContract.MaximumIdentifierLength })
                throw new InvalidDataException("CAD mapping issue detail token is too long.");
        }
        var summary = Summary(preview.Decisions, preview.Issues);
        if (summary != preview.Summary
            || preview.ReadyForSemanticParsing != (summary.BlockingCount == 0))
        {
            throw new InvalidDataException("CAD mapping preview summary is inconsistent.");
        }
        var expected = ComputeSha256(CanonicalJson(preview with { PreviewSha256 = string.Empty }));
        if (!preview.PreviewSha256.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("CAD mapping preview hash does not match its content.");
    }

    public static string SerializePreview(SpaceCadMappingPreviewV1 preview)
    {
        ValidatePreview(preview);
        return JsonSerializer.Serialize(preview, CanonicalJsonOptions);
    }

    private static SpaceCadMappingDecisionV1 Resolve(
        SpaceCadMappingSourceKind sourceKind,
        string sourceKey,
        string? layerId,
        string name,
        long objectCount,
        IReadOnlyList<KeyValuePair<string, string>>? attributes,
        IReadOnlyList<SpaceCadMappingRuleV1> rules,
        ISet<string> matchedRuleIds,
        ICollection<SpaceCadMappingIssueV1> issues)
    {
        var matches = rules
            .Where(rule => rule.SourceKind == sourceKind
                           && Matches(rule.MatchKind, rule.Pattern, name)
                           && MatchesAttributes(rule, attributes))
            .ToArray();
        if (objectCount > 0)
        {
            foreach (var match in matches)
                matchedRuleIds.Add(match.RuleId);
        }
        if (matches.Length == 0)
        {
            issues.Add(new SpaceCadMappingIssueV1(
                "SPACE_CAD_MAPPING_SOURCE_UNMAPPED",
                objectCount == 0 ? SpaceCadIssueSeverity.Info : SpaceCadIssueSeverity.Warning,
                sourceKind,
                sourceKey));
            return new SpaceCadMappingDecisionV1(
                sourceKind,
                sourceKey,
                layerId,
                objectCount,
                SpaceCadMappingDecisionStatus.Unmapped,
                SpaceCadMappingDecisionSource.None,
                RuleId: null,
                Target: null,
                TargetSubtype: null,
                GeometryRule: null,
                DefaultHeightMillimeters: null,
                DefaultThicknessMillimeters: null,
                ConfidenceWeight: null);
        }

        var highestPriority = matches.Max(rule => rule.Priority);
        var byPriority = matches.Where(rule => rule.Priority == highestPriority).ToArray();
        var highestSpecificity = byPriority.Max(rule => Specificity(rule.MatchKind));
        var winners = byPriority
            .Where(rule => Specificity(rule.MatchKind) == highestSpecificity)
            .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
            .ToArray();
        if (winners.Length > 1)
        {
            issues.Add(new SpaceCadMappingIssueV1(
                "SPACE_CAD_MAPPING_RULE_CONFLICT",
                SpaceCadIssueSeverity.Blocking,
                sourceKind,
                sourceKey,
                DetailToken: ConflictToken(winners.Select(rule => rule.RuleId))));
            return new SpaceCadMappingDecisionV1(
                sourceKind,
                sourceKey,
                layerId,
                objectCount,
                SpaceCadMappingDecisionStatus.Conflict,
                SpaceCadMappingDecisionSource.ProfileRule,
                RuleId: null,
                Target: null,
                TargetSubtype: null,
                GeometryRule: null,
                DefaultHeightMillimeters: null,
                DefaultThicknessMillimeters: null,
                ConfidenceWeight: null);
        }

        var winner = winners[0];
        return new SpaceCadMappingDecisionV1(
            sourceKind,
            sourceKey,
            layerId,
            objectCount,
            SpaceCadMappingDecisionStatus.Mapped,
            SpaceCadMappingDecisionSource.ProfileRule,
            winner.RuleId,
            winner.Target,
            winner.TargetSubtype,
            winner.GeometryRule,
            winner.DefaultHeightMillimeters,
            winner.DefaultThicknessMillimeters,
            winner.ConfidenceWeight);
    }

    private static SpaceCadMappingDecisionV1 OverrideDecision(
        SpaceCadLayerInventoryV1 layer,
        SpaceCadLayerMappingOverrideV1 layerOverride)
    {
        if (layerOverride.Ignore)
        {
            return new SpaceCadMappingDecisionV1(
                SpaceCadMappingSourceKind.Layer,
                layer.LayerId,
                layer.LayerId,
                layer.EntityCount,
                SpaceCadMappingDecisionStatus.Ignored,
                SpaceCadMappingDecisionSource.LayerOverride,
                RuleId: null,
                Target: null,
                TargetSubtype: null,
                GeometryRule: null,
                DefaultHeightMillimeters: null,
                DefaultThicknessMillimeters: null,
                ConfidenceWeight: null);
        }
        return new SpaceCadMappingDecisionV1(
            SpaceCadMappingSourceKind.Layer,
            layer.LayerId,
            layer.LayerId,
            layer.EntityCount,
            SpaceCadMappingDecisionStatus.Mapped,
            SpaceCadMappingDecisionSource.LayerOverride,
            RuleId: null,
            layerOverride.Target,
            layerOverride.TargetSubtype,
            layerOverride.GeometryRule,
            layerOverride.DefaultHeightMillimeters,
            layerOverride.DefaultThicknessMillimeters,
            layerOverride.ConfidenceWeight);
    }

    private static IReadOnlyList<SpaceCadLayerMappingOverrideV1> NormalizeOverrides(
        IReadOnlyList<SpaceCadLayerMappingOverrideV1> overrides,
        SpaceCadInventoryV1 inventory)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        if (overrides.Count > SpaceCadMappingVersions.MaximumOverrides)
            throw new ArgumentOutOfRangeException(nameof(overrides));
        var layerIds = inventory.Layers
            .Select(layer => layer.LayerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<SpaceCadLayerMappingOverrideV1>(overrides.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides)
        {
            ValidateLayerOverride(item);
            if (!seen.Add(item.LayerId))
                throw new InvalidDataException("CAD layer overrides must be unique.");
            if (!layerIds.Contains(item.LayerId))
                throw new InvalidDataException($"CAD layer override '{item.LayerId}' is unknown.");
            result.Add(item with
            {
                LayerId = inventory.Layers.Single(layer => layer.LayerId.Equals(
                    item.LayerId,
                    StringComparison.OrdinalIgnoreCase)).LayerId,
            });
        }
        return result.OrderBy(item => item.LayerId, StringComparer.Ordinal).ToArray();
    }

    internal static void ValidateLayerOverride(
        SpaceCadLayerMappingOverrideV1 item)
    {
        ArgumentNullException.ThrowIfNull(item);
        RequireToken(item.LayerId, nameof(item.LayerId));
        ValidateTarget(
            SpaceCadMappingSourceKind.Layer,
            item.Ignore,
            item.Target,
            item.TargetSubtype,
            item.GeometryRule,
            item.DefaultHeightMillimeters,
            item.DefaultThicknessMillimeters,
            item.ConfidenceWeight);
    }

    private static void ValidateDraft(SpaceCadMappingProfileDraftV1 draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Rules);
        if (draft.SchemaVersion != SpaceCadMappingVersions.SchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(draft.SchemaVersion));
        if (!Enum.IsDefined(draft.Scope))
            throw new ArgumentOutOfRangeException(nameof(draft.Scope));
        RequireId(draft.ProfileId, nameof(draft.ProfileId));
        if (draft.Version <= 0)
            throw new ArgumentOutOfRangeException(nameof(draft.Version));
        if (string.IsNullOrWhiteSpace(draft.Name)
            || draft.Name.Length > MaximumProfileNameLength
            || !draft.Name.Equals(draft.Name.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("CAD mapping profile name is invalid.", nameof(draft.Name));
        }
        if (draft.Scope == SpaceCadMappingScope.System)
        {
            if (draft.TenantId is not null
                || draft.BasedOnProfileId is not null
                || draft.BasedOnVersion is not null)
            {
                throw new InvalidDataException(
                    "System CAD mapping profiles cannot carry tenant or base identities.");
            }
        }
        else if (draft.TenantId is null || draft.TenantId == Guid.Empty)
        {
            throw new InvalidDataException("Tenant CAD mapping profiles require a tenant ID.");
        }
        if (draft.BasedOnProfileId.HasValue != draft.BasedOnVersion.HasValue
            || draft.BasedOnProfileId == Guid.Empty
            || draft.BasedOnVersion <= 0)
        {
            throw new InvalidDataException("CAD mapping profile base identity is incomplete.");
        }
        if (draft.Rules.Count is 0 or > SpaceCadMappingVersions.MaximumRules)
            throw new ArgumentOutOfRangeException(nameof(draft.Rules));
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in draft.Rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            RequireToken(rule.RuleId, nameof(rule.RuleId));
            if (!ruleIds.Add(rule.RuleId))
                throw new InvalidDataException("CAD mapping rule IDs must be unique.");
            if (rule.Priority is < 0 or > 10_000)
                throw new ArgumentOutOfRangeException(nameof(rule.Priority));
            if (!Enum.IsDefined(rule.SourceKind)
                || !Enum.IsDefined(rule.MatchKind)
                || !Enum.IsDefined(rule.Target)
                || !Enum.IsDefined(rule.GeometryRule))
            {
                throw new ArgumentOutOfRangeException(nameof(draft.Rules));
            }
            ValidatePattern(rule.MatchKind, rule.Pattern, nameof(rule.Pattern));
            var hasAnyAttribute = rule.AttributeName is not null
                                  || rule.AttributeMatchKind is not null
                                  || rule.AttributePattern is not null;
            if (hasAnyAttribute
                && (rule.SourceKind != SpaceCadMappingSourceKind.Block
                    || string.IsNullOrWhiteSpace(rule.AttributeName)
                    || rule.AttributeMatchKind is null
                    || string.IsNullOrWhiteSpace(rule.AttributePattern)))
            {
                throw new InvalidDataException(
                    "CAD attribute conditions require a block rule with name, match kind and pattern.");
            }
            if (hasAnyAttribute)
            {
                RequireToken(rule.AttributeName!, nameof(rule.AttributeName));
                if (!Enum.IsDefined(rule.AttributeMatchKind!.Value))
                    throw new ArgumentOutOfRangeException(nameof(rule.AttributeMatchKind));
                ValidatePattern(
                    rule.AttributeMatchKind!.Value,
                    rule.AttributePattern!,
                    nameof(rule.AttributePattern),
                    SpaceCadConversionContract.MaximumAttributeValueLength);
            }
            ValidateTarget(
                rule.SourceKind,
                ignore: false,
                rule.Target,
                rule.TargetSubtype,
                rule.GeometryRule,
                rule.DefaultHeightMillimeters,
                rule.DefaultThicknessMillimeters,
                rule.ConfidenceWeight);
        }
    }

    private static void ValidateTarget(
        SpaceCadMappingSourceKind sourceKind,
        bool ignore,
        SpaceCadSemanticTarget? target,
        string? subtype,
        SpaceCadGeometryRule? geometryRule,
        decimal? height,
        decimal? thickness,
        decimal? confidence)
    {
        if (ignore)
        {
            if (target is not null || subtype is not null || geometryRule is not null
                || height is not null || thickness is not null || confidence is not null)
            {
                throw new InvalidDataException("Ignored CAD layer overrides cannot map a target.");
            }
            return;
        }
        if (target is null || geometryRule is null || confidence is null)
            throw new InvalidDataException("CAD mapping target, geometry and confidence are required.");
        if (!Enum.IsDefined(target.Value) || !Enum.IsDefined(geometryRule.Value))
            throw new InvalidDataException("CAD mapping target or geometry enum is invalid.");
        if (subtype is not null)
            RequireToken(subtype, nameof(subtype));
        if (height is <= 0 or > MaximumDimensionMillimeters
            || thickness is <= 0 or > MaximumDimensionMillimeters
            || confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException("CAD mapping dimensions or confidence are invalid.");
        }
        if (sourceKind == SpaceCadMappingSourceKind.Block
            && geometryRule is not (SpaceCadGeometryRule.BlockFootprint
                or SpaceCadGeometryRule.InsertionPoint))
        {
            throw new InvalidDataException(
                "CAD block rules require BlockFootprint or InsertionPoint geometry.");
        }
        if (sourceKind == SpaceCadMappingSourceKind.Layer
            && geometryRule is SpaceCadGeometryRule.BlockFootprint
                or SpaceCadGeometryRule.InsertionPoint)
        {
            throw new InvalidDataException(
                "CAD layer rules cannot use block-only geometry.");
        }
    }

    private static void ValidateForTenant(SpaceCadMappingProfileV1 profile, Guid tenantId)
    {
        RequireId(tenantId, nameof(tenantId));
        Validate(profile);
        if (profile.Scope == SpaceCadMappingScope.Tenant && profile.TenantId != tenantId)
            throw new UnauthorizedAccessException("CAD mapping profile belongs to another tenant.");
    }

    private static bool MatchesAttributes(
        SpaceCadMappingRuleV1 rule,
        IReadOnlyList<KeyValuePair<string, string>>? attributes)
    {
        if (rule.AttributeName is null)
            return true;
        return attributes is not null && attributes.Any(attribute =>
            attribute.Key.Equals(rule.AttributeName, StringComparison.OrdinalIgnoreCase)
            && Matches(rule.AttributeMatchKind!.Value, rule.AttributePattern!, attribute.Value));
    }

    private static bool Matches(
        SpaceCadMappingMatchKind matchKind,
        string pattern,
        string value) => matchKind switch
    {
        SpaceCadMappingMatchKind.Exact => value.Equals(pattern, StringComparison.OrdinalIgnoreCase),
        SpaceCadMappingMatchKind.Glob => Regex.IsMatch(
            value,
            GlobPattern(pattern),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100)),
        SpaceCadMappingMatchKind.Regex => Regex.IsMatch(
            value,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100)),
        _ => false,
    };

    private static void ValidatePattern(
        SpaceCadMappingMatchKind matchKind,
        string pattern,
        string name,
        int maximumLength = SpaceCadConversionContract.MaximumIdentifierLength)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > maximumLength)
            throw new ArgumentException("CAD mapping pattern is invalid.", name);
        if (matchKind == SpaceCadMappingMatchKind.Exact)
            return;
        try
        {
            _ = new Regex(
                matchKind == SpaceCadMappingMatchKind.Glob ? GlobPattern(pattern) : pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(100));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException("CAD mapping pattern is invalid or unsafe.", name, exception);
        }
    }

    private static string GlobPattern(string pattern) =>
        $"^{Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal)}$";

    private static int Specificity(SpaceCadMappingMatchKind matchKind) => matchKind switch
    {
        SpaceCadMappingMatchKind.Exact => 3,
        SpaceCadMappingMatchKind.Glob => 2,
        SpaceCadMappingMatchKind.Regex => 1,
        _ => 0,
    };

    private static string ConflictToken(IEnumerable<string> ruleIds)
    {
        var value = string.Join(',', ruleIds);
        return value.Length <= SpaceCadConversionContract.MaximumIdentifierLength
            ? value
            : $"sha256:{ComputeSha256(value)}";
    }

    private static string SourceStructureSha256(SpaceCadInventoryV1 inventory)
    {
        var structure = new
        {
            layers = inventory.Layers.Select(layer => new
            {
                layer.LayerId,
                layer.Name,
                layer.Color,
                layer.LineType,
                layer.IsVisible,
                types = layer.EntityTypeCounts.Keys.OrderBy(value => value, StringComparer.Ordinal),
            }),
            blocks = inventory.Blocks.Select(block => new
            {
                block.Name,
                block.IsDefined,
                block.IsExternalReference,
                attributes = block.Attributes.Select(attribute => attribute.Name),
            }),
        };
        return ComputeSha256(CanonicalJson(structure));
    }

    private static SpaceCadMappingPreviewSummaryV1 Summary(
        IReadOnlyList<SpaceCadMappingDecisionV1> decisions,
        IReadOnlyList<SpaceCadMappingIssueV1> issues)
    {
        var layers = decisions.Where(
            decision => decision.SourceKind == SpaceCadMappingSourceKind.Layer).ToArray();
        var blocks = decisions.Where(
            decision => decision.SourceKind == SpaceCadMappingSourceKind.Block).ToArray();
        return new SpaceCadMappingPreviewSummaryV1(
            layers.LongLength,
            layers.LongCount(decision => decision.Status == SpaceCadMappingDecisionStatus.Mapped),
            layers.LongCount(decision => decision.Status == SpaceCadMappingDecisionStatus.Unmapped),
            layers.LongCount(decision => decision.Status == SpaceCadMappingDecisionStatus.Ignored),
            layers.LongCount(decision => decision.Status == SpaceCadMappingDecisionStatus.Conflict),
            blocks.LongLength,
            blocks.LongCount(decision => decision.Status == SpaceCadMappingDecisionStatus.Mapped),
            blocks.LongCount(decision => decision.Status == SpaceCadMappingDecisionStatus.Unmapped),
            blocks.LongCount(decision => decision.Status == SpaceCadMappingDecisionStatus.Conflict),
            layers.Where(decision => decision.Status == SpaceCadMappingDecisionStatus.Mapped)
                .Sum(decision => decision.ObjectCount),
            blocks.Where(decision => decision.Status == SpaceCadMappingDecisionStatus.Mapped)
                .Sum(decision => decision.ObjectCount),
            issues.LongCount(issue => issue.Severity == SpaceCadIssueSeverity.Info),
            issues.LongCount(issue => issue.Severity == SpaceCadIssueSeverity.Warning),
            issues.LongCount(issue => issue.Severity == SpaceCadIssueSeverity.Blocking));
    }

    private static void RequireId(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A non-empty ID is required.", name);
    }

    private static void RequireToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > SpaceCadConversionContract.MaximumIdentifierLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("CAD mapping token is invalid.", name);
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
