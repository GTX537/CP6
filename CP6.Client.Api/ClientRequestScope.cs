namespace CP6.Client.Api;

// Local transport metadata, never an authorization claim sent to Main.
public sealed record ClientRequestScope(Uri Source, Guid TenantId, Guid ActorId, string DeviceId, long? InteractionVersion = null)
{
    public static readonly HttpRequestOptionsKey<ClientRequestScope> Key = new("CP6.PinnedRequestScope");
}
