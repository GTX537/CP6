namespace CP6.Core.Services.Oa;

public sealed class PersonalFormDispositionRequiredV2(Guid requestId):InvalidOperationException("REBASE_HIDDEN_LOSS_BLOCKED")
{
    public Guid RequestId { get; }=requestId;
}

// Created only after the disposition Owner checks the exact native source and current qualification.
// It authorizes these losses, not field disclosure or a different draft writer.
internal sealed record FormDispositionRebaseV1(FormDispositionRefV1 Reference,FormDispositionScopeV1 Scope,
    Guid Processor,FormDispositionLossV1[] Losses,DateTimeOffset RetainUntil)
{
    internal bool Allows(string field,string path,string kind)=>Losses.Any(x=>x.Field==field&&x.Path==path&&x.Kind==kind);
}
