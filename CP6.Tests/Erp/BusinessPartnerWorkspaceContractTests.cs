using System.Security.Cryptography;
using System.Text;
using CP6.Core.Services.Erp;

namespace CP6.Tests.Erp;

public sealed class BusinessPartnerWorkspaceContractTests
{
    [Fact]
    public void Business_partner_wp04_01_matches_all_frozen_original_and_writable_names()
    {
        // 原 R3 字段表按“原列名|协议名”排序并以 LF 连接后得到的摘要。
        Assert.Equal("b4b9ec32fa8b095983528aebb16dcbeb89b118b2c08b0f420cf10d68871f382e",
            NamesHash(BusinessPartnerProfileFieldsV1.Original, 143));
        Assert.Equal("79b928442c6b3fbcd5fbccf6c22fd0f8c4583cba0c084918bee428924ccc0cb5",
            NamesHash(BusinessPartnerProfileFieldsV1.Writable, 128));
    }

    private static string NamesHash(BusinessPartnerProfileFieldsV1.Field[] fields, int length)
    {
        Assert.Equal(length, fields.Length);
        Assert.Equal(length, fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count());
        var names = string.Join('\n', fields.Select(field => field.Original + "|" + field.Name).Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(names))).ToLowerInvariant();
    }

    [Fact]
    public void Business_partner_wp04_01_normalizes_long_names_and_folds_without_host_locale()
    {
        Assert.Equal("strasse 株式会社", BusinessPartnerUnicode15.Normalize("　Ｓｔｒａße\t\u00a0株式会社\u3000"));
        Assert.Equal(1800, BusinessPartnerUnicode15.Normalize(new string('\ufdfa', 100)).Length);
        Assert.Equal("가", BusinessPartnerUnicode15.Nfkc("\u1100\u1161"));
    }

    private sealed class Unicode15DataFact : FactAttribute
    {
        public Unicode15DataFact()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CP6_BP_UNICODE_CONFORMANCE")))
                Skip = "需提供官方 Unicode 15.0.0 NormalizationTest.txt。";
        }
    }

    [Unicode15DataFact]
    public void Business_partner_wp04_01_matches_unicode_15_compatibility_composition_conformance()
    {
        var file = Environment.GetEnvironmentVariable("CP6_BP_UNICODE_CONFORMANCE")!;
        var count = 0;
        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0 || line[0] is '#' or '@') continue;
            var fields = line.Split(';').Take(5).Select(field => string.Concat(field.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(code => char.ConvertFromUtf32(Convert.ToInt32(code, 16))))).ToArray();
            foreach (var text in fields) Assert.Equal(fields[3], BusinessPartnerUnicode15.Nfkc(text));
            count++;
        }
        Assert.Equal(19074, count);
    }
}
