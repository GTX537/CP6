namespace CP6.Core.Services.Fin;

public interface IBudgetLineService
{
    Task<List<BudgetLineGridRow>> ListLinesAsync(Guid versionId);
    Task<FinResult> UpsertLineAsync(BudgetLineDto dto);
    Task<FinResult> DeleteLineAsync(Guid lineId);
    Task<BudgetImportPreviewResult> PreviewImportAsync(Guid versionId, Stream excel);   // C-3
    Task<FinResult> ConfirmImportAsync(Guid versionId, Stream excel);                   // C-3
}
