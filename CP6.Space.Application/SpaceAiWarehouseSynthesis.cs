using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed class WarehouseDraftSynthesizer : IWarehouseDraftSynthesizer
{
    private const long MaximumDerivedLocations = 10_000_000;

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly IReadOnlyDictionary<string, Type> EnumFieldTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["type"] = typeof(WarehouseSpaceType),
            ["attributes.zonePurpose"] = typeof(WarehouseZonePurpose),
            ["attributes.rackType"] = typeof(WarehouseRackType),
            ["attributes.doorType"] = typeof(WarehouseDoorType),
            ["attributes.dockType"] = typeof(WarehouseDockType),
            ["attributes.equipmentType"] = typeof(WarehouseEquipmentType),
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>>
        TextEnumFieldValues =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["attributes.direction"] = Values(
                    "OneWay", "TwoWay", "Bidirectional", "Unknown"),
                ["attributes.wallType"] = Values(
                    "Exterior", "Interior", "Partition", "Fire", "Unknown"),
                ["attributes.columnType"] = Values(
                    "Structural", "Guard", "Unknown"),
            };

    private readonly IWarehouseGenerationOutputValidator _outputValidator;

    public WarehouseDraftSynthesizer()
        : this(new WarehouseGenerationOutputValidator())
    {
    }

    public WarehouseDraftSynthesizer(
        IWarehouseGenerationOutputValidator outputValidator)
    {
        _outputValidator = outputValidator
            ?? throw new ArgumentNullException(nameof(outputValidator));
    }

    public Task<WarehouseDraftProposalSetV1> SynthesizeAsync(
        WarehouseDraftSynthesisRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Synthesize(request, cancellationToken));
    }

    public static string Serialize(WarehouseDraftProposalSetV1 proposalSet)
    {
        ValidateProposalSet(proposalSet);
        return JsonSerializer.Serialize(proposalSet, CanonicalJsonOptions);
    }

    private WarehouseDraftProposalSetV1 Synthesize(
        WarehouseDraftSynthesisRequestV1 request,
        CancellationToken cancellationToken)
    {
        var context = ValidateAndBind(request);
        var issues = RuleAndProviderIssues(request, context);
        var proposals = new List<MutableProposal>();

        foreach (var item in request.RulePreview.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Disposition == SpaceCadSemanticDisposition.Rejected
                || item.Geometry is null)
            {
                continue;
            }
            if (!TryMapType(item.Target, out var ruleType))
            {
                issues.Add(new WarehouseProposalIssueV1(
                    "RULE_TARGET_NOT_GENERATABLE",
                    WarehouseProposalIssueSeverity.Warning,
                    item.Source.SourceRef,
                    context.SourceKeyByRef[item.Source.SourceRef],
                    "type"));
                continue;
            }

            var sourceRef = item.Source.SourceRef;
            var sourceKey = context.SourceKeyByRef[sourceRef];
            context.SuggestionByKey.TryGetValue(sourceKey, out var suggestion);
            var candidates = CandidateFields(
                item,
                ruleType,
                suggestion,
                context.LockedByRef.GetValueOrDefault(sourceRef) ?? [],
                context.DefaultsByRef.GetValueOrDefault(sourceRef) ?? []);
            var resolvedType = ResolveField(
                sourceRef,
                sourceKey,
                "type",
                candidates["type"],
                item.Confidence,
                issues,
                out var softTypeConflict);
            var objectType = Enum.Parse<WarehouseSpaceType>(
                resolvedType.ValueToken,
                ignoreCase: false);
            var fields = new List<WarehouseResolvedFieldV1> { resolvedType };
            var hasSoftConflict = softTypeConflict;
            foreach (var (fieldPath, fieldCandidates) in candidates
                         .Where(pair => pair.Key != "type")
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var compatible = fieldCandidates
                    .Where(candidate => FieldApplies(fieldPath, objectType))
                    .ToArray();
                if (compatible.Length == 0)
                {
                    var rejected = fieldCandidates
                        .OrderByDescending(candidate => candidate.Source)
                        .First();
                    issues.Add(new WarehouseProposalIssueV1(
                        rejected.Source == WarehouseFusionSource.HumanLocked
                            ? "LOCKED_ATTRIBUTE_TYPE_CONFLICT"
                            : "AI_ATTRIBUTE_TYPE_CONFLICT",
                        rejected.Source == WarehouseFusionSource.HumanLocked
                            ? WarehouseProposalIssueSeverity.Blocking
                            : WarehouseProposalIssueSeverity.Warning,
                        sourceRef,
                        sourceKey,
                        fieldPath));
                    continue;
                }
                fields.Add(ResolveField(
                    sourceRef,
                    sourceKey,
                    fieldPath,
                    compatible,
                    item.Confidence,
                    issues,
                    out var softConflict));
                hasSoftConflict |= softConflict;
            }

            var confidence = decimal.Min(
                item.Confidence,
                fields.Min(field => field.Confidence));
            var band = ConfidenceBand(confidence, hasSoftConflict);
            var logicalId = WarehouseDeterministicIdentity.CreateObjectLogicalId(
                request.ModelVersionId,
                request.RulePreview.SourceSha256,
                sourceKey);
            proposals.Add(new MutableProposal(
                logicalId,
                sourceKey,
                sourceRef,
                objectType,
                item.Geometry,
                fields.OrderBy(field => field.FieldPath, StringComparer.Ordinal).ToArray(),
                confidence,
                band));
        }

        AddUnresolvedInputIssues(request, context, proposals, issues);
        AddRelations(context, proposals, issues);
        AddRackDerivations(request, context, proposals, issues);

        var canonicalIssues = CanonicalIssues(issues);
        var immutableProposals = proposals
            .OrderBy(proposal => GenerationOrder(proposal.ObjectType))
            .ThenBy(proposal => proposal.SourceRef, StringComparer.Ordinal)
            .Select(proposal => proposal.ToImmutable(canonicalIssues))
            .ToArray();
        EnsureUniqueLogicalIds(immutableProposals);
        var summary = Summary(immutableProposals, canonicalIssues);
        var withoutHash = new WarehouseDraftProposalSetV1(
            WarehouseDraftSynthesisVersions.SchemaVersion,
            IsReadOnlyPreview: true,
            DraftWritten: false,
            request.RulePreview.TenantId,
            request.ModelVersionId,
            request.RulePreview.FloorLogicalId,
            request.RulePreview.SourceSha256,
            request.RulePreview.CoordinateTransformSha256,
            request.RulePreview.SemanticPreviewSha256,
            request.FeaturePackage.LocalSourceMap.ProviderInputSha256,
            request.Ai.CanonicalSha256,
            request.FeaturePackage.LocalSourceMap.SourceMapSha256,
            request.RuleVersion,
            immutableProposals,
            canonicalIssues,
            summary,
            ProposalSetSha256: string.Empty);
        var proposalSet = withoutHash with
        {
            ProposalSetSha256 = ComputeSha256(CanonicalBytes(withoutHash)),
        };
        ValidateProposalSet(proposalSet);
        return proposalSet;
    }

    private BindingContext ValidateAndBind(
        WarehouseDraftSynthesisRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FeaturePackage);
        ArgumentNullException.ThrowIfNull(request.RulePreview);
        ArgumentNullException.ThrowIfNull(request.Ai);
        ArgumentNullException.ThrowIfNull(request.LockedFacts);
        ArgumentNullException.ThrowIfNull(request.TemplateDefaults);
        ArgumentNullException.ThrowIfNull(request.RackProfiles);
        if (request.ModelVersionId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.RuleVersion)
            || request.RuleVersion.Length > 64
            || !request.RuleVersion.Equals(request.RuleVersion.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Warehouse synthesis identity is incomplete.");
        }

        SpaceAiCadFeatureMinimizer.Validate(request.FeaturePackage);
        SpaceCadSemanticParser.Validate(request.RulePreview);
        var validated = _outputValidator.Validate(
            request.FeaturePackage.ProviderInput,
            request.Ai.Output);
        if (!validated.CanonicalSha256.Equals(
                request.Ai.CanonicalSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Validated AI result does not match its canonical evidence hash.");
        }
        var map = request.FeaturePackage.LocalSourceMap;
        if (!map.SourceSha256.Equals(request.RulePreview.SourceSha256, StringComparison.Ordinal)
            || !map.CoordinateTransformSha256.Equals(
                request.RulePreview.CoordinateTransformSha256,
                StringComparison.Ordinal)
            || map.FloorLogicalId != request.RulePreview.FloorLogicalId)
        {
            throw new InvalidDataException(
                "Rule, CAD source map and coordinate identities do not match.");
        }

        var sourceKeyByRef = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in map.Entries)
        {
            foreach (var sourceRef in entry.SourceRefs)
            {
                if (!sourceKeyByRef.TryAdd(sourceRef, entry.SourceKey))
                {
                    throw new InvalidDataException(
                        "CAD source reference has more than one provider key.");
                }
            }
        }
        if (request.RulePreview.Items.Any(item =>
                !sourceKeyByRef.ContainsKey(item.Source.SourceRef)))
        {
            throw new InvalidDataException(
                "Rule preview contains a source outside the local provider map.");
        }

        var lockedByRef = ValidateFacts(
            request.LockedFacts,
            sourceKeyByRef,
            allowSemanticLabel: false);
        var defaultsByRef = ValidateFacts(
            request.TemplateDefaults.Select(item =>
                    new SpaceAiCadLockedFactV1(
                        item.SourceRef,
                        item.FieldPath,
                        item.ValueToken))
                .ToArray(),
            sourceKeyByRef,
            allowSemanticLabel: true);
        ValidateProviderLockedFacts(
            request.FeaturePackage.ProviderInput,
            request.LockedFacts,
            sourceKeyByRef);
        var profilesByRef = ValidateRackProfiles(
            request.RackProfiles,
            sourceKeyByRef);
        return new BindingContext(
            sourceKeyByRef,
            request.Ai.Output.Suggestions.ToDictionary(
                item => item.SourceKey,
                StringComparer.Ordinal),
            lockedByRef,
            defaultsByRef,
            profilesByRef);
    }

    private static Dictionary<string, SpaceAiCadLockedFactV1[]> ValidateFacts(
        IReadOnlyList<SpaceAiCadLockedFactV1> facts,
        IReadOnlyDictionary<string, string> sourceKeyByRef,
        bool allowSemanticLabel)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SpaceAiCadLockedFactV1>();
        foreach (var fact in facts)
        {
            ArgumentNullException.ThrowIfNull(fact);
            if (!sourceKeyByRef.ContainsKey(fact.SourceRef)
                || !identities.Add($"{fact.SourceRef}\n{fact.FieldPath}")
                || !TryCanonicalField(
                    fact.FieldPath,
                    fact.ValueToken,
                    allowSemanticLabel,
                    out var value))
            {
                throw new InvalidDataException(
                    "Warehouse synthesis fact is invalid or not allowlisted.");
            }
            result.Add(fact with { ValueToken = value });
        }
        return result
            .GroupBy(item => item.SourceRef, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.FieldPath, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private static bool TryCanonicalField(
        string fieldPath,
        string value,
        bool allowSemanticLabel,
        out string canonical)
    {
        canonical = string.Empty;
        if (EnumFieldTypes.TryGetValue(fieldPath, out var type))
        {
            if (value is null
                || !Enum.TryParse(type, value, ignoreCase: false, out var parsed)
                || parsed is null
                || !Enum.IsDefined(type, parsed))
            {
                return false;
            }
            canonical = parsed.ToString()!;
            return true;
        }
        if (allowSemanticLabel
            && fieldPath == "attributes.semanticLabel"
            && IsSafeToken(value, 256))
        {
            canonical = value;
            return true;
        }
        if (TextEnumFieldValues.TryGetValue(fieldPath, out var values)
            && value is not null
            && values.Contains(value))
        {
            canonical = value;
            return true;
        }
        if (value is not null &&
            (fieldPath == "attributes.name" && IsSafeToken(value, 128) ||
             fieldPath is (
                 "relations.zoneSourceKey" or
                 "relations.aisleSourceKey" or
                 "relations.wallSourceKey") && IsSafeToken(value, 256)))
        {
            canonical = value;
            return true;
        }
        return false;
    }

    private static void ValidateProviderLockedFacts(
        WarehouseGenerationInput input,
        IReadOnlyList<SpaceAiCadLockedFactV1> localFacts,
        IReadOnlyDictionary<string, string> sourceKeyByRef)
    {
        var expected = localFacts
            .Select(fact => new WarehouseGenerationLockedFact(
                sourceKeyByRef[fact.SourceRef],
                fact.FieldPath,
                fact.ValueToken))
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .ThenBy(item => item.FieldPath, StringComparer.Ordinal)
            .ToArray();
        var actual = input.LockedFacts
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .ThenBy(item => item.FieldPath, StringComparer.Ordinal)
            .ToArray();
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidDataException(
                "Local locked facts do not match the provider input snapshot.");
        }
    }

    private static Dictionary<string, WarehouseRackProfileBindingV1[]>
        ValidateRackProfiles(
            IReadOnlyList<WarehouseRackProfileBindingV1> bindings,
            IReadOnlyDictionary<string, string> sourceKeyByRef)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(binding.Profile);
            if (!sourceKeyByRef.ContainsKey(binding.SourceRef)
                || !Enum.IsDefined(binding.Source)
                || !identities.Add($"{binding.SourceRef}\n{binding.Source}"))
            {
                throw new InvalidDataException("Rack profile binding is invalid.");
            }
            ValidateRackProfile(binding.Profile);
        }
        return bindings
            .GroupBy(item => item.SourceRef, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Source).ToArray(),
                StringComparer.Ordinal);
    }

    private static void ValidateRackProfile(WarehouseRackGenerationProfileV1 profile)
    {
        ArgumentNullException.ThrowIfNull(profile.Levels);
        if (profile.ProfileVersionId == Guid.Empty
            || profile.RackWidthMillimeters <= 0
            || profile.RackDepthMillimeters <= 0
            || profile.RackHeightMillimeters <= 0
            || profile.Levels.Count is < 1 or > 1_000)
        {
            throw new InvalidDataException("Rack generation profile is incomplete.");
        }
        var levelNumbers = new HashSet<int>();
        long total = 0;
        foreach (var level in profile.Levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            if (level.LevelNo <= 0
                || !levelNumbers.Add(level.LevelNo)
                || level.BottomZMillimeters < 0
                || level.ClearHeightMillimeters <= 0
                || level.BinCount <= 0
                || level.DepthCount <= 0
                || level.CellWidthMillimeters <= 0
                || level.CellDepthMillimeters <= 0
                || level.BeamHeightMillimeters < 0
                || level.MaxLoadKilograms < 0
                || (long)level.BottomZMillimeters
                    + level.ClearHeightMillimeters
                    + level.BeamHeightMillimeters > profile.RackHeightMillimeters
                || (long)level.BinCount * level.CellWidthMillimeters
                    > profile.RackWidthMillimeters
                || (long)level.DepthCount * level.CellDepthMillimeters
                    > profile.RackDepthMillimeters)
            {
                throw new InvalidDataException(
                    "Rack generation profile level is invalid.");
            }
            total = checked(total + ((long)level.BinCount * level.DepthCount));
        }
        if (total > MaximumDerivedLocations)
        {
            throw new InvalidDataException(
                "Rack generation profile exceeds the derived location limit.");
        }
    }

    private static List<WarehouseProposalIssueV1> RuleAndProviderIssues(
        WarehouseDraftSynthesisRequestV1 request,
        BindingContext context)
    {
        var issues = request.RulePreview.Issues
            .Select(issue => new WarehouseProposalIssueV1(
                issue.Code,
                issue.Severity switch
                {
                    SpaceCadIssueSeverity.Info => WarehouseProposalIssueSeverity.Info,
                    SpaceCadIssueSeverity.Warning => WarehouseProposalIssueSeverity.Warning,
                    SpaceCadIssueSeverity.Blocking => WarehouseProposalIssueSeverity.Blocking,
                    _ => throw new ArgumentOutOfRangeException(nameof(issue.Severity)),
                },
                issue.SourceRef,
                issue.SourceRef is not null
                    ? context.SourceKeyByRef.GetValueOrDefault(issue.SourceRef)
                    : null,
                DetailToken: issue.DetailToken))
            .ToList();
        issues.AddRange(request.Ai.Output.Diagnostics.Select(diagnostic =>
            new WarehouseProposalIssueV1(
                $"PROVIDER_{diagnostic.Code}",
                diagnostic.Severity switch
                {
                    WarehouseDiagnosticSeverity.Info => WarehouseProposalIssueSeverity.Info,
                    WarehouseDiagnosticSeverity.Warning => WarehouseProposalIssueSeverity.Warning,
                    WarehouseDiagnosticSeverity.Error => WarehouseProposalIssueSeverity.Blocking,
                    _ => throw new ArgumentOutOfRangeException(nameof(diagnostic.Severity)),
                },
                SourceRefForKey(context, diagnostic.SourceKey),
                diagnostic.SourceKey)));
        return issues;
    }

    private static Dictionary<string, List<FieldCandidate>> CandidateFields(
        SpaceCadSemanticPreviewItemV1 rule,
        WarehouseSpaceType ruleType,
        WarehouseGenerationSuggestion? ai,
        IReadOnlyList<SpaceAiCadLockedFactV1> locked,
        IReadOnlyList<SpaceAiCadLockedFactV1> defaults)
    {
        var result = new Dictionary<string, List<FieldCandidate>>(StringComparer.Ordinal);
        AddCandidate(result, "type", ruleType.ToString(),
            WarehouseFusionSource.DeterministicRule, rule.Confidence,
            [RuleEvidence(rule)]);
        if (rule.TargetSubtype is not null)
        {
            AddCandidate(result, "attributes.semanticLabel", rule.TargetSubtype,
                WarehouseFusionSource.DeterministicRule, rule.Confidence,
                [RuleEvidence(rule)]);
        }
        if (ai is not null)
        {
            var evidence = ai.EvidenceCodes
                .Order()
                .Select(code => $"AI:{code}")
                .ToArray();
            AddCandidate(result, "type", ai.SuggestedType.ToString(),
                WarehouseFusionSource.Ai, ai.Confidence, evidence);
            AddAiAttribute(result, "attributes.zonePurpose", ai.Attributes.ZonePurpose, ai, evidence);
            AddAiAttribute(result, "attributes.rackType", ai.Attributes.RackType, ai, evidence);
            AddAiAttribute(result, "attributes.doorType", ai.Attributes.DoorType, ai, evidence);
            AddAiAttribute(result, "attributes.dockType", ai.Attributes.DockType, ai, evidence);
            AddAiAttribute(result, "attributes.equipmentType", ai.Attributes.EquipmentType, ai, evidence);
            if (ai.Attributes.SemanticLabel is not null)
            {
                AddCandidate(result, "attributes.semanticLabel", ai.Attributes.SemanticLabel,
                    WarehouseFusionSource.Ai, ai.Confidence, evidence);
            }
        }
        foreach (var fact in defaults)
        {
            AddCandidate(result, fact.FieldPath, fact.ValueToken,
                WarehouseFusionSource.TemplateDefault, 0.5m,
                ["TEMPLATE_DEFAULT"]);
        }
        foreach (var fact in locked)
        {
            AddCandidate(result, fact.FieldPath, fact.ValueToken,
                WarehouseFusionSource.HumanLocked, 1m,
                ["HUMAN_LOCKED"]);
        }
        return result;
    }

    private static void AddAiAttribute<T>(
        IDictionary<string, List<FieldCandidate>> fields,
        string path,
        T? value,
        WarehouseGenerationSuggestion ai,
        IReadOnlyList<string> evidence) where T : struct, Enum
    {
        if (value is not null)
        {
            AddCandidate(fields, path, value.Value.ToString(),
                WarehouseFusionSource.Ai, ai.Confidence, evidence);
        }
    }

    private static void AddCandidate(
        IDictionary<string, List<FieldCandidate>> fields,
        string path,
        string value,
        WarehouseFusionSource source,
        decimal confidence,
        IReadOnlyList<string> evidence)
    {
        if (!fields.TryGetValue(path, out var candidates))
        {
            candidates = [];
            fields.Add(path, candidates);
        }
        candidates.Add(new FieldCandidate(source, value, confidence, evidence));
    }

    private static WarehouseResolvedFieldV1 ResolveField(
        string sourceRef,
        string sourceKey,
        string fieldPath,
        IReadOnlyList<FieldCandidate> candidates,
        decimal ruleConfidence,
        ICollection<WarehouseProposalIssueV1> issues,
        out bool softRuleConflict)
    {
        var ordered = candidates
            .OrderByDescending(candidate => candidate.Source)
            .ThenBy(candidate => candidate.Value, StringComparer.Ordinal)
            .ToArray();
        var winner = ordered[0];
        var conflicts = ordered
            .Where(candidate => !candidate.Value.Equals(
                winner.Value,
                StringComparison.Ordinal))
            .ToArray();
        softRuleConflict = false;
        if (winner.Source == WarehouseFusionSource.HumanLocked
            && conflicts.Any(candidate => candidate.Source == WarehouseFusionSource.Ai))
        {
            issues.Add(new WarehouseProposalIssueV1(
                "AI_LOCKED_VALUE_CONFLICT",
                WarehouseProposalIssueSeverity.Info,
                sourceRef,
                sourceKey,
                fieldPath));
        }
        if (winner.Source == WarehouseFusionSource.HumanLocked
            && conflicts.Any(candidate =>
                candidate.Source == WarehouseFusionSource.DeterministicRule))
        {
            issues.Add(new WarehouseProposalIssueV1(
                "LOCKED_RULE_VALUE_CONFLICT",
                WarehouseProposalIssueSeverity.Warning,
                sourceRef,
                sourceKey,
                fieldPath));
        }
        if (winner.Source == WarehouseFusionSource.DeterministicRule
            && conflicts.Any(candidate => candidate.Source == WarehouseFusionSource.Ai))
        {
            softRuleConflict = ruleConfidence < 1m;
            issues.Add(new WarehouseProposalIssueV1(
                "AI_RULE_VALUE_CONFLICT",
                softRuleConflict
                    ? WarehouseProposalIssueSeverity.Warning
                    : WarehouseProposalIssueSeverity.Info,
                sourceRef,
                sourceKey,
                fieldPath,
                softRuleConflict ? "confidence-downgraded" : "strong-rule-retained"));
        }

        var confidence = softRuleConflict
            ? decimal.Min(winner.Confidence, conflicts
                .Where(candidate => candidate.Source == WarehouseFusionSource.Ai)
                .Select(candidate => candidate.Confidence)
                .DefaultIfEmpty(winner.Confidence)
                .Min())
            : winner.Confidence;
        return new WarehouseResolvedFieldV1(
            fieldPath,
            winner.Value,
            winner.Source,
            confidence,
            ordered.Select(candidate => new WarehouseFusionEvidenceV1(
                    candidate.Source,
                    candidate.Value,
                    candidate.Confidence,
                    candidate.Evidence.Order(StringComparer.Ordinal).ToArray()))
                .ToArray());
    }

    private static void AddUnresolvedInputIssues(
        WarehouseDraftSynthesisRequestV1 request,
        BindingContext context,
        IReadOnlyList<MutableProposal> proposals,
        ICollection<WarehouseProposalIssueV1> issues)
    {
        var proposalKeys = proposals
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var suggestion in request.Ai.Output.Suggestions
                     .Where(item => !proposalKeys.Contains(item.SourceKey)))
        {
            issues.Add(new WarehouseProposalIssueV1(
                "AI_GEOMETRY_RULE_REQUIRED",
                WarehouseProposalIssueSeverity.Blocking,
                SourceRefForKey(context, suggestion.SourceKey),
                suggestion.SourceKey,
                "geometry"));
        }
        foreach (var fact in request.LockedFacts
                     .Where(fact => !proposalKeys.Contains(
                         context.SourceKeyByRef[fact.SourceRef])))
        {
            issues.Add(new WarehouseProposalIssueV1(
                "LOCKED_GEOMETRY_RULE_REQUIRED",
                WarehouseProposalIssueSeverity.Blocking,
                fact.SourceRef,
                context.SourceKeyByRef[fact.SourceRef],
                fact.FieldPath));
        }
        var proposalRefs = proposals
            .Select(item => item.SourceRef)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var profile in request.RackProfiles
                     .Where(item => !proposalRefs.Contains(item.SourceRef)))
        {
            issues.Add(new WarehouseProposalIssueV1(
                "RACK_PROFILE_TARGET_INVALID",
                WarehouseProposalIssueSeverity.Blocking,
                profile.SourceRef,
                context.SourceKeyByRef[profile.SourceRef]));
        }
    }

    private static void AddRelations(
        BindingContext context,
        IReadOnlyList<MutableProposal> proposals,
        ICollection<WarehouseProposalIssueV1> issues)
    {
        var proposalsByKey = proposals
            .GroupBy(item => item.SourceKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.SourceRef, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var pending = new List<PendingRelation>();
        foreach (var proposal in proposals)
        {
            if (!context.SuggestionByKey.TryGetValue(proposal.SourceKey, out var suggestion))
                continue;
            foreach (var relation in suggestion.Relations)
            {
                if (!proposalsByKey.TryGetValue(relation.TargetSourceKey, out var targets))
                {
                    issues.Add(new WarehouseProposalIssueV1(
                        "AI_RELATION_TARGET_UNRESOLVED",
                        WarehouseProposalIssueSeverity.Blocking,
                        proposal.SourceRef,
                        proposal.SourceKey,
                        "relations"));
                    continue;
                }
                foreach (var target in targets)
                {
                    pending.Add(new PendingRelation(
                        proposal,
                        target,
                        relation.RelationType,
                        relation.Confidence));
                }
            }
        }

        var cyclicIds = CyclicParentCore(pending);
        foreach (var relation in pending
                     .OrderBy(item => item.Source.SourceRef, StringComparer.Ordinal)
                     .ThenBy(item => item.Type)
                     .ThenBy(item => item.Target.SourceRef, StringComparer.Ordinal))
        {
            if (IsParent(relation.Type)
                && (cyclicIds.Contains(relation.Source.LogicalId)
                    || cyclicIds.Contains(relation.Target.LogicalId)))
            {
                issues.Add(new WarehouseProposalIssueV1(
                    "AI_PARENT_RELATION_CYCLE",
                    WarehouseProposalIssueSeverity.Blocking,
                    relation.Source.SourceRef,
                    relation.Source.SourceKey,
                    "relations"));
                continue;
            }
            relation.Source.Relations.Add(new WarehouseProposalRelationV1(
                relation.Type,
                relation.Target.LogicalId,
                relation.Confidence,
                ["AI_RELATION"]));
        }
    }

    private static HashSet<Guid> CyclicParentCore(
        IReadOnlyList<PendingRelation> relations)
    {
        var edges = relations.Where(item => IsParent(item.Type)).ToArray();
        var outgoing = new Dictionary<Guid, HashSet<Guid>>();
        var incoming = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var edge in edges)
        {
            AddEdge(outgoing, edge.Source.LogicalId, edge.Target.LogicalId);
            AddEdge(incoming, edge.Target.LogicalId, edge.Source.LogicalId);
            outgoing.TryAdd(edge.Target.LogicalId, []);
            incoming.TryAdd(edge.Source.LogicalId, []);
        }
        var active = outgoing.Keys.ToHashSet();
        var queue = new Queue<Guid>(active.Where(id =>
            outgoing[id].Count == 0 || incoming[id].Count == 0));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!active.Remove(id)) continue;
            foreach (var target in outgoing[id])
            {
                incoming[target].Remove(id);
                if (active.Contains(target)
                    && (incoming[target].Count == 0 || outgoing[target].Count == 0))
                {
                    queue.Enqueue(target);
                }
            }
            foreach (var source in incoming[id])
            {
                outgoing[source].Remove(id);
                if (active.Contains(source)
                    && (incoming[source].Count == 0 || outgoing[source].Count == 0))
                {
                    queue.Enqueue(source);
                }
            }
        }
        return active;
    }

    private static void AddEdge(
        IDictionary<Guid, HashSet<Guid>> map,
        Guid source,
        Guid target)
    {
        if (!map.TryGetValue(source, out var targets))
        {
            targets = [];
            map.Add(source, targets);
        }
        targets.Add(target);
    }

    private static void AddRackDerivations(
        WarehouseDraftSynthesisRequestV1 request,
        BindingContext context,
        IReadOnlyList<MutableProposal> proposals,
        ICollection<WarehouseProposalIssueV1> issues)
    {
        foreach (var proposal in proposals)
        {
            context.ProfilesByRef.TryGetValue(proposal.SourceRef, out var bindings);
            if (proposal.ObjectType != WarehouseSpaceType.Rack)
            {
                if (bindings is { Length: > 0 })
                {
                    issues.Add(new WarehouseProposalIssueV1(
                        "RACK_PROFILE_TARGET_INVALID",
                        WarehouseProposalIssueSeverity.Blocking,
                        proposal.SourceRef,
                        proposal.SourceKey));
                }
                continue;
            }
            proposal.CodeState = WarehouseProposalCodeState.ExistingServicePrecheckRequired;
            if (bindings is not { Length: > 0 })
            {
                issues.Add(new WarehouseProposalIssueV1(
                    SpaceErrorCodes.RackProfileRequired,
                    WarehouseProposalIssueSeverity.Blocking,
                    proposal.SourceRef,
                    proposal.SourceKey,
                    "rackProfile"));
                continue;
            }
            var winner = bindings[0];
            var profile = winner.Profile;
            var levels = profile.Levels
                .OrderBy(level => level.LevelNo)
                .Select(level =>
                {
                    var count = checked((long)level.BinCount * level.DepthCount);
                    return new WarehouseRackLevelDerivationV1(
                        WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                            proposal.LogicalId,
                            level.LevelNo),
                        level.LevelNo,
                        level.BottomZMillimeters,
                        level.ClearHeightMillimeters,
                        level.BinCount,
                        level.DepthCount,
                        level.CellWidthMillimeters,
                        level.CellDepthMillimeters,
                        level.BeamHeightMillimeters,
                        level.MaxLoadKilograms,
                        count,
                        WarehouseDeterministicIdentity.CreateLocationLogicalId(
                            proposal.LogicalId,
                            level.LevelNo,
                            1,
                            1),
                        WarehouseDeterministicIdentity.CreateLocationLogicalId(
                            proposal.LogicalId,
                            level.LevelNo,
                            level.BinCount,
                            level.DepthCount));
                })
                .ToArray();
            var normalizedProfile = profile with
            {
                Levels = profile.Levels.OrderBy(level => level.LevelNo).ToArray(),
            };
            proposal.RackDerivation = new WarehouseRackDerivationV1(
                profile.ProfileVersionId,
                ComputeSha256(CanonicalBytes(normalizedProfile)),
                winner.Source,
                bindings.Select(binding => binding.Source).Distinct().ToArray(),
                profile.RackWidthMillimeters,
                profile.RackDepthMillimeters,
                profile.RackHeightMillimeters,
                levels.Sum(level => level.LocationCount),
                levels,
                WarehouseDraftSynthesisVersions.IdentityAlgorithm,
                RequiresExistingCodeServicePrecheck: true);
        }
    }

    private static WarehouseFusionConfidenceBand ConfidenceBand(
        decimal confidence,
        bool softRuleConflict)
    {
        if (!softRuleConflict && confidence >= 0.90m)
            return WarehouseFusionConfidenceBand.High;
        if (confidence >= 0.70m)
            return WarehouseFusionConfidenceBand.Medium;
        return WarehouseFusionConfidenceBand.Low;
    }

    private static bool FieldApplies(string fieldPath, WarehouseSpaceType type) =>
        fieldPath switch
        {
            "attributes.zonePurpose" => type == WarehouseSpaceType.Zone,
            "attributes.rackType" => type == WarehouseSpaceType.Rack,
            "attributes.doorType" => type == WarehouseSpaceType.Door,
            "attributes.dockType" => type == WarehouseSpaceType.Dock,
            "attributes.equipmentType" => type == WarehouseSpaceType.StaticEquipment,
            "attributes.direction" => type == WarehouseSpaceType.Aisle,
            "attributes.wallType" => type == WarehouseSpaceType.Wall,
            "attributes.columnType" => type == WarehouseSpaceType.Column,
            "relations.zoneSourceKey" => type is
                WarehouseSpaceType.Rack or
                WarehouseSpaceType.Dock or
                WarehouseSpaceType.StaticEquipment,
            "relations.aisleSourceKey" => type == WarehouseSpaceType.Rack,
            "relations.wallSourceKey" => type == WarehouseSpaceType.Door,
            _ => true,
        };

    private static IReadOnlySet<string> Values(params string[] values) =>
        values.ToHashSet(StringComparer.Ordinal);

    private static bool TryMapType(
        SpaceCadSemanticTarget target,
        out WarehouseSpaceType type)
    {
        type = target switch
        {
            SpaceCadSemanticTarget.Wall => WarehouseSpaceType.Wall,
            SpaceCadSemanticTarget.Column => WarehouseSpaceType.Column,
            SpaceCadSemanticTarget.Door => WarehouseSpaceType.Door,
            SpaceCadSemanticTarget.Dock => WarehouseSpaceType.Dock,
            SpaceCadSemanticTarget.Zone => WarehouseSpaceType.Zone,
            SpaceCadSemanticTarget.Aisle => WarehouseSpaceType.Aisle,
            SpaceCadSemanticTarget.Rack => WarehouseSpaceType.Rack,
            SpaceCadSemanticTarget.Equipment => WarehouseSpaceType.StaticEquipment,
            SpaceCadSemanticTarget.VerticalCirculation => WarehouseSpaceType.StaticEquipment,
            SpaceCadSemanticTarget.RestrictedArea => WarehouseSpaceType.Zone,
            _ => WarehouseSpaceType.Unknown,
        };
        return type != WarehouseSpaceType.Unknown;
    }

    private static string RuleEvidence(SpaceCadSemanticPreviewItemV1 item) =>
        item.AppliedMapping.RuleId is null
            ? $"RULE:{item.AppliedMapping.DecisionSource}"
            : $"RULE:{item.AppliedMapping.RuleId}";

    private static bool IsParent(WarehouseRelationType type) =>
        type is WarehouseRelationType.ParentCandidate
            or WarehouseRelationType.ContainedBy;

    private static string? SourceRefForKey(
        BindingContext context,
        string? sourceKey)
    {
        if (sourceKey is null) return null;
        return context.SourceKeyByRef
            .Where(pair => pair.Value.Equals(sourceKey, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key)
            .FirstOrDefault();
    }

    private static int GenerationOrder(WarehouseSpaceType type) => type switch
    {
        WarehouseSpaceType.Floor => 0,
        WarehouseSpaceType.Zone => 1,
        WarehouseSpaceType.Wall or WarehouseSpaceType.Column
            or WarehouseSpaceType.Door or WarehouseSpaceType.Dock => 2,
        WarehouseSpaceType.Aisle => 3,
        WarehouseSpaceType.Rack => 4,
        WarehouseSpaceType.StaticEquipment => 7,
        _ => 8,
    };

    private static WarehouseProposalIssueV1[] CanonicalIssues(
        IEnumerable<WarehouseProposalIssueV1> issues) => issues
        .Distinct()
        .OrderByDescending(issue => issue.Severity)
        .ThenBy(issue => issue.Code, StringComparer.Ordinal)
        .ThenBy(issue => issue.SourceRef, StringComparer.Ordinal)
        .ThenBy(issue => issue.SourceKey, StringComparer.Ordinal)
        .ThenBy(issue => issue.FieldPath, StringComparer.Ordinal)
        .ThenBy(issue => issue.DetailToken, StringComparer.Ordinal)
        .ToArray();

    private static WarehouseDraftProposalSummaryV1 Summary(
        IReadOnlyList<WarehouseDraftProposalV1> proposals,
        IReadOnlyList<WarehouseProposalIssueV1> issues) => new(
        proposals.Count,
        proposals.LongCount(item =>
            item.ConfidenceBand == WarehouseFusionConfidenceBand.High),
        proposals.LongCount(item =>
            item.ConfidenceBand == WarehouseFusionConfidenceBand.Medium),
        proposals.LongCount(item =>
            item.ConfidenceBand == WarehouseFusionConfidenceBand.Low),
        proposals.LongCount(item => item.ObjectType == WarehouseSpaceType.Rack),
        proposals.Sum(item => (long)(item.RackDerivation?.Levels.Count ?? 0)),
        proposals.Sum(item => item.RackDerivation?.LocationCount ?? 0),
        issues.LongCount(item => item.Severity == WarehouseProposalIssueSeverity.Info),
        issues.LongCount(item => item.Severity == WarehouseProposalIssueSeverity.Warning),
        issues.LongCount(item => item.Severity == WarehouseProposalIssueSeverity.Blocking),
        CanEnterReview: proposals.Count > 0,
        ReadyForApply: false);

    private static void EnsureUniqueLogicalIds(
        IReadOnlyList<WarehouseDraftProposalV1> proposals)
    {
        if (proposals.Select(item => item.LogicalId).Distinct().Count()
            != proposals.Count)
        {
            throw new InvalidDataException(
                "Deterministic warehouse proposal identities collided.");
        }
    }

    private static void ValidateProposalSet(WarehouseDraftProposalSetV1 proposalSet)
    {
        ArgumentNullException.ThrowIfNull(proposalSet);
        ArgumentNullException.ThrowIfNull(proposalSet.Proposals);
        ArgumentNullException.ThrowIfNull(proposalSet.Issues);
        ArgumentNullException.ThrowIfNull(proposalSet.Summary);
        if (proposalSet.SchemaVersion != WarehouseDraftSynthesisVersions.SchemaVersion
            || !proposalSet.IsReadOnlyPreview
            || proposalSet.DraftWritten
            || proposalSet.TenantId == Guid.Empty
            || proposalSet.ModelVersionId == Guid.Empty
            || proposalSet.FloorLogicalId == Guid.Empty
            || !IsSha256(proposalSet.SourceSha256)
            || !IsSha256(proposalSet.CoordinateTransformSha256)
            || !IsSha256(proposalSet.SemanticPreviewSha256)
            || !IsSha256(proposalSet.ProviderInputSha256)
            || !IsSha256(proposalSet.ProviderOutputSha256)
            || !IsSha256(proposalSet.SourceMapSha256)
            || !IsSha256(proposalSet.ProposalSetSha256)
            || !IsSafeToken(proposalSet.RuleVersion, 64)
            || proposalSet.Summary.ReadyForApply
            || proposalSet.Proposals.Any(item =>
                item.GeometrySource != WarehouseProposalGeometrySource.CadIrDeterministicRule)
            || !proposalSet.Proposals.SequenceEqual(proposalSet.Proposals
                .OrderBy(item => GenerationOrder(item.ObjectType))
                .ThenBy(item => item.SourceRef, StringComparer.Ordinal))
            || !proposalSet.Issues.SequenceEqual(CanonicalIssues(proposalSet.Issues)))
        {
            throw new InvalidDataException("Warehouse proposal set is invalid.");
        }
        EnsureUniqueLogicalIds(proposalSet.Proposals);
        var logicalIds = proposalSet.Proposals
            .Select(item => item.LogicalId)
            .ToHashSet();
        foreach (var proposal in proposalSet.Proposals)
        {
            ValidateProposal(proposalSet, proposal, logicalIds);
        }
        foreach (var issue in proposalSet.Issues)
        {
            if (!Enum.IsDefined(issue.Severity)
                || !IsSafeToken(issue.Code, 512)
                || issue.SourceRef is not null
                    && !IsSafeToken(issue.SourceRef, 512)
                || issue.SourceKey is not null
                    && !IsSafeToken(issue.SourceKey, 256)
                || issue.FieldPath is not null
                    && !IsSafeToken(issue.FieldPath, 256)
                || issue.DetailToken is not null
                    && !IsSafeToken(issue.DetailToken, 512))
            {
                throw new InvalidDataException(
                    "Warehouse proposal issue is invalid.");
            }
        }
        if (proposalSet.Summary != Summary(proposalSet.Proposals, proposalSet.Issues))
        {
            throw new InvalidDataException(
                "Warehouse proposal summary is inconsistent.");
        }
        var expectedHash = ComputeSha256(CanonicalBytes(
            proposalSet with { ProposalSetSha256 = string.Empty }));
        if (!proposalSet.ProposalSetSha256.Equals(expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Warehouse proposal set hash is invalid.");
        }
    }

    private static void ValidateProposal(
        WarehouseDraftProposalSetV1 proposalSet,
        WarehouseDraftProposalV1 proposal,
        IReadOnlySet<Guid> logicalIds)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(proposal.Geometry);
        ArgumentNullException.ThrowIfNull(proposal.Fields);
        ArgumentNullException.ThrowIfNull(proposal.Relations);
        var expectedLogicalId = WarehouseDeterministicIdentity.CreateObjectLogicalId(
            proposalSet.ModelVersionId,
            proposalSet.SourceSha256,
            proposal.SourceKey);
        var expectedCodeState = proposal.ObjectType == WarehouseSpaceType.Rack
            ? WarehouseProposalCodeState.ExistingServicePrecheckRequired
            : WarehouseProposalCodeState.NotApplicable;
        var typeField = proposal.Fields.SingleOrDefault(field =>
            field.FieldPath == "type");
        var hasBlocking = proposalSet.Issues.Any(issue =>
            issue.SourceRef == proposal.SourceRef
            && issue.Severity == WarehouseProposalIssueSeverity.Blocking);
        if (proposal.LogicalId != expectedLogicalId
            || !IsSafeToken(proposal.SourceKey, 256)
            || !IsSafeToken(proposal.SourceRef, 512)
            || !Enum.IsDefined(proposal.ObjectType)
            || !Enum.IsDefined(proposal.GeometrySource)
            || proposal.CodeState != expectedCodeState
            || proposal.Confidence is < 0 or > 1
            || !Enum.IsDefined(proposal.ConfidenceBand)
            || proposal.ConfidenceBand == WarehouseFusionConfidenceBand.High
                && proposal.Confidence < 0.90m
            || proposal.ConfidenceBand == WarehouseFusionConfidenceBand.Medium
                && proposal.Confidence < 0.70m
            || proposal.ConfidenceBand == WarehouseFusionConfidenceBand.Low
                && proposal.Confidence >= 0.70m
            || !proposal.RequiresHumanReview
            || proposal.CanBatchAccept !=
               (proposal.ConfidenceBand == WarehouseFusionConfidenceBand.High
                && !hasBlocking)
            || !proposal.Fields.SequenceEqual(proposal.Fields
                .OrderBy(field => field.FieldPath, StringComparer.Ordinal))
            || proposal.Fields.Select(field => field.FieldPath).Distinct(
                    StringComparer.Ordinal).Count() != proposal.Fields.Count
            || typeField is null
            || !typeField.ValueToken.Equals(
                proposal.ObjectType.ToString(),
                StringComparison.Ordinal)
            || !proposal.Relations.SequenceEqual(proposal.Relations
                .OrderBy(item => item.RelationType)
                .ThenBy(item => item.TargetLogicalId))
            || proposal.Relations.Distinct().Count() != proposal.Relations.Count)
        {
            throw new InvalidDataException("Warehouse proposal is invalid.");
        }

        foreach (var field in proposal.Fields)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(field.Evidence);
            var winner = field.Evidence.FirstOrDefault();
            if (!IsSafeToken(field.FieldPath, 256)
                || !IsSafeToken(field.ValueToken, 256)
                || !Enum.IsDefined(field.WinningSource)
                || field.Confidence is < 0 or > 1
                || winner is null
                || winner.Source != field.WinningSource
                || !winner.ValueToken.Equals(field.ValueToken, StringComparison.Ordinal)
                || !field.Evidence.SequenceEqual(field.Evidence
                    .OrderByDescending(item => item.Source)
                    .ThenBy(item => item.ValueToken, StringComparer.Ordinal)))
            {
                throw new InvalidDataException(
                    "Warehouse resolved proposal field is invalid.");
            }
            foreach (var evidence in field.Evidence)
            {
                ArgumentNullException.ThrowIfNull(evidence);
                ArgumentNullException.ThrowIfNull(evidence.EvidenceCodes);
                if (!Enum.IsDefined(evidence.Source)
                    || !IsSafeToken(evidence.ValueToken, 256)
                    || evidence.Confidence is < 0 or > 1
                    || evidence.EvidenceCodes.Count == 0
                    || !evidence.EvidenceCodes.SequenceEqual(
                        evidence.EvidenceCodes.Order(StringComparer.Ordinal))
                    || evidence.EvidenceCodes.Any(code => !IsSafeToken(code, 256)))
                {
                    throw new InvalidDataException(
                        "Warehouse proposal field evidence is invalid.");
                }
            }
        }

        foreach (var relation in proposal.Relations)
        {
            ArgumentNullException.ThrowIfNull(relation);
            ArgumentNullException.ThrowIfNull(relation.EvidenceCodes);
            if (!Enum.IsDefined(relation.RelationType)
                || relation.TargetLogicalId == proposal.LogicalId
                || !logicalIds.Contains(relation.TargetLogicalId)
                || relation.Confidence is < 0 or > 1
                || relation.EvidenceCodes.Count == 0
                || relation.EvidenceCodes.Any(code => !IsSafeToken(code, 256)))
            {
                throw new InvalidDataException(
                    "Warehouse proposal relation is invalid.");
            }
        }

        if (proposal.ObjectType != WarehouseSpaceType.Rack)
        {
            if (proposal.RackDerivation is not null)
                throw new InvalidDataException("Non-rack proposal has rack derivation.");
            return;
        }
        if (proposal.RackDerivation is { } rack)
            ValidateRackDerivation(proposal.LogicalId, rack);
    }

    private static void ValidateRackDerivation(
        Guid rackLogicalId,
        WarehouseRackDerivationV1 rack)
    {
        ArgumentNullException.ThrowIfNull(rack.EvidenceSources);
        ArgumentNullException.ThrowIfNull(rack.Levels);
        if (rack.ProfileVersionId == Guid.Empty
            || !IsSha256(rack.ProfileSha256)
            || !Enum.IsDefined(rack.WinningSource)
            || rack.EvidenceSources.Count == 0
            || rack.EvidenceSources[0] != rack.WinningSource
            || !rack.EvidenceSources.SequenceEqual(
                rack.EvidenceSources.Distinct().OrderDescending())
            || rack.RackWidthMillimeters <= 0
            || rack.RackDepthMillimeters <= 0
            || rack.RackHeightMillimeters <= 0
            || rack.LocationCount != rack.Levels.Sum(level => level.LocationCount)
            || rack.IdentityAlgorithm != WarehouseDraftSynthesisVersions.IdentityAlgorithm
            || !rack.RequiresExistingCodeServicePrecheck
            || !rack.Levels.SequenceEqual(rack.Levels.OrderBy(level => level.LevelNo)))
        {
            throw new InvalidDataException("Warehouse rack derivation is invalid.");
        }
        foreach (var level in rack.Levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            var expectedCount = checked((long)level.BinCount * level.DepthCount);
            if (level.LogicalId !=
                    WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                        rackLogicalId,
                        level.LevelNo)
                || level.LocationCount != expectedCount
                || level.FirstLocationLogicalId !=
                    WarehouseDeterministicIdentity.CreateLocationLogicalId(
                        rackLogicalId,
                        level.LevelNo,
                        1,
                        1)
                || level.LastLocationLogicalId !=
                    WarehouseDeterministicIdentity.CreateLocationLogicalId(
                        rackLogicalId,
                        level.LevelNo,
                        level.BinCount,
                        level.DepthCount))
            {
                throw new InvalidDataException(
                    "Warehouse rack level derivation is invalid.");
            }
        }
    }

    private static byte[] CanonicalBytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, CanonicalJsonOptions);

    private static string ComputeSha256(byte[] bytes)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSafeToken(string? value, int maximumLength) =>
        value is { Length: > 0 }
        && value.Length <= maximumLength
        && value.Equals(value.Trim(), StringComparison.Ordinal)
        && value.All(character => character >= ' ' && character != '\u007f');

    private sealed record FieldCandidate(
        WarehouseFusionSource Source,
        string Value,
        decimal Confidence,
        IReadOnlyList<string> Evidence);

    private sealed record BindingContext(
        IReadOnlyDictionary<string, string> SourceKeyByRef,
        IReadOnlyDictionary<string, WarehouseGenerationSuggestion> SuggestionByKey,
        IReadOnlyDictionary<string, SpaceAiCadLockedFactV1[]> LockedByRef,
        IReadOnlyDictionary<string, SpaceAiCadLockedFactV1[]> DefaultsByRef,
        IReadOnlyDictionary<string, WarehouseRackProfileBindingV1[]> ProfilesByRef);

    private sealed record PendingRelation(
        MutableProposal Source,
        MutableProposal Target,
        WarehouseRelationType Type,
        decimal Confidence);

    private sealed class MutableProposal(
        Guid logicalId,
        string sourceKey,
        string sourceRef,
        WarehouseSpaceType objectType,
        SpaceCadSemanticGeometryV1 geometry,
        IReadOnlyList<WarehouseResolvedFieldV1> fields,
        decimal confidence,
        WarehouseFusionConfidenceBand confidenceBand)
    {
        public Guid LogicalId { get; } = logicalId;
        public string SourceKey { get; } = sourceKey;
        public string SourceRef { get; } = sourceRef;
        public WarehouseSpaceType ObjectType { get; } = objectType;
        public SpaceCadSemanticGeometryV1 Geometry { get; } = geometry;
        public IReadOnlyList<WarehouseResolvedFieldV1> Fields { get; } = fields;
        public decimal Confidence { get; } = confidence;
        public WarehouseFusionConfidenceBand ConfidenceBand { get; } = confidenceBand;
        public List<WarehouseProposalRelationV1> Relations { get; } = [];
        public WarehouseProposalCodeState CodeState { get; set; } =
            WarehouseProposalCodeState.NotApplicable;
        public WarehouseRackDerivationV1? RackDerivation { get; set; }

        public WarehouseDraftProposalV1 ToImmutable(
            IReadOnlyList<WarehouseProposalIssueV1> issues)
        {
            var hasBlocking = issues.Any(issue =>
                issue.SourceRef == SourceRef
                && issue.Severity == WarehouseProposalIssueSeverity.Blocking);
            return new WarehouseDraftProposalV1(
                LogicalId,
                SourceKey,
                SourceRef,
                ObjectType,
                Geometry,
                WarehouseProposalGeometrySource.CadIrDeterministicRule,
                CodeState,
                Fields,
                Relations
                    .Distinct()
                    .OrderBy(item => item.RelationType)
                    .ThenBy(item => item.TargetLogicalId)
                    .ToArray(),
                Confidence,
                ConfidenceBand,
                RequiresHumanReview: true,
                CanBatchAccept: ConfidenceBand == WarehouseFusionConfidenceBand.High
                                && !hasBlocking,
                RackDerivation);
        }
    }
}

