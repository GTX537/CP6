using System.Text;

namespace CP6.Core.Services.Erp;

/// <summary>交易伙伴规范串固定使用 Unicode 15.0，避免主机 ICU 升级改变查重结果。</summary>
internal static class BusinessPartnerUnicode15
{
    internal const string Version = "BP-NORM-U15.0-1";
    private static readonly Lazy<UnicodeTable> Data = new(UnicodeTable.Read);

    internal static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        var space = false;

        void Append(int scalar)
        {
            if (IsSpace(scalar))
            {
                space = result.Length != 0;
                return;
            }
            if (space) result.Append(' ');
            space = false;
            result.Append(new Rune(scalar));
        }

        foreach (var scalar in CompatibilityCompose(value))
        {
            if (!Data.Value.Folding.TryGetValue(scalar, out var folded)) Append(scalar);
            else foreach (var item in folded) Append(item);
        }
        return result.ToString();
    }

    private static bool IsSpace(int scalar) => scalar is >= 0x9 and <= 0xD or 0x20 or 0x85 or 0xA0 or 0x1680
        or >= 0x2000 and <= 0x200A or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000;

    internal static string Nfkc(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var scalar in CompatibilityCompose(value)) result.Append(new Rune(scalar));
        return result.ToString();
    }

    private static List<int> CompatibilityCompose(string value)
    {
        var table = Data.Value;
        var decomposed = new List<int>(value.Length);
        foreach (var rune in value.EnumerateRunes()) Expand(rune.Value);
        var composed = new List<int>(decomposed.Count);
        var starterIndex = -1;
        var lastClass = 0;
        foreach (var scalar in decomposed)
        {
            var rank = table.Combining.GetValueOrDefault(scalar);
            if (starterIndex >= 0 && (lastClass == 0 || lastClass < rank) &&
                ComposePair(composed[starterIndex], scalar, table, out var merged))
            {
                composed[starterIndex] = merged;
                continue;
            }
            if (rank == 0) starterIndex = composed.Count;
            composed.Add(scalar);
            lastClass = rank;
        }
        return composed;

        void Expand(int scalar)
        {
            const int syllableBase = 0xAC00;
            if (scalar is >= syllableBase and < syllableBase + 11172)
            {
                var offset = scalar - syllableBase;
                Order(0x1100 + offset / 588);
                Order(0x1161 + offset % 588 / 28);
                if (offset % 28 != 0) Order(0x11A7 + offset % 28);
            }
            else if (table.Decomposition.TryGetValue(scalar, out var members))
            {
                foreach (var member in members) Expand(member);
            }
            else Order(scalar);
        }

        void Order(int scalar)
        {
            var rank = table.Combining.GetValueOrDefault(scalar);
            var index = decomposed.Count;
            if (rank != 0)
                while (index > 0 && table.Combining.GetValueOrDefault(decomposed[index - 1]) > rank) index--;
            decomposed.Insert(index, scalar);
        }
    }

    private static bool ComposePair(int left, int right, UnicodeTable table, out int value)
    {
        if (left is >= 0x1100 and < 0x1113 && right is >= 0x1161 and < 0x1176)
        {
            value = 0xAC00 + (left - 0x1100) * 588 + (right - 0x1161) * 28;
            return true;
        }
        if (left is >= 0xAC00 and < 0xD7A4 && (left - 0xAC00) % 28 == 0 && right is > 0x11A7 and < 0x11C3)
        {
            value = left + right - 0x11A7;
            return true;
        }
        return table.Composition.TryGetValue((left, right), out value);
    }

    private sealed class UnicodeTable
    {
        internal Dictionary<int, int> Combining { get; } = [];
        internal Dictionary<int, int[]> Decomposition { get; } = [];
        internal Dictionary<int, int[]> Folding { get; } = [];
        internal Dictionary<(int, int), int> Composition { get; } = [];

        internal static UnicodeTable Read()
        {
            using var resource = typeof(BusinessPartnerUnicode15).Assembly.GetManifestResourceStream("CP6.BusinessPartner.Unicode15.txt")
                ?? throw new InvalidOperationException("交易伙伴 Unicode 15.0 数据未安装。");
            using var reader = new StreamReader(resource);
            var table = new UnicodeTable();
            while (reader.ReadLine() is { } line)
            {
                if (line[0] == '#') continue;
                var values = line[2..].Split(' ').Select(value => Convert.ToInt32(value, 16)).ToArray();
                switch (line[0])
                {
                    case 'C': table.Combining.Add(values[0], values[1]); break;
                    case 'D': table.Decomposition.Add(values[0], values[1..]); break;
                    case 'P': table.Composition.Add((values[0], values[1]), values[2]); break;
                    case 'F': table.Folding.Add(values[0], values[1..]); break;
                    default: throw new InvalidOperationException("交易伙伴 Unicode 15.0 数据行无法识别。");
                }
            }
            return table;
        }
    }
}
