using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

[ApiController]
[Route("api/credit-note")]
[Authorize]
public class CreditNoteController : ControllerBase
{
    private readonly ICreditNoteService _service;

    public CreditNoteController(ICreditNoteService service)
    {
        _service = service;
    }

    /// <summary>POST /api/credit-note/search</summary>
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] CreditNoteQuery query)
    {
        if (query.Page <= 0) query.Page = 1;
        if (query.PageSize <= 0 || query.PageSize > 200) query.PageSize = 50;

        var result = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = result });
    }
}
