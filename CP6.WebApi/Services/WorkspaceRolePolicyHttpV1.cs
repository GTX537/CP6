using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.Services;

internal static class WorkspaceRolePolicyHttpV1
{
    private const string MediaType = "application/vnd.cp6.iam-role-policy+json";

    internal static bool IsVersioned(HttpRequest request) =>
        System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var value) &&
        string.Equals(value.MediaType, MediaType, StringComparison.OrdinalIgnoreCase);

    internal static T? Read<T>(HttpContext context, JsonElement input) =>
        input.Deserialize<T>(context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions);

    internal static async Task<IActionResult> Save(ControllerBase controller, int roleId, JsonElement input)
    {
        IamRolePolicyCommandV1? command;
        try { command = Read<IamRolePolicyCommandV1>(controller.HttpContext, input); }
        catch (JsonException) { return controller.BadRequest(new { code = "IAM_COMMAND_ENVELOPE_REQUIRED" }); }
        if (command is null) return controller.BadRequest(new { code = "IAM_COMMAND_ENVELOPE_REQUIRED" });

        var http = controller.HttpContext;
        var policies = http.RequestServices.GetRequiredService<IamRolePolicyAdministrationV1>();
        // 由同一 IAM Owner 核对当前授权、角色与引用版本，并写入原操作。
        return await IamAdministrationHttpV1.Execute(controller, async () =>
            await policies.ChangeAsync("actions", roleId, command, ct => IamAdministrationHttpV1.CurrentActor(http, ct), http.RequestAborted));
    }
}
