using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public static class SpaceExcelCadMatching
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly HashSet<string> RackCodeAttributeKeys = new(
        ["RACK_ID", "RACK_CODE", "RACKCODE", "CODE", "BUSINESS_KEY"],
        StringComparer.OrdinalIgnoreCase);

    public static SpaceExcelEditorSnapshotV1 SealEditorSnapshot(
        Guid tenantId,
        Guid modelVersionId,
        Guid floorLogicalId,
        string floorCode,
        long contentRevision,
        string? contentHash,
        IReadOnlyList<SpaceExcelEditorRackSnapshotV1> racks)
    {
        ArgumentNullException.ThrowIfNull(racks);
        var ordered = racks
            .OrderBy(item => item.LogicalId)
            .ThenBy(item => item.RevisionId)
            .ToArray();
        var withoutHash = new SpaceExcelEditorSnapshotV1(
            SpaceExcelCadMatchVersions.SchemaVersion,
            IsReadOnlySnapshot: true,
            tenantId,
            modelVersionId,
            floorLogicalId,
            floorCode,
            contentRevision,
            contentHash,
            ordered,
            SnapshotSha256: string.Empty);
        var snapshot = withoutHash with
        {
            SnapshotSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        ValidateEditorSnapshot(snapshot);
        return snapshot;
    }

    public static SpaceExcelCadMatchPreviewV1 Build(
        Guid tenantId,
        Guid modelVersionId,
        Guid excelSourceId,
        Guid preflightJobId,
        SpaceExcelMappingProfileDto mappingProfile,
        SpaceExcelWorkbookData workbook,
        SpaceCadSemanticPreviewV1 semanticPreview,
        SpaceCadSemanticDiagnosticIndexV1 diagnosticIndex,
        SpaceExcelEditorSnapshotV1 editorSnapshot)
    {
        ArgumentNullException.ThrowIfNull(mappingProfile);
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(semanticPreview);
        ArgumentNullException.ThrowIfNull(diagnosticIndex);
        ArgumentNullException.ThrowIfNull(editorSnapshot);
        if (tenantId == Guid.Empty || modelVersionId == Guid.Empty
            || excelSourceId == Guid.Empty || preflightJobId == Guid.Empty
            || mappingProfile.Id == Guid.Empty || mappingProfile.Version <= 0
            || !IsSha256(mappingProfile.DefinitionHash))
        {
            throw new InvalidDataException(
                "Excel/CAD matching input identity is incomplete.");
        }

        SpaceCadSemanticParser.Validate(semanticPreview);
        SpaceCadSemanticDiagnostics.Validate(diagnosticIndex);
        ValidateEditorSnapshot(editorSnapshot);
        if (semanticPreview.TenantId != tenantId
            || diagnosticIndex.TenantId != tenantId
            || diagnosticIndex.SemanticPreviewSha256 !=
            semanticPreview.SemanticPreviewSha256
            || diagnosticIndex.FloorLogicalId != semanticPreview.FloorLogicalId
            || !diagnosticIndex.FloorCode.Equals(
                semanticPreview.FloorCode,
                StringComparison.Ordinal)
            || editorSnapshot.ModelVersionId != modelVersionId
            || editorSnapshot.TenantId != tenantId
            || editorSnapshot.FloorLogicalId != semanticPreview.FloorLogicalId
            || !editorSnapshot.FloorCode.Equals(
                semanticPreview.FloorCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Excel/CAD matching inputs do not belong to one tenant, model and floor chain.");
        }

        var inspection = new SpaceExcelPreflightValidator().Inspect(
            mappingProfile.Definition,
            workbook);
        var canonicalRows = CanonicalRows(inspection.Rows);
        var workbookProjectionSha256 = ComputeSha256(CanonicalJson(new
        {
            mappingProfile.DefinitionHash,
            Rows = canonicalRows.Select(Projection).ToArray(),
            Findings = CanonicalFindings(inspection.Validation.Findings),
        }));
        var blockingByRow = inspection.Validation.Findings
            .Where(item => item.Severity == SpaceIssueSeverity.Blocking
                           && item.Row.HasValue)
            .GroupBy(item => (item.Sheet, item.Row!.Value))
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Code)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray());
        var excelBlockingCount = inspection.Validation.Findings.LongCount(
            item => item.Severity == SpaceIssueSeverity.Blocking);

        var evidenceByPreviewId = diagnosticIndex.Evidence.ToDictionary(
            item => item.PreviewObjectId,
            StringComparer.Ordinal);
        var cadCandidates = semanticPreview.Items
            .Where(item => item.DraftObjectKind == SpaceCadSemanticDraftObjectKind.Rack
                           && item.Disposition != SpaceCadSemanticDisposition.Rejected)
            .Select(item => new CadCandidate(
                item,
                evidenceByPreviewId[item.PreviewObjectId],
                item.Source.Attributes
                    .Where(attribute => RackCodeAttributeKeys.Contains(attribute.Key)
                                        && !string.IsNullOrWhiteSpace(attribute.Value))
                    .Select(attribute => attribute.Value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(item => item.Item.PreviewObjectId, StringComparer.Ordinal)
            .ToArray();
        var editorCandidates = editorSnapshot.Racks
            .OrderBy(item => item.LogicalId)
            .ToArray();

        var drafts = canonicalRows
            .Where(row => row.TargetSheet.Equals("Racks", StringComparison.Ordinal))
            .Select(row => Draft(
                row,
                mappingProfile.DefinitionHash,
                excelSourceId,
                semanticPreview,
                cadCandidates,
                editorCandidates,
                blockingByRow.GetValueOrDefault((row.SourceSheet, row.RowNumber))))
            .ToArray();
        ApplyDuplicateTargetConflicts(drafts);
        var rows = drafts
            .Select(ToMatch)
            .OrderBy(item => item.SourceSheet, StringComparer.Ordinal)
            .ThenBy(item => item.RowNumber)
            .ThenBy(item => item.ExcelRowId, StringComparer.Ordinal)
            .ToArray();
        var summary = Summary(rows);
        var canConfirm = rows.Length > 0
                         && summary.UnmatchedCount == 0
                         && summary.ConflictCount == 0
                         && summary.ErrorCount == 0
                         && rows.All(row => row.CadConfidenceBand is null
                             or SpaceCadConfidenceBand.High
                             or SpaceCadConfidenceBand.Review)
                         && excelBlockingCount == 0
                         && semanticPreview.ReadyForConfirmation
                         && diagnosticIndex.Summary.BlockingCount == 0;
        var withoutHash = new SpaceExcelCadMatchPreviewV1(
            SpaceExcelCadMatchVersions.SchemaVersion,
            IsReadOnlyPreview: true,
            tenantId,
            modelVersionId,
            excelSourceId,
            preflightJobId,
            mappingProfile.Id,
            mappingProfile.Version,
            mappingProfile.DefinitionHash,
            workbookProjectionSha256,
            semanticPreview.FloorLogicalId,
            semanticPreview.FloorCode,
            semanticPreview.SemanticPreviewSha256,
            diagnosticIndex.DiagnosticIndexSha256,
            semanticPreview.ReadyForConfirmation,
            diagnosticIndex.Summary.BlockingCount,
            excelBlockingCount,
            editorSnapshot.ContentRevision,
            editorSnapshot.ContentHash,
            editorSnapshot.SnapshotSha256,
            rows,
            summary,
            canConfirm,
            MatchPreviewSha256: string.Empty);
        var preview = withoutHash with
        {
            MatchPreviewSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        Validate(preview);
        return preview;
    }

    public static string Serialize(SpaceExcelCadMatchPreviewV1 preview)
    {
        Validate(preview);
        return JsonSerializer.Serialize(preview, CanonicalJsonOptions);
    }

    public static void ValidateEditorSnapshot(SpaceExcelEditorSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Racks);
        if (snapshot.SchemaVersion != SpaceExcelCadMatchVersions.SchemaVersion
            || !snapshot.IsReadOnlySnapshot
            || snapshot.TenantId == Guid.Empty
            || snapshot.ModelVersionId == Guid.Empty
            || snapshot.FloorLogicalId == Guid.Empty
            || !IsToken(snapshot.FloorCode)
            || snapshot.ContentRevision < 0
            || snapshot.ContentHash is not null && !IsSha256(snapshot.ContentHash)
            || !IsSha256(snapshot.SnapshotSha256)
            || !snapshot.Racks.SequenceEqual(snapshot.Racks
                .OrderBy(item => item.LogicalId)
                .ThenBy(item => item.RevisionId)))
        {
            throw new InvalidDataException("Excel editor rack snapshot is invalid.");
        }

        var logicalIds = new HashSet<Guid>();
        var rackCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rack in snapshot.Racks)
        {
            if (rack.LogicalId == Guid.Empty || rack.RevisionId == Guid.Empty
                || !logicalIds.Add(rack.LogicalId)
                || !rackCodes.Add(rack.RackCode)
                || !IsToken(rack.RackCode)
                || rack.SourceRef is not null && !IsSourceRef(rack.SourceRef)
                || !rack.FloorCode.Equals(snapshot.FloorCode, StringComparison.OrdinalIgnoreCase)
                || !IsToken(rack.ZoneCode)
                || rack.WidthMillimeters <= 0
                || rack.DepthMillimeters <= 0
                || rack.HeightMillimeters <= 0
                || !IsToken(rack.LifecycleState))
            {
                throw new InvalidDataException(
                    "Excel editor rack snapshot entry is invalid or duplicated.");
            }
        }
        var expected = ComputeSha256(CanonicalJson(
            snapshot with { SnapshotSha256 = string.Empty }));
        if (!snapshot.SnapshotSha256.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("Excel editor rack snapshot hash is invalid.");
    }

    public static void Validate(SpaceExcelCadMatchPreviewV1 preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(preview.Rows);
        ArgumentNullException.ThrowIfNull(preview.Summary);
        if (preview.SchemaVersion != SpaceExcelCadMatchVersions.SchemaVersion
            || !preview.IsReadOnlyPreview
            || preview.TenantId == Guid.Empty
            || preview.ModelVersionId == Guid.Empty
            || preview.ExcelSourceId == Guid.Empty
            || preview.PreflightJobId == Guid.Empty
            || preview.MappingProfileId == Guid.Empty
            || preview.MappingProfileVersion <= 0
            || !IsSha256(preview.MappingDefinitionSha256)
            || !IsSha256(preview.WorkbookProjectionSha256)
            || preview.FloorLogicalId == Guid.Empty
            || !IsToken(preview.FloorCode)
            || !IsSha256(preview.SemanticPreviewSha256)
            || !IsSha256(preview.DiagnosticIndexSha256)
            || preview.CadBlockingCount < 0
            || preview.ExcelBlockingFindingCount < 0
            || preview.EditorContentRevision < 0
            || preview.EditorContentHash is not null && !IsSha256(preview.EditorContentHash)
            || !IsSha256(preview.EditorSnapshotSha256)
            || !IsSha256(preview.MatchPreviewSha256)
            || !preview.Rows.SequenceEqual(preview.Rows
                .OrderBy(item => item.SourceSheet, StringComparer.Ordinal)
                .ThenBy(item => item.RowNumber)
                .ThenBy(item => item.ExcelRowId, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Excel/CAD match preview identity is invalid.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in preview.Rows)
        {
            ValidateRow(row);
            if (!ids.Add(row.ExcelRowId))
                throw new InvalidDataException("Excel/CAD match row identity is duplicated.");
        }
        var expectedSummary = Summary(preview.Rows);
        var expectedCanConfirm = preview.Rows.Count > 0
                                 && expectedSummary.UnmatchedCount == 0
                                 && expectedSummary.ConflictCount == 0
                                 && expectedSummary.ErrorCount == 0
                                 && preview.Rows.All(row => row.CadConfidenceBand is null
                                     or SpaceCadConfidenceBand.High
                                     or SpaceCadConfidenceBand.Review)
                                 && preview.ExcelBlockingFindingCount == 0
                                 && preview.CadReadyForConfirmation
                                 && preview.CadBlockingCount == 0;
        if (expectedSummary != preview.Summary || expectedCanConfirm != preview.CanConfirm)
            throw new InvalidDataException("Excel/CAD match preview summary is inconsistent.");
        var expectedHash = ComputeSha256(CanonicalJson(
            preview with { MatchPreviewSha256 = string.Empty }));
        if (!preview.MatchPreviewSha256.Equals(expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("Excel/CAD match preview hash is invalid.");
    }

    public static SpaceExcelCadMatchPageV1 Query(
        SpaceExcelCadMatchPreviewV1 preview,
        SpaceExcelCadMatchQueryV1 query)
    {
        Validate(preview);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0 || query.Limit <= 0
            || query.Limit > SpaceExcelCadMatchVersions.MaximumPageSize
            || query.Disposition is { } disposition && !Enum.IsDefined(disposition)
            || query.RackCode is not null && !IsToken(query.RackCode)
            || query.SourceRef is not null && !IsSourceRef(query.SourceRef))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Excel/CAD match query is invalid.");
        }
        var filtered = preview.Rows.Where(row =>
            (query.Disposition is null || row.Disposition == query.Disposition)
            && (query.RackCode is null || row.Values.RackCode?.Equals(
                query.RackCode,
                StringComparison.OrdinalIgnoreCase) == true)
            && (query.SourceRef is null || row.MatchedSourceRef?.Equals(
                query.SourceRef,
                StringComparison.Ordinal) == true)
            && (!query.OnlyLocatable || row.Location?.CanFocusCanvas == true))
            .ToArray();
        return new SpaceExcelCadMatchPageV1(
            query.Offset,
            query.Limit,
            filtered.LongLength,
            filtered.Skip(query.Offset).Take(query.Limit).ToArray());
    }

    private static MatchDraft Draft(
        SpaceExcelCanonicalRow row,
        string mappingDefinitionSha256,
        Guid excelSourceId,
        SpaceCadSemanticPreviewV1 semanticPreview,
        IReadOnlyList<CadCandidate> cadCandidates,
        IReadOnlyList<SpaceExcelEditorRackSnapshotV1> editorCandidates,
        IReadOnlyList<string>? blockingCodes)
    {
        var values = Values(row);
        var rowId = $"excel-row-{ComputeSha256(string.Join(
            '|',
            mappingDefinitionSha256,
            excelSourceId.ToString("N"),
            row.SourceSheet,
            row.RowNumber))[..32]}";
        if (blockingCodes is { Count: > 0 } || values.RackCode is null)
        {
            return new MatchDraft(
                rowId,
                row,
                values,
                SpaceExcelCadMatchDisposition.Error,
                [],
                [],
                [],
                [],
                blockingCodes?.ToArray() ?? ["SPACE_EXCEL_RACK_CODE_INVALID"]);
        }

        var rackCode = values.RackCode;
        var keyEvidence = new List<SpaceExcelCadMatchKeyEvidenceV1>();
        foreach (var candidate in cadCandidates)
        {
            if (candidate.Item.Source.SourceRef.Equals(rackCode, StringComparison.Ordinal))
            {
                keyEvidence.Add(new SpaceExcelCadMatchKeyEvidenceV1(
                    SpaceExcelCadMatchKeyKind.CadSourceRef,
                    rackCode,
                    candidate.Item.PreviewObjectId));
            }
            if (candidate.BusinessKeys.Contains(rackCode, StringComparer.OrdinalIgnoreCase))
            {
                keyEvidence.Add(new SpaceExcelCadMatchKeyEvidenceV1(
                    SpaceExcelCadMatchKeyKind.CadRackCode,
                    rackCode,
                    candidate.Item.PreviewObjectId));
            }
        }
        foreach (var candidate in editorCandidates)
        {
            var candidateId = candidate.LogicalId.ToString("N");
            if (candidate.SourceRef?.Equals(rackCode, StringComparison.Ordinal) == true)
            {
                keyEvidence.Add(new SpaceExcelCadMatchKeyEvidenceV1(
                    SpaceExcelCadMatchKeyKind.EditorSourceRef,
                    rackCode,
                    candidateId));
            }
            if (candidate.RackCode.Equals(rackCode, StringComparison.OrdinalIgnoreCase))
            {
                keyEvidence.Add(new SpaceExcelCadMatchKeyEvidenceV1(
                    SpaceExcelCadMatchKeyKind.EditorRackCode,
                    rackCode,
                    candidateId));
            }
        }
        keyEvidence = keyEvidence
            .Distinct()
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.CandidateId, StringComparer.Ordinal)
            .ToList();
        var cadIds = keyEvidence
            .Where(item => item.Kind is SpaceExcelCadMatchKeyKind.CadSourceRef
                or SpaceExcelCadMatchKeyKind.CadRackCode)
            .Select(item => item.CandidateId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var editorIds = keyEvidence
            .Where(item => item.Kind is SpaceExcelCadMatchKeyKind.EditorSourceRef
                or SpaceExcelCadMatchKeyKind.EditorRackCode)
            .Select(item => item.CandidateId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var cad = cadCandidates.Where(item => cadIds.Contains(item.Item.PreviewObjectId))
            .ToArray();
        var editors = editorCandidates.Where(item => editorIds.Contains(
                item.LogicalId.ToString("N")))
            .ToArray();
        var differences = new List<string>();
        SpaceExcelCadMatchDisposition disposition;
        if (cad.Length > 1 || editors.Length > 1)
        {
            disposition = SpaceExcelCadMatchDisposition.Conflict;
            differences.Add("MultipleCandidates");
        }
        else if (cad.Length == 0 && editors.Length == 0)
        {
            disposition = SpaceExcelCadMatchDisposition.Unmatched;
        }
        else if (values.FloorCode is not null && !values.FloorCode.Equals(
                     semanticPreview.FloorCode,
                     StringComparison.OrdinalIgnoreCase))
        {
            disposition = SpaceExcelCadMatchDisposition.Conflict;
            differences.Add("FloorCode");
        }
        else if (cad.Length == 1 && editors.Length == 1
                 && cad[0].Item.Source.SourceRef is { } cadSource
                 && editors[0].SourceRef is { } editorSource
                 && !cadSource.Equals(editorSource, StringComparison.Ordinal))
        {
            disposition = SpaceExcelCadMatchDisposition.Conflict;
            differences.Add("SourceRef");
        }
        else if (editors.Length == 1)
        {
            differences.AddRange(Differences(values, editors[0]));
            disposition = differences.Count == 0
                ? SpaceExcelCadMatchDisposition.Unchanged
                : SpaceExcelCadMatchDisposition.Update;
        }
        else
        {
            disposition = SpaceExcelCadMatchDisposition.New;
        }
        return new MatchDraft(
            rowId,
            row,
            values,
            disposition,
            cad,
            editors,
            keyEvidence,
            differences,
            []);
    }

    private static void ApplyDuplicateTargetConflicts(IReadOnlyList<MatchDraft> drafts)
    {
        var claims = drafts
            .Where(item => item.Disposition is not SpaceExcelCadMatchDisposition.Error
                and not SpaceExcelCadMatchDisposition.Unmatched)
            .SelectMany(item => item.CadCandidates.Select(candidate => new
            {
                Target = $"CAD:{candidate.Item.PreviewObjectId}",
                Draft = item,
            }).Concat(item.EditorCandidates.Select(candidate => new
            {
                Target = $"EDITOR:{candidate.LogicalId:N}",
                Draft = item,
            })))
            .GroupBy(item => item.Target, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Draft.ExcelRowId)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any());
        foreach (var group in claims)
        {
            foreach (var draft in group.Select(item => item.Draft).Distinct())
            {
                draft.Disposition = SpaceExcelCadMatchDisposition.Conflict;
                if (!draft.DifferenceFields.Contains(
                        "TargetClaimedByMultipleExcelRows",
                        StringComparer.Ordinal))
                {
                    draft.DifferenceFields.Add("TargetClaimedByMultipleExcelRows");
                }
            }
        }
    }

    private static SpaceExcelCadRackMatchV1 ToMatch(MatchDraft draft)
    {
        var cad = draft.CadCandidates.Length == 1 ? draft.CadCandidates[0] : null;
        var editor = draft.EditorCandidates.Length == 1 ? draft.EditorCandidates[0] : null;
        var sourceRef = cad?.Item.Source.SourceRef ?? editor?.SourceRef;
        var location = cad?.Evidence.Location;
        var withoutHash = new SpaceExcelCadRackMatchV1(
            draft.ExcelRowId,
            draft.Row.SourceSheet,
            draft.Row.RowNumber,
            draft.Values,
            draft.Disposition,
            cad?.Item.PreviewObjectId,
            editor?.LogicalId,
            sourceRef,
            cad?.Item.Confidence,
            cad?.Evidence.ConfidenceBand,
            draft.KeyEvidence,
            draft.DifferenceFields
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            draft.ErrorCodes
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            location,
            MatchEvidenceSha256: string.Empty);
        return withoutHash with
        {
            MatchEvidenceSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
    }

    private static SpaceExcelRackValuesV1 Values(SpaceExcelCanonicalRow row) => new(
        Value(row, "FloorCode"),
        Value(row, "ZoneCode"),
        Value(row, "RackCode"),
        Decimal(row, "XMm"),
        Decimal(row, "YMm"),
        Decimal(row, "ZMm"),
        Decimal(row, "WidthMm"),
        Decimal(row, "DepthMm"),
        Decimal(row, "HeightMm"),
        Decimal(row, "RotationZDeg"),
        Value(row, "RackTemplateCode"),
        Value(row, "LifecycleStatus"));

    private static IReadOnlyList<string> Differences(
        SpaceExcelRackValuesV1 values,
        SpaceExcelEditorRackSnapshotV1 editor)
    {
        var result = new List<string>();
        AddDifference(result, "FloorCode", values.FloorCode, editor.FloorCode);
        AddDifference(result, "ZoneCode", values.ZoneCode, editor.ZoneCode);
        AddDifference(result, "RackCode", values.RackCode, editor.RackCode);
        AddDifference(result, "XMm", values.XMillimeters, editor.XMillimeters);
        AddDifference(result, "YMm", values.YMillimeters, editor.YMillimeters);
        AddDifference(result, "ZMm", values.ZMillimeters, editor.ZMillimeters);
        AddDifference(result, "WidthMm", values.WidthMillimeters, editor.WidthMillimeters);
        AddDifference(result, "DepthMm", values.DepthMillimeters, editor.DepthMillimeters);
        AddDifference(result, "HeightMm", values.HeightMillimeters, editor.HeightMillimeters);
        AddDifference(
            result,
            "RotationZDeg",
            values.RotationZDegrees,
            editor.RotationZDegrees);
        AddDifference(
            result,
            "LifecycleStatus",
            values.LifecycleStatus,
            editor.LifecycleState);
        return result;
    }

    private static void AddDifference(
        ICollection<string> differences,
        string field,
        string? excel,
        string editor)
    {
        if (excel is not null && !excel.Equals(editor, StringComparison.OrdinalIgnoreCase))
            differences.Add(field);
    }

    private static void AddDifference(
        ICollection<string> differences,
        string field,
        decimal? excel,
        decimal editor)
    {
        if (excel.HasValue && excel.Value != editor)
            differences.Add(field);
    }

    private static void ValidateRow(SpaceExcelCadRackMatchV1 row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(row.Values);
        ArgumentNullException.ThrowIfNull(row.KeyEvidence);
        ArgumentNullException.ThrowIfNull(row.DifferenceFields);
        ArgumentNullException.ThrowIfNull(row.ErrorCodes);
        if (!IsToken(row.ExcelRowId) || !IsToken(row.SourceSheet)
            || row.RowNumber <= 0 || !Enum.IsDefined(row.Disposition)
            || row.CadPreviewObjectId is not null && !IsToken(row.CadPreviewObjectId)
            || row.EditorLogicalId == Guid.Empty
            || row.MatchedSourceRef is not null && !IsSourceRef(row.MatchedSourceRef)
            || row.CadConfidence is < 0 or > 1
            || row.CadConfidenceBand is { } band && !Enum.IsDefined(band)
            || !IsSha256(row.MatchEvidenceSha256))
        {
            throw new InvalidDataException("Excel/CAD match row is invalid.");
        }
        foreach (var evidence in row.KeyEvidence)
        {
            if (!Enum.IsDefined(evidence.Kind)
                || !IsToken(evidence.Value)
                || !IsToken(evidence.CandidateId))
            {
                throw new InvalidDataException("Excel/CAD match key evidence is invalid.");
            }
        }
        if (!row.KeyEvidence.SequenceEqual(row.KeyEvidence
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.CandidateId, StringComparer.Ordinal))
            || row.DifferenceFields.Any(item => !IsToken(item))
            || row.ErrorCodes.Any(item => !IsToken(item))
            || !row.DifferenceFields.SequenceEqual(row.DifferenceFields
                .OrderBy(item => item, StringComparer.Ordinal))
            || !row.ErrorCodes.SequenceEqual(row.ErrorCodes
                .OrderBy(item => item, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Excel/CAD match row evidence is not canonical.");
        }

        var error = row.Disposition == SpaceExcelCadMatchDisposition.Error;
        if (error != (row.ErrorCodes.Count > 0)
            || row.Disposition == SpaceExcelCadMatchDisposition.Unmatched
            && (row.CadPreviewObjectId is not null
                || row.EditorLogicalId is not null
                || row.KeyEvidence.Count > 0)
            || row.Disposition == SpaceExcelCadMatchDisposition.New
            && row.CadPreviewObjectId is null
            || row.Disposition is SpaceExcelCadMatchDisposition.Update
                or SpaceExcelCadMatchDisposition.Unchanged
            && row.EditorLogicalId is null
            || row.Disposition == SpaceExcelCadMatchDisposition.Unchanged
            && row.DifferenceFields.Count > 0)
        {
            throw new InvalidDataException("Excel/CAD match disposition is inconsistent.");
        }
        if (!error && (row.Values.FloorCode is null
                       || row.Values.ZoneCode is null
                       || row.Values.RackCode is null
                       || row.Values.XMillimeters is null
                       || row.Values.YMillimeters is null
                       || row.Values.WidthMillimeters is null
                       || row.Values.DepthMillimeters is null
                       || row.Values.HeightMillimeters is null
                       || row.Values.LifecycleStatus is null))
        {
            throw new InvalidDataException("Excel/CAD match row lacks canonical rack values.");
        }
        if (row.Location is not null && row.Location.FloorLogicalId == Guid.Empty)
            throw new InvalidDataException("Excel/CAD match location is invalid.");
        var expected = ComputeSha256(CanonicalJson(
            row with { MatchEvidenceSha256 = string.Empty }));
        if (!row.MatchEvidenceSha256.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("Excel/CAD match row hash is invalid.");
    }

    private static SpaceExcelCadMatchSummaryV1 Summary(
        IReadOnlyList<SpaceExcelCadRackMatchV1> rows) => new(
        rows.Count,
        rows.LongCount(item => item.Disposition == SpaceExcelCadMatchDisposition.New),
        rows.LongCount(item => item.Disposition == SpaceExcelCadMatchDisposition.Update),
        rows.LongCount(item => item.Disposition == SpaceExcelCadMatchDisposition.Unchanged),
        rows.LongCount(item => item.Disposition == SpaceExcelCadMatchDisposition.Unmatched),
        rows.LongCount(item => item.Disposition == SpaceExcelCadMatchDisposition.Conflict),
        rows.LongCount(item => item.Disposition == SpaceExcelCadMatchDisposition.Error),
        rows.LongCount(item => item.Location?.CanFocusCanvas == true));

    private static SpaceExcelCanonicalRow[] CanonicalRows(
        IReadOnlyList<SpaceExcelCanonicalRow> rows) => rows
        .OrderBy(item => item.TargetSheet, StringComparer.Ordinal)
        .ThenBy(item => item.SourceSheet, StringComparer.Ordinal)
        .ThenBy(item => item.RowNumber)
        .Select(item => item with
        {
            Values = new SortedDictionary<string, string?>(
                item.Values.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal),
            Columns = new SortedDictionary<string, string?>(
                item.Columns.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal),
        })
        .ToArray();

    private static object Projection(SpaceExcelCanonicalRow row) => new
    {
        row.TargetSheet,
        row.SourceSheet,
        row.RowNumber,
        row.Values,
        row.Columns,
    };

    private static SpaceExcelPreflightFinding[] CanonicalFindings(
        IReadOnlyList<SpaceExcelPreflightFinding> findings) => findings
        .OrderByDescending(item => item.Severity)
        .ThenBy(item => item.Code, StringComparer.Ordinal)
        .ThenBy(item => item.Sheet, StringComparer.Ordinal)
        .ThenBy(item => item.Row)
        .ThenBy(item => item.Column, StringComparer.Ordinal)
        .ThenBy(item => item.TargetField, StringComparer.Ordinal)
        .ThenBy(item => item.SuggestedActionCode, StringComparer.Ordinal)
        .ToArray();

    private static string? Value(SpaceExcelCanonicalRow row, string field) =>
        row.Values.GetValueOrDefault(field);

    private static decimal? Decimal(SpaceExcelCanonicalRow row, string field) =>
        decimal.TryParse(
            Value(row, field),
            NumberStyles.Number | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static bool IsToken(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= SpaceCadConversionContract.MaximumIdentifierLength
        && value.Equals(value.Trim(), StringComparison.Ordinal);

    private static bool IsSourceRef(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= SpaceCadConversionContract.MaximumSourceReferenceLength
        && value.Equals(value.Trim(), StringComparison.Ordinal);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record CadCandidate(
        SpaceCadSemanticPreviewItemV1 Item,
        SpaceCadSemanticEvidenceV1 Evidence,
        IReadOnlyList<string> BusinessKeys);

    private sealed class MatchDraft(
        string excelRowId,
        SpaceExcelCanonicalRow row,
        SpaceExcelRackValuesV1 values,
        SpaceExcelCadMatchDisposition disposition,
        CadCandidate[] cadCandidates,
        SpaceExcelEditorRackSnapshotV1[] editorCandidates,
        IReadOnlyList<SpaceExcelCadMatchKeyEvidenceV1> keyEvidence,
        IReadOnlyList<string> differenceFields,
        IReadOnlyList<string> errorCodes)
    {
        public string ExcelRowId { get; } = excelRowId;
        public SpaceExcelCanonicalRow Row { get; } = row;
        public SpaceExcelRackValuesV1 Values { get; } = values;
        public SpaceExcelCadMatchDisposition Disposition { get; set; } = disposition;
        public CadCandidate[] CadCandidates { get; } = cadCandidates;
        public SpaceExcelEditorRackSnapshotV1[] EditorCandidates { get; } = editorCandidates;
        public IReadOnlyList<SpaceExcelCadMatchKeyEvidenceV1> KeyEvidence { get; } = keyEvidence;
        public List<string> DifferenceFields { get; } = [.. differenceFields];
        public IReadOnlyList<string> ErrorCodes { get; } = errorCodes;
    }
}
