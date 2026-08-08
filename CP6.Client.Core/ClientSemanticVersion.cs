namespace CP6.Client.Core;

internal sealed record ClientSemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string[] PreRelease)
{
    public static bool TryParse(
        string? value,
        out ClientSemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var withoutBuild = value.Split('+', 2);
        if (withoutBuild.Length == 2 &&
            !ValidIdentifiers(
                withoutBuild[1],
                allowLeadingZero: true))
        {
            return false;
        }

        var releaseParts = withoutBuild[0].Split('-', 2);
        var core = releaseParts[0].Split('.');
        if (core.Length != 3 ||
            !TryParseNumber(core[0], out var major) ||
            !TryParseNumber(core[1], out var minor) ||
            !TryParseNumber(core[2], out var patch))
        {
            return false;
        }

        var preRelease = releaseParts.Length == 2
            ? releaseParts[1].Split('.')
            : [];
        if (releaseParts.Length == 2 &&
            !ValidIdentifiers(
                releaseParts[1],
                allowLeadingZero: false))
        {
            return false;
        }

        version = new ClientSemanticVersion(
            major,
            minor,
            patch,
            preRelease);
        return true;
    }

    public int CompareTo(ClientSemanticVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison == 0)
            comparison = Minor.CompareTo(other.Minor);
        if (comparison == 0)
            comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0)
            return comparison;
        if (PreRelease.Length == 0)
            return other.PreRelease.Length == 0 ? 0 : 1;
        if (other.PreRelease.Length == 0)
            return -1;

        for (var index = 0;
             index < Math.Min(
                 PreRelease.Length,
                 other.PreRelease.Length);
             index++)
        {
            var leftNumeric = int.TryParse(
                PreRelease[index],
                out var left);
            var rightNumeric = int.TryParse(
                other.PreRelease[index],
                out var right);
            if (leftNumeric && rightNumeric)
                comparison = left.CompareTo(right);
            else if (leftNumeric)
                comparison = -1;
            else if (rightNumeric)
                comparison = 1;
            else
                comparison = string.CompareOrdinal(
                    PreRelease[index],
                    other.PreRelease[index]);
            if (comparison != 0)
                return comparison;
        }

        return PreRelease.Length.CompareTo(
            other.PreRelease.Length);
    }

    private static bool TryParseNumber(
        string value,
        out int result)
    {
        result = 0;
        return value.Length > 0 &&
               (value.Length == 1 || value[0] != '0') &&
               int.TryParse(value, out result) &&
               result >= 0;
    }

    private static bool ValidIdentifiers(
        string value,
        bool allowLeadingZero)
    {
        var identifiers = value.Split('.');
        return identifiers.All(identifier =>
            identifier.Length > 0 &&
            identifier.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character == '-') &&
            (allowLeadingZero ||
             !identifier.All(char.IsAsciiDigit) ||
             identifier.Length == 1 ||
             identifier[0] != '0'));
    }
}
