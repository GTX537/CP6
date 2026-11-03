using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

internal static class LegacyDefinitionResult
{
    // The old DTOs are not extended or silently reinterpreted as v2 commands.
    internal static IActionResult Gone() => new ObjectResult(new
    {
        code = LegacyDefinitionWrites.Code,
        message = "此旧版定义接口已停用。请使用现有设计器中的草稿工作区；旧定义可在历史来源查看。",
        commandEndpoint = "/api/oa/v2/definitions/{FLOW|FORM}",
        readEndpoint = "/api/oa/v2/legacy-definitions/{FLOW|FORM}",
        retryMode = "NONE"
    }) { StatusCode = 410 };
}
