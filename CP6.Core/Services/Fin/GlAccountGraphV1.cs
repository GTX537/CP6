using static CP6.Core.Services.Fin.GlAccountWorkflowStoreV1;

namespace CP6.Core.Services.Fin;

internal sealed class GlAccountGraphV1
{
    internal sealed record Piece(Guid Root, GlTimelineSegmentV1 Segment, GlAccountContentV1 Content,
        string Digest, Guid Maker);
    private readonly Dictionary<Guid, GlRootV1> roots;
    private readonly Piece[] pieces;

    internal GlAccountGraphV1(GlRootV1[] rootRows, GraphRow[] rows)
    {
        roots = rootRows.ToDictionary(root => root.Id);
        pieces = rows.Select(row =>
        {
            if (!roots.ContainsKey(row.RootId) || Digest(row.Payload) != row.Digest)
                throw Error("GL_GRAPH_UNPROVEN", 503);
            return new Piece(row.RootId, new(row.From, row.ToExclusive, row.VersionId, row.ReleaseId),
                Decode<GlAccountContentV1>(row.Payload), row.Digest, row.Maker);
        }).ToArray();
    }

    internal string Identity(DateOnly businessDay) => Digest(Encode(new
    {
        businessDay, roots = roots.Values.OrderBy(root => root.Id),
        pieces = pieces.Select(piece => new { piece.Root, piece.Segment, piece.Digest })
    }));

    internal static bool Covers(GlTimelineSegmentV1 part, DateOnly at) =>
        part.From <= at && (!part.ToExclusive.HasValue || at < part.ToExclusive.Value);

    private static bool After(DateOnly? until, DateOnly at) => !until.HasValue || until.Value > at;
    private static bool Overlaps(GlTimelineSegmentV1 first, GlTimelineSegmentV1 second)
        => After(first.ToExclusive, second.From) && After(second.ToExclusive, first.From);

    internal (GlTimelineSegmentV1[] Result, GlTimelineSegmentV1[] Superseded, bool RoleConflict) Propose(
        GlRootV1 root, GlVersionV1 version, Guid release, DateOnly currentDay)
    {
        var candidate = new GlTimelineSegmentV1(version.Content.From, version.Content.ToExclusive, version.Id, release);
        var sameRoot = pieces.Where(piece => piece.Root == root.Id).ToArray();
        var retained = new List<Piece>(pieces.Where(piece => piece.Root != root.Id));
        var superseded = new List<GlTimelineSegmentV1>();
        foreach (var piece in sameRoot)
        {
            var old = piece.Segment;
            if (!Overlaps(old, candidate))
            {
                retained.Add(piece);
                continue;
            }

            superseded.Add(old);
            if (old.From < candidate.From)
                retained.Add(piece with { Segment = old with { ToExclusive = candidate.From } });
            if (candidate.ToExclusive is DateOnly end && After(old.ToExclusive, end))
                retained.Add(piece with { Segment = old with { From = end } });
        }
        retained.Add(new(root.Id, candidate, version.Content, version.Digest, version.Maker));
        // 候选区间以外仍保原版本和发布引用，核对候选涵盖的每个断点。
        var start = candidate.From > currentDay ? candidate.From : currentDay;
        var roleConflict = false;
        foreach (var day in Breakpoints(retained, start).Where(date => After(candidate.ToExclusive, date)))
        {
            var active = At(retained, day);
            roleConflict |= HasRoleConflict(active);
            foreach (var node in active.Values.Where(node => node.Content.ParentRootId.HasValue))
                Depth(node, active, candidate.VersionId == node.Segment.VersionId && node.Root == root.Id);
        }

        return (retained.Where(piece => piece.Root == root.Id).OrderBy(piece => piece.Segment.From)
            .Select(piece => piece.Segment).ToArray(), superseded.ToArray(), roleConflict);
    }

    internal Piece[] RequireRestore(GlRootV1 root, DateOnly businessDay)
    {
        var relevant = pieces.Where(piece => piece.Root == root.Id && After(piece.Segment.ToExclusive, businessDay)).ToArray();
        if (relevant.Length == 0) throw Error("NO_EFFECTIVE_VERSION", 422);
        foreach (var day in Breakpoints(pieces, businessDay))
        {
            var active = At(pieces, day);
            if (active.TryGetValue(root.Id, out var piece)) Depth(piece, active, true);
        }
        return relevant;
    }

