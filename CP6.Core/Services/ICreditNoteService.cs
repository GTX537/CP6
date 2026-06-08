using CP6.Entity.DTOs;
using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services;

public interface ICreditNoteService
{
    Task<PagedResultDto<CreditNoteListItemDto>> SearchAsync(CreditNoteQuery query);
}
