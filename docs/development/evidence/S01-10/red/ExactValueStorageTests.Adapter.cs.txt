using System.Numerics;
using CP6.Entity.Contracts.FinCredit;
using CP6.Persistence.PostgreSql.Contracts;
using NpgsqlTypes;
using Xunit;

namespace CP6.ExactValues.PostgreSqlTests;

public partial class ExactValueStorageTests
{
    [Fact]
    public void Adapter_binds_38_digit_coefficient_as_BigInteger_without_decimal_roundtrip()
    {
        const string wire = "999999999999999999999999999999.12345678";
        var value = CreditAmount.Parse(wire, CreditAmountProfile.Positive);
        var parameters = ExactValueStorage.CreditParameters(value, CreditAmountProfile.Positive);
        Assert.Equal(NpgsqlDbType.Numeric, parameters[0].NpgsqlDbType);
        Assert.Equal(value.Coefficient, Assert.IsType<BigInteger>(parameters[0].Value));
        Assert.Equal((short)8, parameters[1].Value);
        Assert.Equal(0, parameters[0].Size);
        Assert.Equal(0, parameters[0].Precision);
    }
}
