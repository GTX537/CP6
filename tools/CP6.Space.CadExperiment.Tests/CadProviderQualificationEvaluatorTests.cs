using System.Text.Json;
using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.Tests;

public sealed class CadProviderQualificationEvaluatorTests
{
    private static readonly Guid SiteId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTime EvaluatedAtUtc =
        new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Selects_unique_highest_and_second_and_builds_import_contract()
    {
        var request = Request(
            Candidate("candidate-a", Scores92()),
            Candidate("candidate-b", Scores87()),
            Candidate("candidate-c", Scores82()));

        var report = CadProviderQualificationEvaluator.Evaluate(request);
        var replay = CadProviderQualificationEvaluator.Evaluate(request);

        Assert.True(report.CadGaReady);
        Assert.Empty(report.BlockingCodes);
        Assert.Equal(3, report.QualifiedCandidateCount);
        Assert.Equal("candidate-a", report.Primary?.ProviderKey);
        Assert.Equal(92, report.Primary?.QualificationScore);
        Assert.Equal("candidate-b", report.Backup?.ProviderKey);
        Assert.Equal(87, report.Backup?.QualificationScore);
        Assert.Equal(64, report.SelectionSha256.Length);
        Assert.Equal(report.SelectionSha256, replay.SelectionSha256);
        Assert.Collection(
            report.CertificationInputs,
            primary =>
            {
                Assert.Equal("candidate-a", primary.ProviderKey);
                Assert.Equal("2026.8.14", primary.ProviderVersion);
                Assert.Equal("Primary", primary.Role);
                Assert.Equal(92, primary.QualificationScore);
                Assert.True(primary.LicensingApproved);
                Assert.True(primary.SecurityApproved);
                Assert.True(primary.DataRegionApproved);
                Assert.True(primary.DeletionRetentionApproved);
                Assert.Equal(
                    $"sha256:{report.SelectionSha256}",
                    primary.QualificationEvidenceReference);
            },
            backup =>
            {
                Assert.Equal("candidate-b", backup.ProviderKey);
                Assert.Equal("2026.8.14", backup.ProviderVersion);
                Assert.Equal("Backup", backup.Role);
                Assert.Equal(87, backup.QualificationScore);
                Assert.Equal(
                    $"sha256:{report.SelectionSha256}",
                    backup.QualificationEvidenceReference);
            });
    }

    [Fact]
    public void Selects_one_qualified_primary_without_requiring_backup()
    {
        var report = CadProviderQualificationEvaluator.Evaluate(
            Request(Candidate("candidate-a", Scores92())));

        Assert.True(report.CadGaReady);
        Assert.Empty(report.BlockingCodes);
        Assert.Equal(1, report.QualifiedCandidateCount);
        Assert.Equal("candidate-a", report.Primary?.ProviderKey);
        Assert.Null(report.Backup);
        var certification = Assert.Single(report.CertificationInputs);
        Assert.Equal("Primary", certification.Role);
        Assert.Equal("candidate-a", certification.ProviderKey);
    }

    [Fact]
    public void Fails_closed_when_threshold_or_hard_gate_is_missing()
    {
        var missingSecurity = Candidate("candidate-a", Scores92()) with
        {
            HardGates = ValidHardGates() with { SecurityApprovalReference = null },
        };
        var belowThreshold = Candidate("candidate-b", Scores79());

        var report = CadProviderQualificationEvaluator.Evaluate(
            Request(missingSecurity, belowThreshold));

        Assert.False(report.CadGaReady);
        Assert.Null(report.Primary);
        Assert.Null(report.Backup);
        Assert.Empty(report.CertificationInputs);
        Assert.Equal(0, report.QualifiedCandidateCount);
        Assert.Contains(
            "CAD_PROVIDER_PRIMARY_NOT_QUALIFIED",
            report.BlockingCodes);
        Assert.Contains(
            "CAD_PROVIDER_SECURITY_APPROVAL_MISSING",
            report.Candidates.Single(item => item.ProviderKey == "candidate-a")
                .BlockingCodes);
        Assert.Contains(
            "CAD_PROVIDER_QUALIFICATION_SCORE_BELOW_THRESHOLD",
            report.Candidates.Single(item => item.ProviderKey == "candidate-b")
                .BlockingCodes);
    }

    [Fact]
    public void Fails_closed_when_primary_score_is_tied()
    {
        var report = CadProviderQualificationEvaluator.Evaluate(
            Request(
                Candidate("candidate-a", Scores92()),
                Candidate("candidate-b", Scores92()),
                Candidate("candidate-c", Scores82())));

        Assert.False(report.CadGaReady);
        Assert.Contains("CAD_PROVIDER_PRIMARY_SCORE_TIE", report.BlockingCodes);
        Assert.Null(report.Primary);
        Assert.Null(report.Backup);
        Assert.Empty(report.CertificationInputs);
    }

