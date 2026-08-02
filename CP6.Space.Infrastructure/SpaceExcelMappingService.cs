using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceExcelMappingService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock) : ISpaceExcelMappingService
{
    public static readonly Guid SystemStandardProfileId =
        Guid.Parse("00000000-0000-0000-0000-000000030001");

    private const string Operation = "space.excel-mapping-profile.save";
    private const int MaximumWorkbookSheets = 20;
    private const int MaximumHeadersPerSheet = 512;
    private const int MaximumDefinitionBytes = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly SpaceExcelMappingDefinitionDto SystemDefinition =
        CreateSystemDefinition();

    private static readonly string SystemDefinitionJson =
        JsonSerializer.Serialize(SystemDefinition, JsonOptions);

    private static readonly string SystemDefinitionHash =
        Hash(SystemDefinitionJson);

    public async Task<IReadOnlyList<SpaceExcelMappingProfileDto>>
        GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var profiles = await context.ExcelMappingProfiles
            .AsNoTracking()
            .OrderBy(item => item.NormalizedName)
            .ToArrayAsync(cancellationToken);
        var profileIds = profiles.Select(item => item.Id).ToArray();
        SpaceExcelMappingProfileVersion[] versions = profileIds.Length == 0
            ? []
            : await context.ExcelMappingProfileVersions
                .AsNoTracking()
                .Where(item => profileIds.Contains(item.ProfileId))
                .ToArrayAsync(cancellationToken);
        var byKey = versions.ToDictionary(
            item => (item.ProfileId, item.Version));

        return
        [
            SystemProfile(),
            .. profiles.Select(profile => ToDto(
                profile,
                byKey[(profile.Id, profile.CurrentVersion)])),
        ];
    }

    public async Task<SpaceExcelMappingProfileDto> GetProfileAsync(
        Guid profileId,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        RequireTenant();
        if (profileId == SystemStandardProfileId)
        {
            if (version is not null and not 1)
                throw NotFound();
            return SystemProfile();
        }
        if (profileId == Guid.Empty || version <= 0)
            throw NotFound();

        var profile = await context.ExcelMappingProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == profileId,
                cancellationToken)
            ?? throw NotFound();
        var selectedVersion = version ?? profile.CurrentVersion;
        var item = await context.ExcelMappingProfileVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ProfileId == profile.Id &&
                    candidate.Version == selectedVersion,
                cancellationToken)
            ?? throw NotFound();
        return ToDto(profile, item);
    }

    public SpaceExcelMappingPreviewDto Preview(
        PreviewSpaceExcelMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTenant();
        var definition = NormalizeDefinition(request.Definition, strict: false);
        var workbook = NormalizeWorkbook(request.Workbook);
        var issues = new List<SpaceExcelMappingIssueDto>();
        var previews = new List<SpaceExcelSheetPreviewDto>();

        foreach (var targetSheet in SpaceExcelTargetCatalog.Sheets)
        {
            var mapping = definition.Sheets.SingleOrDefault(item =>
                string.Equals(
                    item.TargetSheet,
                    targetSheet,
                    StringComparison.Ordinal));
            if (mapping is null)
            {
                issues.Add(Issue(
                    "SPACE_EXCEL_TARGET_SHEET_UNMAPPED",
                    "Error",
                    targetSheet,
                    null,
                    $"Target sheet '{targetSheet}' is not mapped.",
                    "Add a sheet mapping for every standard target sheet."));
                previews.Add(new SpaceExcelSheetPreviewDto(
                    targetSheet,
                    string.Empty,
                    null,
                    "Unmapped",
                    [],
                    []));
                continue;
            }

            var matches = workbook
                .Where(sample => SheetMatches(
                    sample.SheetName,
                    mapping.SourceSheet,
                    mapping.SheetMatchMode))
                .ToArray();
            if (matches.Length != 1)
            {
                var ambiguous = matches.Length > 1;
                issues.Add(Issue(
                    ambiguous
                        ? "SPACE_EXCEL_SOURCE_SHEET_AMBIGUOUS"
                        : "SPACE_EXCEL_SOURCE_SHEET_MISSING",
                    "Error",
                    mapping.SourceSheet,
                    null,
                    ambiguous
                        ? $"Source sheet pattern '{mapping.SourceSheet}' matched multiple sheets."
                        : $"Source sheet pattern '{mapping.SourceSheet}' matched no sheet.",
                    ambiguous
                        ? "Use an exact sheet name or a narrower wildcard."
                        : "Correct the source sheet name or include it in the workbook sample."));
                previews.Add(new SpaceExcelSheetPreviewDto(
                    targetSheet,
                    mapping.SourceSheet,
                    null,
                    ambiguous ? "Ambiguous" : "Missing",
                    CreateUnresolvedColumns(mapping),
                    []));
                continue;
            }

            var sample = matches[0];
            var headerLookup = sample.Headers
                .Select((header, index) => new HeaderPosition(header, index + 1))
                .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var consumed = new HashSet<int>();
            var columnPreviews = new List<SpaceExcelColumnPreviewDto>();
            foreach (var field in SpaceExcelTargetCatalog.ForSheet(targetSheet))
            {
                var column = mapping.Columns.SingleOrDefault(item =>
                    string.Equals(
                        item.TargetField,
                        field.Field,
                        StringComparison.Ordinal));
                if (column is null)
                {
                    var status = field.Required ? "MissingRequired" : "UnmappedOptional";
                    if (field.Required)
                    {
                        issues.Add(Issue(
                            "SPACE_EXCEL_REQUIRED_FIELD_UNMAPPED",
                            "Error",
                            sample.SheetName,
                            field.Field,
                            $"Required target field '{targetSheet}.{field.Field}' is not mapped.",
                            "Map a source header/column or supply an allowed default value."));
                    }
                    columnPreviews.Add(new SpaceExcelColumnPreviewDto(
                        field.Field,
                        field.Required,
                        null,
                        null,
                        null,
                        status));
                    continue;
                }

                var resolved = ResolveColumn(column, sample.Headers, headerLookup);
                if (resolved.Status == "Mapped" && resolved.Index.HasValue)
                    consumed.Add(resolved.Index.Value);
                if (resolved.Status is "HeaderMissing" or "HeaderDuplicate" or "ColumnMismatch")
                {
                    issues.Add(Issue(
                        resolved.Status switch
                        {
                            "HeaderDuplicate" => "SPACE_EXCEL_SOURCE_HEADER_DUPLICATE",
                            "ColumnMismatch" => "SPACE_EXCEL_SOURCE_COLUMN_MISMATCH",
                            _ => "SPACE_EXCEL_SOURCE_HEADER_MISSING",
                        },
                        field.Required ? "Error" : "Warning",
                        sample.SheetName,
                        column.SourceHeader ?? column.SourceColumn,
                        $"Source for target field '{targetSheet}.{field.Field}' could not be resolved uniquely.",
                        "Correct the source header/column selector or the workbook header row."));
                }
                columnPreviews.Add(new SpaceExcelColumnPreviewDto(
                    field.Field,
                    field.Required,
                    column.SourceHeader,
                    column.SourceColumn,
                    resolved.Index,
                    resolved.Status));
            }

            var unknown = sample.Headers
                .Select((header, index) => new { header, index = index + 1 })
                .Where(item => !consumed.Contains(item.index))
                .Select(item => item.header)
                .ToArray();
            if (unknown.Length > 0 &&
                definition.UnknownColumnPolicy != "Ignore")
            {
                var severity = definition.UnknownColumnPolicy == "Reject"
                    ? "Error"
                    : "Warning";
                foreach (var header in unknown)
                {
                    issues.Add(Issue(
                        "SPACE_EXCEL_UNKNOWN_COLUMN",
                        severity,
                        sample.SheetName,
                        header,
                        $"Source column '{header}' is not mapped.",
                        severity == "Error"
                            ? "Map the column or change UnknownColumnPolicy."
                            : "Confirm that the column can be ignored."));
                }
            }
            previews.Add(new SpaceExcelSheetPreviewDto(
                targetSheet,
                mapping.SourceSheet,
                sample.SheetName,
                "Matched",
                columnPreviews,
                unknown));
        }

        return new SpaceExcelMappingPreviewDto(
            !issues.Any(issue => issue.Severity == "Error"),
            definition,
            previews,
            issues);
    }

    public async Task<SaveSpaceExcelMappingProfileResponse> SaveProfileAsync(
        SaveSpaceExcelMappingProfileRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = RequireTenant();
        var actorId = RequireActor();
        if (request.ProfileId == SystemStandardProfileId)
            throw ReadOnly();
        var definition = NormalizeDefinition(request.Definition, strict: true);
        var definitionJson = JsonSerializer.Serialize(definition, JsonOptions);
        if (Encoding.UTF8.GetByteCount(definitionJson) > MaximumDefinitionBytes)
            throw Invalid("The normalized mapping definition is too large.");
        var definitionHash = Hash(definitionJson);
        var name = RequireText(request.Name, 200, "profile name");
        var normalizedRequest = request with
        {
            Name = name,
            Definition = definition,
        };
        var requestHash = Hash(JsonSerializer.Serialize(
            normalizedRequest,
            JsonOptions));
        var keyHash = IdempotencyKeyHash(idempotencyKey);
        var replay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            SpaceExcelMappingProfile profile;
            int version;
            Guid? basedOnProfileId;
            int? basedOnVersion;
            var created = !request.ProfileId.HasValue;
            if (created)
            {
                await ValidateCopySourceAsync(
                    request.CopyFromProfileId,
                    request.CopyFromVersion,
                    cancellationToken);
                profile = SpaceExcelMappingProfile.Create(tenantId, name);
                version = 1;
                basedOnProfileId = request.CopyFromProfileId;
                basedOnVersion = request.CopyFromVersion;
                context.ExcelMappingProfiles.Add(profile);
            }
            else
            {
                if (request.CopyFromProfileId.HasValue || request.CopyFromVersion.HasValue)
                    throw Invalid("An existing profile version is based on its current version.");
                profile = await context.ExcelMappingProfiles.SingleOrDefaultAsync(
                    item => item.Id == request.ProfileId,
                    cancellationToken) ?? throw NotFound();
                ApplyExpectedRowVersion(profile, request.ExpectedRowVersion);
                version = checked(profile.CurrentVersion + 1);
                basedOnProfileId = profile.Id;
                basedOnVersion = profile.CurrentVersion;
            }

            profile.Advance(name, version);
            var versionEntity = SpaceExcelMappingProfileVersion.Create(
                tenantId,
                profile.Id,
                version,
                definitionJson,
                definitionHash,
                basedOnProfileId,
                basedOnVersion);
            context.ExcelMappingProfileVersions.Add(versionEntity);
            await context.SaveChangesAsync(cancellationToken);

            var response = new SaveSpaceExcelMappingProfileResponse(
                ToDto(profile, versionEntity),
                created,
                IdempotentReplay: false);
            var now = RequireUtcNow();
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                tenantId,
                actorId,
                Operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                200,
                now.AddHours(24),
                now.AddDays(90)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException exception)
            when (exception.GetBaseException() is SqlException
                  {
                      Number: 2601 or 2627,
                  })
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict();
        }

    }

    private async Task ValidateCopySourceAsync(
        Guid? profileId,
        int? version,
        CancellationToken cancellationToken)
    {
        if (profileId.HasValue != version.HasValue ||
            version.HasValue && version.Value <= 0)
            throw Invalid("Copy source profile identity and version must be supplied together.");
        if (!profileId.HasValue)
            return;
        if (profileId == SystemStandardProfileId)
        {
            if (version != 1)
                throw NotFound();
            return;
        }
        var exists = await context.ExcelMappingProfileVersions
            .AsNoTracking()
            .AnyAsync(
                item => item.ProfileId == profileId && item.Version == version,
                cancellationToken);
        if (!exists)
            throw NotFound();
    }

    private async Task<SaveSpaceExcelMappingProfileResponse?> ReadReplayAsync(
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var actorId = RequireActor();
        var record = await context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == actorId &&
                    item.Operation == Operation &&
                    item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }
        return (JsonSerializer.Deserialize<SaveSpaceExcelMappingProfileResponse>(
                    record.ResponseJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "The Excel mapping idempotency response is invalid."))
            with
            {
                IdempotentReplay = true,
            };
    }

    private static SpaceExcelMappingDefinitionDto NormalizeDefinition(
        SpaceExcelMappingDefinitionDto? definition,
        bool strict)
    {
        if (definition is null ||
            definition.SchemaVersion != SpaceExcelTargetCatalog.MappingSchemaVersion)
        {
            throw Invalid("Unsupported mapping definition schema version.");
        }
        var unknownPolicy = ParseChoice(
            definition.UnknownColumnPolicy,
            "unknown column policy",
            "Ignore", "Warning", "Reject");
        var emptyPolicy = ParseChoice(
            definition.EmptyValuePolicy,
            "empty value policy",
            "Reject", "UseDefault", "KeepEmpty");
        var duplicatePolicy = ParseChoice(
            definition.DuplicateRowPolicy,
            "duplicate row policy",
            "Reject", "KeepFirst", "KeepLast");
        var sheets = definition.Sheets?.ToArray() ?? [];
        if (sheets.Length is < 1 or > 5)
            throw Invalid("A mapping definition must contain 1 to 5 sheet mappings.");

        var normalizedSheets = sheets.Select(NormalizeSheet).ToArray();
        if (normalizedSheets.Select(item => item.TargetSheet)
                .Distinct(StringComparer.Ordinal).Count() != normalizedSheets.Length)
        {
            throw Invalid("Target sheet mappings must be unique.");
        }
        if (strict)
        {
            var targetSheets = normalizedSheets
                .Select(item => item.TargetSheet)
                .ToHashSet(StringComparer.Ordinal);
            if (!targetSheets.SetEquals(SpaceExcelTargetCatalog.Sheets))
                throw Invalid("Every standard target sheet must be mapped before saving.");
            foreach (var sheet in normalizedSheets)
            {
                var mapped = sheet.Columns.Select(item => item.TargetField)
                    .ToHashSet(StringComparer.Ordinal);
                var missing = SpaceExcelTargetCatalog.ForSheet(sheet.TargetSheet)
                    .Where(field => field.Required && !mapped.Contains(field.Field))
                    .Select(field => field.Field)
                    .ToArray();
                if (missing.Length > 0)
                {
                    throw Invalid(
                        $"Sheet '{sheet.TargetSheet}' is missing required target fields: " +
                        string.Join(", ", missing));
                }
            }
        }
        return new SpaceExcelMappingDefinitionDto(
            SpaceExcelTargetCatalog.MappingSchemaVersion,
            unknownPolicy,
            emptyPolicy,
            duplicatePolicy,
            normalizedSheets.OrderBy(
                item => Array.IndexOf(
                    SpaceExcelTargetCatalog.Sheets.ToArray(),
                    item.TargetSheet)).ToArray());
    }

    private static SpaceExcelSheetMappingDto NormalizeSheet(
        SpaceExcelSheetMappingDto sheet)
    {
        if (sheet is null)
            throw Invalid("Sheet mappings cannot be null.");
        var targetSheet = SpaceExcelTargetCatalog.Sheets.SingleOrDefault(item =>
            string.Equals(item, sheet.TargetSheet?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            ?? throw Invalid("Unsupported target sheet.");
        var sourceSheet = RequireText(sheet.SourceSheet, 100, "source sheet");
        var matchMode = ParseChoice(
            sheet.SheetMatchMode,
            "sheet match mode",
            "Exact", "Wildcard");
        if (matchMode == "Wildcard" &&
            sourceSheet.Any(character => character is '[' or ']' or '\\'))
        {
            throw Invalid("Wildcard sheet matching only supports '*' and '?'.");
        }
        if (sheet.HeaderRow is < 1 or > 1000 ||
            sheet.DataStartRow <= sheet.HeaderRow ||
            sheet.DataStartRow > 1_048_576)
        {
            throw Invalid("HeaderRow/DataStartRow are outside the Excel row range.");
        }
        var columns = sheet.Columns?.ToArray() ?? [];
        if (columns.Length > 100)
            throw Invalid("A sheet mapping cannot contain more than 100 columns.");
        var normalizedColumns = columns.Select(column =>
            NormalizeColumn(targetSheet, column)).ToArray();
        if (normalizedColumns.Select(item => item.TargetField)
                .Distinct(StringComparer.Ordinal).Count() != normalizedColumns.Length)
        {
            throw Invalid($"Target fields in sheet '{targetSheet}' must be unique.");
        }
        var sourceSelectors = normalizedColumns
            .Where(item => item.SourceHeader is not null || item.SourceColumn is not null)
            .Select(item => $"{item.SourceHeader?.ToUpperInvariant()}|{item.SourceColumn}")
            .ToArray();
        if (sourceSelectors.Distinct(StringComparer.Ordinal).Count() !=
            sourceSelectors.Length)
        {
            throw Invalid($"Source selectors in sheet '{targetSheet}' must be unique.");
        }
        return new SpaceExcelSheetMappingDto(
            targetSheet,
            sourceSheet,
            matchMode,
            sheet.HeaderRow,
            sheet.DataStartRow,
            normalizedColumns.OrderBy(item =>
                SpaceExcelTargetCatalog.ForSheet(targetSheet)
                    .Select(field => field.Field)
                    .ToList()
                    .IndexOf(item.TargetField)).ToArray());
    }

    private static SpaceExcelColumnMappingDto NormalizeColumn(
        string targetSheet,
        SpaceExcelColumnMappingDto column)
    {
        if (column is null)
            throw Invalid("Column mappings cannot be null.");
        var target = SpaceExcelTargetCatalog.Find(targetSheet, column.TargetField)
            ?? throw Invalid($"Unsupported target field in '{targetSheet}'.");
        var sourceHeader = OptionalText(column.SourceHeader, 200, "source header");
        var sourceColumn = OptionalText(column.SourceColumn, 3, "source column")
            ?.ToUpperInvariant();
        if (sourceColumn is not null && !TryColumnIndex(sourceColumn, out _))
            throw Invalid("SourceColumn must be an Excel column from A through XFD.");
        var defaultValue = OptionalText(column.DefaultValue, 1000, "default value");
        if (sourceHeader is null && sourceColumn is null && defaultValue is null)
            throw Invalid($"Target field '{targetSheet}.{target.Field}' has no source or default.");
        var dataType = ParseChoice(
            column.DataType,
            "column data type",
            "Text", "Integer", "Decimal");
        if (dataType != target.DataType)
            throw Invalid($"Target field '{targetSheet}.{target.Field}' requires {target.DataType}.");
        if (column.IsBusinessKey != target.IsBusinessKey)
            throw Invalid($"Business-key semantics for '{targetSheet}.{target.Field}' are fixed.");
        var reference = OptionalText(column.ReferenceTarget, 100, "reference target")
            ?? target.ReferenceTarget;
        if (!string.Equals(reference, target.ReferenceTarget, StringComparison.Ordinal))
            throw Invalid($"Reference semantics for '{targetSheet}.{target.Field}' are fixed.");
        var conversions = NormalizeEnumConversions(column.EnumConversions);
        if (column.UnitConversionMultiplier is <= 0 or > 1_000_000)
            throw Invalid("Unit conversion multiplier must be greater than zero and bounded.");
        return new SpaceExcelColumnMappingDto(
            target.Field,
            sourceHeader,
            sourceColumn,
            dataType,
            OptionalText(column.Format, 100, "format"),
            defaultValue,
            target.IsBusinessKey,
            target.ReferenceTarget,
            conversions,
            column.UnitConversionMultiplier);
    }

    private static IReadOnlyList<SpaceExcelEnumConversionDto> NormalizeEnumConversions(
        IReadOnlyList<SpaceExcelEnumConversionDto>? conversions)
    {
        var source = conversions?.ToArray() ?? [];
        if (source.Length > 100)
            throw Invalid("A column cannot contain more than 100 enum conversions.");
        var normalized = source.Select(item =>
        {
            if (item is null)
                throw Invalid("Enum conversions cannot be null.");
            return new SpaceExcelEnumConversionDto(
                RequireText(item.SourceValue, 200, "enum source value"),
                RequireText(item.TargetValue, 200, "enum target value"));
        }).OrderBy(item => item.SourceValue, StringComparer.Ordinal).ToArray();
        if (normalized.Select(item => item.SourceValue.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw Invalid("Enum conversion source values must be unique.");
        }
        return normalized;
    }

    private static SpaceExcelHeaderSampleDto[] NormalizeWorkbook(
        IReadOnlyList<SpaceExcelHeaderSampleDto>? workbook)
    {
        var samples = workbook?.ToArray() ?? [];
        if (samples.Length is < 1 or > MaximumWorkbookSheets)
            throw Invalid($"Workbook preview must contain 1 to {MaximumWorkbookSheets} sheets.");
        var normalized = samples.Select(sample =>
        {
            if (sample is null)
                throw Invalid("Workbook sheet samples cannot be null.");
            var name = RequireText(sample.SheetName, 100, "workbook sheet name");
            var headers = sample.Headers?.ToArray() ?? [];
            if (headers.Length is < 1 or > MaximumHeadersPerSheet)
                throw Invalid($"A workbook sheet must contain 1 to {MaximumHeadersPerSheet} headers.");
            return new SpaceExcelHeaderSampleDto(
                name,
                headers.Select(header =>
                    RequireText(header, 200, "workbook header")).ToArray());
        }).ToArray();
        if (normalized.Select(item => item.SheetName.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw Invalid("Workbook sample sheet names must be unique.");
        }
        return normalized;
    }

    private static ResolvedColumn ResolveColumn(
        SpaceExcelColumnMappingDto column,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, HeaderPosition[]> headerLookup)
    {
        int? byHeader = null;
        if (column.SourceHeader is not null)
        {
            if (!headerLookup.TryGetValue(column.SourceHeader, out var matches))
                return new ResolvedColumn(null, "HeaderMissing");
            if (matches.Length != 1)
                return new ResolvedColumn(null, "HeaderDuplicate");
            byHeader = matches[0].Index;
        }
        int? byColumn = null;
        if (column.SourceColumn is not null)
        {
            TryColumnIndex(column.SourceColumn, out var index);
            if (index > headers.Count)
                return new ResolvedColumn(null, "HeaderMissing");
            byColumn = index;
        }
        if (byHeader.HasValue && byColumn.HasValue && byHeader != byColumn)
            return new ResolvedColumn(null, "ColumnMismatch");
        return new ResolvedColumn(byHeader ?? byColumn, "Mapped");
    }

    private static IReadOnlyList<SpaceExcelColumnPreviewDto>
        CreateUnresolvedColumns(SpaceExcelSheetMappingDto mapping) =>
        SpaceExcelTargetCatalog.ForSheet(mapping.TargetSheet)
            .Select(field =>
            {
                var column = mapping.Columns.SingleOrDefault(item =>
                    item.TargetField == field.Field);
                return new SpaceExcelColumnPreviewDto(
                    field.Field,
                    field.Required,
                    column?.SourceHeader,
                    column?.SourceColumn,
                    null,
                    "SheetUnresolved");
            }).ToArray();

    private static bool SheetMatches(
        string value,
        string pattern,
        string mode) =>
        mode == "Exact"
            ? string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase)
            : WildcardMatches(value, pattern);

    private static bool WildcardMatches(string value, string pattern)
    {
        var textIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var matchIndex = 0;
        while (textIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 char.ToUpperInvariant(pattern[patternIndex]) ==
                 char.ToUpperInvariant(value[textIndex])))
            {
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                matchIndex = textIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++matchIndex;
            }
            else
            {
                return false;
            }
        }
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            patternIndex++;
        return patternIndex == pattern.Length;
    }

    private static bool TryColumnIndex(string column, out int index)
    {
        index = 0;
        if (column.Length is < 1 or > 3)
            return false;
        foreach (var character in column)
        {
            if (character is < 'A' or > 'Z')
                return false;
            index = checked(index * 26 + character - 'A' + 1);
        }
        return index <= 16_384;
    }

    private static SpaceExcelMappingDefinitionDto CreateSystemDefinition() =>
        new(
            SpaceExcelTargetCatalog.MappingSchemaVersion,
            "Warning",
            "Reject",
            "Reject",
            SpaceExcelTargetCatalog.Sheets.Select(sheet =>
                new SpaceExcelSheetMappingDto(
                    sheet,
                    sheet,
                    "Exact",
                    1,
                    2,
                    SpaceExcelTargetCatalog.ForSheet(sheet).Select(field =>
                        new SpaceExcelColumnMappingDto(
                            field.Field,
                            field.Field,
                            null,
                            field.DataType,
                            null,
                            null,
                            field.IsBusinessKey,
                            field.ReferenceTarget,
                            [],
                            null)).ToArray())).ToArray());

    private static SpaceExcelMappingProfileDto SystemProfile() =>
        new(
            SystemStandardProfileId,
            "CP6 Standard Excel v1",
            "System",
            1,
            IsReadOnly: true,
            SystemDefinitionHash,
            SystemDefinition,
            null,
            null,
            null,
            null,
            null);

    private static SpaceExcelMappingProfileDto ToDto(
        SpaceExcelMappingProfile profile,
        SpaceExcelMappingProfileVersion version) =>
        new(
            profile.Id,
            profile.Name,
            "Tenant",
            version.Version,
            IsReadOnly: false,
            version.DefinitionHash,
            JsonSerializer.Deserialize<SpaceExcelMappingDefinitionDto>(
                version.DefinitionJson,
                JsonOptions) ?? throw new InvalidOperationException(
                    "Stored Excel mapping definition is invalid."),
            version.BasedOnProfileId,
            version.BasedOnVersion,
            Convert.ToBase64String(profile.RowVersion),
            version.CreatedAtUtc,
            version.CreatedBy);

    private void ApplyExpectedRowVersion(
        SpaceExcelMappingProfile profile,
        string? expected)
    {
        if (expected is null)
            throw Conflict("ExpectedRowVersion is required when adding a version.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(expected);
        }
        catch (FormatException)
        {
            throw Conflict("ExpectedRowVersion is invalid.");
        }
        if (!profile.RowVersion.SequenceEqual(bytes))
            throw Conflict();
        context.Entry(profile).Property(item => item.RowVersion).OriginalValue = bytes;
    }

    private string IdempotencyKeyHash(string idempotencyKey)
    {
        var normalized = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            Encoding.UTF8.GetByteCount(normalized) > 128 ||
            normalized.Any(char.IsControl))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                recoveryAction: "supply-idempotency-key");
        }
        return Hash($"{execution.TenantId:D}\n{Operation}\n{normalized}");
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return null;
        return await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
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

    private Guid RequireActor()
    {
        if (execution.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space actor is required.");
        }
        return execution.ActorId;
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static string ParseChoice(
        string? value,
        string label,
        params string[] choices)
    {
        var input = value?.Trim();
        var match = choices.SingleOrDefault(choice =>
            string.Equals(choice, input, StringComparison.OrdinalIgnoreCase));
        return match ?? throw Invalid($"Unsupported {label}.");
    }

    private static string RequireText(string? value, int max, string label) =>
        OptionalText(value, max, label)
        ?? throw Invalid($"{label} is required.");

    private static string? OptionalText(string? value, int max, string label)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result))
            return null;
        if (result.Length > max || result.Any(char.IsControl))
            throw Invalid($"{label} is invalid or too long.");
        return result;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceExcelMappingIssueDto Issue(
        string code,
        string severity,
        string? sheet,
        string? column,
        string message,
        string fixHint) =>
        new(code, severity, sheet, column, message, fixHint);

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.ExcelMappingProfileNotFound,
            404,
            "The Excel mapping profile was not found.",
            recoveryAction: "select-current-mapping-profile");

    private static SpaceProblemException ReadOnly() =>
        new(
            SpaceErrorCodes.ExcelMappingProfileReadOnly,
            409,
            "The system Excel mapping profile is read-only.",
            recoveryAction: "copy-system-mapping-profile");

    private static SpaceProblemException Conflict(string? detail = null) =>
        new(
            SpaceErrorCodes.ExcelMappingProfileConflict,
            409,
            "The Excel mapping profile conflicts with current data.",
            detail,
            "reload-mapping-profile");

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.ExcelMappingProfileInvalid,
            422,
            "The Excel mapping profile is invalid.",
            detail,
            "correct-mapping-profile");

    private sealed record ResolvedColumn(int? Index, string Status);

    private sealed record HeaderPosition(string Header, int Index);
}