public static class WarehouseDeterministicIdentity
{
    public static Guid CreateObjectLogicalId(
        Guid modelVersionNamespace,
        string sourceSha256,
        string sourceKey)
    {
        if (modelVersionNamespace == Guid.Empty
            || !IsSha256(sourceSha256)
            || string.IsNullOrWhiteSpace(sourceKey))
        {
            throw new ArgumentException("Deterministic object identity input is invalid.");
        }
        return CreateVersion5(
            modelVersionNamespace,
            $"warehouse-object\n{sourceSha256}\n{sourceKey}");
    }

    public static Guid CreateRackLevelLogicalId(Guid rackLogicalId, int levelNo)
    {
        if (rackLogicalId == Guid.Empty || levelNo <= 0)
            throw new ArgumentException("Rack level identity input is invalid.");
        return CreateVersion5(rackLogicalId, $"rack-level\n{levelNo}");
    }

    public static Guid CreateLocationLogicalId(
        Guid rackLogicalId,
        int levelNo,
        int columnNo,
        int depthNo)
    {
        if (rackLogicalId == Guid.Empty
            || levelNo <= 0
            || columnNo <= 0
            || depthNo <= 0)
        {
            throw new ArgumentException("Location identity input is invalid.");
        }
        return CreateVersion5(
            rackLogicalId,
            $"location\n{levelNo}\n{columnNo}\n{depthNo}");
    }

    private static Guid CreateVersion5(Guid namespaceId, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes);
        SwapGuidByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] hash;
        using (var algorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
        {
            algorithm.AppendData(namespaceBytes);
            algorithm.AppendData(nameBytes);
            hash = algorithm.GetHashAndReset();
        }
        try
        {
            hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
            Span<byte> guidBytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(guidBytes);
            SwapGuidByteOrder(guidBytes);
            return new Guid(guidBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nameBytes);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static void SwapGuidByteOrder(Span<byte> bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
