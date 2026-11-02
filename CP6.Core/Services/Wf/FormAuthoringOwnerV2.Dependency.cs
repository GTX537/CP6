using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed partial class FormAuthoringOwnerV2
{
    // Called inside the FLOW Owner's existing tenant/authorization transaction.
    // A null actor is the existing registered-service TxS consumer, already authorized by its binding Owner.
    internal async Task<FormPublicationV2> ReadDependencyAsync(UserPermissionContext? actor, string key, Guid? version,
        string? readMenu, CancellationToken ct)
    {
        await ActivationReady(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEPENDENCY_NOT_AVAILABLE", 404);
        if (actor != null)
        {
            var fields = await Permission(actor, head, false, false, ct, readMenu!);
            if (Fields.Any(f => !fields.Fields[f].CanRead)) throw Error("DEPENDENCY_NOT_READABLE", 403);
        }
        var activation = await Activation(head.Id, ct);
        if (!activation.Enabled) throw Error("DEPENDENCY_DISABLED", 422);
        var id = version ?? activation.PublishedVersionId!.Value;
        var row = await Published(head.Id, id, ct) ?? throw Error("DEPENDENCY_VERSION_NOT_AVAILABLE", 404);
        return JsonSerializer.Deserialize<FormPublicationV2>(row.Snapshot, Json)!;
    }
}
