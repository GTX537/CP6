namespace CP6.Core.Services.Wf;

public interface IFormSubmissionService
{
    Task<SubmitFormResult> SubmitAsync(SubmitFormCommand command, CancellationToken ct = default);
}
