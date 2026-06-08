using CP6.Entity.DTOs;

namespace CP6.Core.Services;

public interface IOtdReportService
{
    Task<OtdReportSummaryDto> GetSummaryAsync(OtdReportQuery query, CancellationToken ct = default);

    Task<byte[]> ExportCsvAsync(OtdReportQuery query, CancellationToken ct = default);
}
