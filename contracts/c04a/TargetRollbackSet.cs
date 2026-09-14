using System.Text.Json;

namespace CP6.Crm.Cutover;

// Byte-identical additive contract compiled independently by Core and CRM.
// The source binds this complete anchor before freezing; recovery requires every receipt.
public sealed record TargetRollbackSetAnchor(IReadOnlyList<TargetRollbackAnchor> Targets)
{
    public string Format => "CP6.CRM.TargetRollbackSetAnchor.v1";
    public string Digest() => TargetRollbackProtocol.Digest(this);

    public void Validate()
    {
        if (Targets is not { Count: >= 2 and <= 16 }) Fail();
        string? previous = null;
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in Targets)
        {
            if (target is null || target.OrganizationId == Guid.Empty || !TargetRollbackProtocol.IsHash(target.TargetBindingSha256)) Fail();
            var organization = target.OrganizationId.ToString("D");
            if (previous is not null && StringComparer.Ordinal.Compare(previous, organization) >= 0
                || !bindings.Add(target.TargetBindingSha256)) Fail();
            previous = organization;
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail() => throw new TargetRollbackException("C04A_TARGET_ROLLBACK_SET_ANCHOR_MISMATCH");
}

public sealed record TargetRollbackSetReceipt(IReadOnlyList<TargetRollbackReceipt> Targets)
{
    public string Format => "CP6.CRM.TargetRollbackSetReceipt.v1";
    public TargetRollbackSetAnchor Anchor => new(Targets.Select(target => target.Anchor).ToArray());
    public string Digest() => TargetRollbackProtocol.Digest(this);

    public void Validate()
    {
        if (Targets is not { Count: >= 2 and <= 16 }
            || Targets.Any(target => target is null || target.Permit is null || target.Target is null))
            throw new TargetRollbackException("C04A_TARGET_ROLLBACK_SET_RECEIPT_MISMATCH");
        foreach (var target in Targets)
            _ = TargetRollbackProtocol.ParseReceipt(JsonSerializer.Serialize(target));
        try { Anchor.Validate(); }
        catch (TargetRollbackException) { throw new TargetRollbackException("C04A_TARGET_ROLLBACK_SET_RECEIPT_MISMATCH"); }
    }
}
