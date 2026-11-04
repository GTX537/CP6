using CP6.Core.Services.Oa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa/forms/{formKey}/submissions")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FormSubmissionController(PersonalFormOwnerV2 owner):LegacyPersonalFormHttpV1(owner)
{
    [HttpPost,RequestSizeLimit(PersonalFormOwnerV2.DataRequestMaxBytes)]
    public Task<IActionResult> Submit(string formKey,CancellationToken ct)=>Run(()=>Write("FORM_DIRECT_SUBMIT",formKey,ct));
}
