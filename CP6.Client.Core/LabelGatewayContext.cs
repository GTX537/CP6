using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Client.Core;

public sealed record LabelGatewayIdentity(Uri Source, Guid TenantId, Guid ActorId, string DeviceId)
{
    public string ScopeKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Source.AbsoluteUri + "\n" + TenantId.ToString("D") + "\n" + ActorId.ToString("D") + "\n" + DeviceId)));
}

public interface ILabelGatewayContext
{
    LabelGatewayIdentity Capture();
}

internal sealed class ClientLabelGatewayContext(ClientOptions options, IClientSessionService sessions) : ILabelGatewayContext
{
    public LabelGatewayIdentity Capture()
    {
        return ReadIdentity(sessions.AccessToken, options.ApiBaseAddress, options.Context.DeviceId);
    }

    internal static LabelGatewayIdentity ReadIdentity(string? token, Uri source, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new LabelGatewayRecoveryException("AuthenticationRequired");
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || parts[1].Length > 32768) throw new FormatException();
            var encoded = parts[1].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(encoded));
            var root = document.RootElement;
            var tenant = root.GetProperty("tenant_id").GetGuid();
            var actor = root.TryGetProperty("sub", out var subject) ? subject.GetGuid()
                : root.TryGetProperty("nameid", out var name) ? name.GetGuid()
                : root.GetProperty(ClaimTypes.NameIdentifier).GetGuid();
            if (tenant == Guid.Empty || actor == Guid.Empty || string.IsNullOrWhiteSpace(deviceId)
                || !source.IsAbsoluteUri || source.Scheme is not ("http" or "https")
                || source.UserInfo.Length != 0 || source.Query.Length != 0 || source.Fragment.Length != 0)
                throw new FormatException();
            // Claims partition local recovery metadata only. Every API request still authenticates at Main.
            return new LabelGatewayIdentity(source, tenant, actor, deviceId);
        }
        catch (Exception error) when (error is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new LabelGatewayRecoveryException("AuthenticationRequired", inner: error); }
    }
}

internal sealed class MissingLabelGatewayContext : ILabelGatewayContext
{
    public LabelGatewayIdentity Capture() => throw new LabelGatewayRecoveryException("LabelGatewayContextRequired");
}

public sealed class LabelGatewayRecoveryException(string code, string? jobNo = null, Exception? inner = null)
    : InvalidOperationException(jobNo == null ? code : code + ": " + jobNo, inner)
{
    public string Code { get; } = code;
    public string? JobNo { get; } = jobNo;
}
