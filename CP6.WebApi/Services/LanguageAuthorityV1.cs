using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.Services;

internal sealed class LanguageAuthorityV1(CP6Context db, IamAdministrationV1 authority)
{
    internal Task<T> Execute<T>(HttpContext http, bool global, string action,
        Func<UserPermissionContext, CancellationToken, Task<T>> body)
    {
        return action == "query"
            ? Consume(http, Authorized)
            : IamAdministrationHttpV1.ExecuteWriteAsync(http, authority, "lang:" + action, Authorized);

        async Task<T> Authorized(UserPermissionContext actor, CancellationToken ct)
        {
            if (action == "query" && !actor.MenuKeys.Contains("lang") && !actor.ActionKeys.Contains("lang:query"))
                throw new LanguageProblemV1(403, "LANG_SCOPE_FORBIDDEN");
            if (global)
            {
                var platform = await PlatformAdminAuthorization.RequireAsync(db, http.User, ct, lockActor: true);
                if (platform.Id != actor.UserId) throw new LanguageProblemV1(403, "LANG_SCOPE_FORBIDDEN");
            }
            return await body(actor, ct);
        }
    }

    internal Task<T> Consume<T>(HttpContext http, Func<UserPermissionContext, CancellationToken, Task<T>> body)
        => authority.ExecuteIdentityAsync(ct => CurrentActor(http, ct), async (actor, ct) =>
        {
            var result = await body(actor, ct);
            if (await CurrentActor(http, ct) != actor.UserId)
                throw new LanguageProblemV1(401, "REAUTH_REQUIRED");
            return result;
        }, http.RequestAborted);

    private static Task<Guid> CurrentActor(HttpContext http, CancellationToken ct)
        => IamAdministrationHttpV1.CurrentActor(http, http.RequestServices, ct, allowImpersonation: true);

    internal static int Access(UserPermissionContext actor, string field) =>
        actor.FieldPerms.TryGetValue("lang", out var fields) && fields.TryGetValue(field, out var access) ? access : 1;
}

internal sealed class LanguageProblemV1(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
}