    internal void RequireNoFutureChildren(Guid parent, DateOnly currentDay)
    {
        if (pieces.Any(piece => piece.Content.ParentRootId == parent &&
            After(piece.Segment.ToExclusive, currentDay) && !RetiredAt(piece.Root, currentDay)))
            throw Error("CHILD_INTERVAL_CONFLICT", 422);
    }

    internal Piece? Current(Guid root, DateOnly date) => AsOf(date).GetValueOrDefault(root);

    internal Dictionary<Guid, Piece> AsOf(DateOnly date) => At(pieces, date);

    internal int Level(Piece piece, DateOnly date) => Depth(piece, AsOf(date), false);

    internal GlAccountEligibilityV1 Role(string role, DateOnly date)
    {
        var active = At(pieces, date);
        var matches = active.Values.Where(piece => piece.Content.RoleKey == role).Take(2).ToArray();
        return matches.Length switch
        {
            0 => new("NOT_CONFIGURED", null, null, null),
            1 => new("FOUND", null, matches[0].Root, matches[0].Segment.VersionId),
            _ => new("UNPROVEN", "ROLE_INTERVAL_CONFLICT", null, null)
        };
    }

    internal GlAccountEligibilityV1 Screen(GlRootV1 root, DateOnly date, string purpose, string currency, string baseCurrency)
    {
        var active = At(pieces, date);
        if (root.ControlState != "ENABLED") return Denied(root.ControlState);
        if (!active.TryGetValue(root.Id, out var piece)) return Denied("NO_EFFECTIVE_VERSION");
        var body = piece.Content;
        if (body.PostingClass != "POSTABLE") return Denied("GROUP_NOT_POSTABLE");
        if (body.IsControl)
            return Denied(purpose == "GL_MANUAL" ? "CONTROL_ACCOUNT_MANUAL_DENIED" : "SUBLEDGER_AUTHORITY_UNPROVEN");
        if (body.RequirePartner) return Denied("PARTNER_AUTHORITY_UNPROVEN");
        if (body.Currency is not { } rule ||
            (rule.Mode == "BASE_ONLY" ? currency != baseCurrency : !rule.Codes.Contains(currency, StringComparer.Ordinal)))
            return Denied("CURRENCY_MISMATCH");
        try { Depth(piece, active, true); }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException error) when (error.Status == 422)
        { return Denied(error.Message); }
        return new("OBSERVED_AS_OF", null, root.Id, piece.Segment.VersionId);

        GlAccountEligibilityV1 Denied(string reason) => new("DENIED", reason, root.Id, null);
    }

    internal int? Level(Guid root, DateOnly date)
    {
        var active = At(pieces, date);
        return active.TryGetValue(root, out var node) ? Depth(node, active, false) : null;
    }

    private static DateOnly[] Breakpoints(IEnumerable<Piece> source, DateOnly from)
        => source.SelectMany(piece => piece.Segment.ToExclusive.HasValue
            ? new[] { piece.Segment.From, piece.Segment.ToExclusive.Value } : [piece.Segment.From])
            .Where(day => day >= from).Append(from).Distinct().Order().ToArray();

    private Dictionary<Guid, Piece> At(IEnumerable<Piece> source, DateOnly date)
    {
        var result = new Dictionary<Guid, Piece>();
        foreach (var piece in source)
        {
            if (Covers(piece.Segment, date) && !RetiredAt(piece.Root, date) && !result.TryAdd(piece.Root, piece))
                throw Error("GL_GRAPH_UNPROVEN", 503);
        }
        return result;
    }

    private bool RetiredAt(Guid root, DateOnly date) => roots[root].RetiredOn is DateOnly retired && retired <= date;

    private static bool HasRoleConflict(Dictionary<Guid, Piece> active) =>
        active.Values.Where(piece => piece.Content.RoleKey is not null)
            .GroupBy(piece => piece.Content.RoleKey, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private int Depth(Piece node, Dictionary<Guid, Piece> active, bool requireEnabledParents)
    {
        var visited = new HashSet<Guid> { node.Root };
        var cursor = node;
        while (cursor.Content.ParentRootId is Guid parent)
        {
            if (!visited.Add(parent)) throw Error("PARENT_CYCLE", 422);
            if (visited.Count > 32) throw Error("DEPENDENCY_LIMIT", 422);
            if (!active.TryGetValue(parent, out var next) || next.Content.PostingClass != "GROUP")
                throw Error("CHILD_INTERVAL_CONFLICT", 422);
            if (requireEnabledParents && roots[parent].ControlState != "ENABLED")
                throw Error("PARENT_UNAVAILABLE", 422);
            cursor = next;
        }
        return visited.Count;
    }
}
