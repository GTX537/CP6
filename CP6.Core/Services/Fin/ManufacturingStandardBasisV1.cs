using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Entity.DomainModels.Fin;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardBasisSubmissionV1(Guid CommandId, Guid WorkOrderId, Guid PolicyVersionId, Guid? PreviousVersionId,
    string TargetQuantity, string OutputUnit, DateOnly From, DateOnly? ToExclusive,
    StandardMaterialInputV1[] Materials, StandardTimeInputV1[] Times, string? ZeroBasisReference, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardMaterialInputV1(Guid MaterialOccurrenceId, string AllowedQuantity, string Unit);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardTimeInputV1(Guid WorkStepId, string LaborHours, string MachineHours);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardBasisReviewV1(Guid CommandId, long ExpectedRevision, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardBasisReleaseV1(Guid CommandId, long ExpectedRevision);

public sealed record StandardBasisStatusV1(Guid VersionId, long Sequence, string State, long Revision);
internal sealed record StandardBasisReferenceV1(Guid VersionId, long Sequence, Guid PolicyVersionId);

internal sealed record StandardBasisBodyV1(Guid ProductId, string OutputUnit, decimal TargetQuantity, string Currency,
    StandardBasisLineV1[] Lines, decimal Total, string? ZeroBasisReference)
{
    internal object ToView() => new
    {
        ProductId, OutputUnit, targetQuantity = DecimalText(TargetQuantity), Currency, total = DecimalText(Total), ZeroBasisReference,
        lines = Lines.Select(line => new
        {
            line.InputId, element = line.Element.ToString(), line.Unit, quantity = DecimalText(line.Quantity),
            unitPrice = DecimalText(line.UnitPrice), amount = DecimalText(line.Amount), line.SourceId, line.SourceRowVersion,
            line.OperationId, line.WorkCenterId,
        }),
    };

    internal static string DecimalText(decimal value) => value.ToString("F8", CultureInfo.InvariantCulture);

    internal static decimal Quantity(string? text)
    {
        if (text == null || !Regex.IsMatch(text, @"\A(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,8})?\z", RegexOptions.CultureInvariant))
            throw Error("STANDARD_BASIS_QUANTITY_INVALID", 422);
        return decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}

internal sealed record StandardBasisLineV1(Guid InputId, CostElement Element, string Unit, decimal Quantity,
    decimal UnitPrice, decimal Amount, Guid SourceId, byte[]? SourceRowVersion = null, Guid? OperationId = null,
    Guid? WorkCenterId = null, Guid? RateMaker = null, string? WorkCenter = null);