    [Fact]
    public void Keeps_primary_ready_when_optional_backup_score_is_tied()
    {
        var report = CadProviderQualificationEvaluator.Evaluate(
            Request(
                Candidate("candidate-a", Scores92()),
                Candidate("candidate-b", Scores87()),
                Candidate("candidate-c", Scores87())));

        Assert.True(report.CadGaReady);
        Assert.Empty(report.BlockingCodes);
        Assert.Equal("candidate-a", report.Primary?.ProviderKey);
        Assert.Null(report.Backup);
        var certification = Assert.Single(report.CertificationInputs);
        Assert.Equal("Primary", certification.Role);
    }

    [Fact]
    public void Fails_closed_when_candidates_use_different_frozen_baselines()
    {
        var differentDataset = Candidate("candidate-b", Scores87()) with
        {
            GoldenDatasetSha256 = new string('a', 64),
        };

        var report = CadProviderQualificationEvaluator.Evaluate(
            Request(Candidate("candidate-a", Scores92()), differentDataset));

        Assert.False(report.CadGaReady);
        Assert.Contains(
            "CAD_PROVIDER_QUALIFICATION_BASELINE_MISMATCH",
            report.BlockingCodes);
        Assert.Empty(report.CertificationInputs);
    }

    [Fact]
    public void Rejects_out_of_range_dimensions()
    {
        var invalid = Candidate("candidate-a", Scores92()) with
        {
            Scores = Scores92() with { EntityBlockAttributeCoverage = 26 },
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            CadProviderQualificationEvaluator.Evaluate(Request(invalid)));

        Assert.Contains("EntityBlockAttributeCoverage", error.Message);
    }

    [Fact]
    public async Task Rejects_unknown_and_duplicate_json_properties()
    {
        using var fixture = new TemporaryDirectory();
        var json = JsonSerializer.Serialize(Request(Candidate("candidate-a", Scores92())),
            CadExperimentJson.Options);
        var unknownPath = fixture.Write(
            "unknown.json",
            json.Replace("\"siteId\"", "\"unexpected\":true,\"siteId\""));
        var duplicatePath = fixture.Write(
            "duplicate.json",
            json.Replace(
                "\"siteId\"",
                $"\"SiteId\":\"{SiteId:D}\",\"siteId\"",
                StringComparison.Ordinal));

        await Assert.ThrowsAsync<JsonException>(() =>
            CadProviderQualificationEvaluator.EvaluateAsync(unknownPath));
        var duplicateError = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CadProviderQualificationEvaluator.EvaluateAsync(duplicatePath));
        Assert.Contains("Duplicate JSON property", duplicateError.Message);
    }

    [Fact]
    public async Task Command_writes_a_hash_bound_single_primary_report()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write(
            "scorecard.json",
            JsonSerializer.Serialize(
                Request(Candidate("candidate-a", Scores92())),
                CadExperimentJson.Options));
        var output = Path.Combine(fixture.Path, "selection.json");

        var exitCode = await Program.Main(
            ["qualify-providers", "--input", input, "--output", output]);
        var report = JsonSerializer.Deserialize<CadProviderQualificationReportV1>(
            await File.ReadAllTextAsync(output),
            CadExperimentJson.Options);

        Assert.Equal(0, exitCode);
        Assert.NotNull(report);
        Assert.True(report.CadGaReady);
        Assert.Equal(64, report.SelectionSha256.Length);
        Assert.Single(report.CertificationInputs);
    }

    private static CadProviderQualificationRequestV1 Request(
        params CadProviderQualificationCandidateV1[] candidates) =>
        new(1, SiteId, EvaluatedAtUtc, candidates);

    private static CadProviderQualificationCandidateV1 Candidate(
        string providerKey,
        CadProviderQualificationScoresV1 scores) =>
        new(
            providerKey,
            "2026.8.14",
            "OnPremisesIsolatedWorker",
            "SiteLocal",
            SupportsDwg: true,
            SupportsDxf: true,
            EvaluatedAtUtc.AddDays(-1),
            EvaluatedAtUtc.AddDays(30),
            $"evidence://approval/{providerKey}",
            null,
            ValidHardGates(providerKey),
            TrialPreflightPassed: true,
            new string('f', 64),
            CadProviderQualificationEvaluator.RubricVersion,
            new string('d', 64),
            new string('e', 64),
            $"evidence://qualification/{providerKey}",
            scores);

    private static CadProviderHardGateEvidenceV1 ValidHardGates(
        string providerKey = "candidate-a") =>
        new(
            $"evidence://licensing/{providerKey}",
            $"evidence://security/{providerKey}",
            $"evidence://data-region/{providerKey}",
            $"evidence://retention/{providerKey}");

    private static CadProviderQualificationScoresV1 Scores92() =>
        new(23, 19, 14, 14, 14, 8);

    private static CadProviderQualificationScoresV1 Scores87() =>
        new(22, 18, 13, 13, 13, 8);

    private static CadProviderQualificationScoresV1 Scores82() =>
        new(20, 17, 12, 12, 13, 8);

    private static CadProviderQualificationScoresV1 Scores79() =>
        new(20, 16, 12, 11, 12, 8);
}
