using CP6.Space.Contracts;

namespace CP6.Space.Application;

internal static class SpaceCadSemanticQualityDiagnostics
{
    public const string GeometryOverlapCode =
        "SPACE_CAD_SEMANTIC_GEOMETRY_OVERLAP";

    public static IReadOnlyList<SpaceCadSemanticIssueV1> DetectOverlaps(
        IReadOnlyList<SpaceCadSemanticPreviewItemV1> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var partners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in items
                     .Where(IsAreaCandidate)
                     .GroupBy(item => item.Target)
                     .OrderBy(group => group.Key))
        {
            var active = new List<SpaceCadSemanticPreviewItemV1>();
            foreach (var current in group
                         .OrderBy(item => item.Geometry!.Bounds.MinX)
                         .ThenBy(item => item.Geometry!.Bounds.MinY)
                         .ThenBy(item => item.Source.SourceRef, StringComparer.Ordinal))
            {
                active.RemoveAll(item =>
                    item.Geometry!.Bounds.MaxX <= current.Geometry!.Bounds.MinX);

                foreach (var candidate in active)
                {
                    if (!HasPositiveAreaOverlap(
                            candidate.Geometry!,
                            current.Geometry!))
                    {
                        continue;
                    }

                    partners.TryAdd(
                        current.Source.SourceRef,
                        candidate.PreviewObjectId);
                    partners.TryAdd(
                        candidate.Source.SourceRef,
                        current.PreviewObjectId);
                }

                active.Add(current);
            }
        }

        return items
            .Where(item => partners.ContainsKey(item.Source.SourceRef))
            .OrderBy(item => item.Source.SourceRef, StringComparer.Ordinal)
            .Select(item => new SpaceCadSemanticIssueV1(
                GeometryOverlapCode,
                SpaceCadIssueSeverity.Warning,
                item.Source.SourceRef,
                item.PreviewObjectId,
                item.AppliedMapping.SourceKind,
                item.AppliedMapping.SourceKey,
                item.AppliedMapping.RuleId,
                $"overlaps:{partners[item.Source.SourceRef]}"))
            .ToArray();
    }

    private static bool IsAreaCandidate(SpaceCadSemanticPreviewItemV1 item) =>
        item.Disposition != SpaceCadSemanticDisposition.Rejected
        && item.Geometry is
        {
            Kind: SpaceCadSemanticGeometryKind.Polygon
                or SpaceCadSemanticGeometryKind.Circle,
        } geometry
        && geometry.Bounds.MaxX > geometry.Bounds.MinX
        && geometry.Bounds.MaxY > geometry.Bounds.MinY;

    private static bool HasPositiveAreaOverlap(
        SpaceCadSemanticGeometryV1 left,
        SpaceCadSemanticGeometryV1 right)
    {
        if (!BoundsOverlap(left.Bounds, right.Bounds))
            return false;

        return (left.Kind, right.Kind) switch
        {
            (SpaceCadSemanticGeometryKind.Circle,
                SpaceCadSemanticGeometryKind.Circle) => CirclesOverlap(left, right),
            (SpaceCadSemanticGeometryKind.Polygon,
                SpaceCadSemanticGeometryKind.Polygon) => PolygonsOverlap(left, right),
            (SpaceCadSemanticGeometryKind.Circle,
                SpaceCadSemanticGeometryKind.Polygon) => CirclePolygonOverlap(left, right),
            (SpaceCadSemanticGeometryKind.Polygon,
                SpaceCadSemanticGeometryKind.Circle) => CirclePolygonOverlap(right, left),
            _ => false,
        };
    }

    private static bool BoundsOverlap(
        SpaceCadMillimeterBoundsV1 left,
        SpaceCadMillimeterBoundsV1 right) =>
        left.MinX < right.MaxX
        && right.MinX < left.MaxX
        && left.MinY < right.MaxY
        && right.MinY < left.MaxY;

    private static bool CirclesOverlap(
        SpaceCadSemanticGeometryV1 left,
        SpaceCadSemanticGeometryV1 right)
    {
        var leftCenter = left.Points.Single();
        var rightCenter = right.Points.Single();
        var deltaX = (decimal)leftCenter.X - rightCenter.X;
        var deltaY = (decimal)leftCenter.Y - rightCenter.Y;
        var radius = checked(left.RadiusMillimeters!.Value
                             + right.RadiusMillimeters!.Value);
        return (deltaX * deltaX) + (deltaY * deltaY)
               < (decimal)radius * radius;
    }

    private static bool CirclePolygonOverlap(
        SpaceCadSemanticGeometryV1 circle,
        SpaceCadSemanticGeometryV1 polygon)
    {
        var center = circle.Points.Single();
        var radius = circle.RadiusMillimeters!.Value;
        if (PointInPolygonStrict(center, polygon.Points))
            return true;

        var radiusSquared = (decimal)radius * radius;
        if (polygon.Points.Any(point => DistanceSquared(point, center) < radiusSquared))
            return true;

        return Edges(polygon.Points).Any(edge =>
            DistanceToSegmentSquared(center, edge.Start, edge.End) < radiusSquared);
    }

    private static bool PolygonsOverlap(
        SpaceCadSemanticGeometryV1 left,
        SpaceCadSemanticGeometryV1 right)
    {
        if (left.Points.SequenceEqual(right.Points))
            return true;

        if (left.Points.Any(point => PointInPolygonStrict(point, right.Points))
            || right.Points.Any(point => PointInPolygonStrict(point, left.Points)))
        {
            return true;
        }

        if (Edges(left.Points).Any(edge =>
                PointInPolygonStrict(Midpoint(edge.Start, edge.End), right.Points))
            || Edges(right.Points).Any(edge =>
                PointInPolygonStrict(Midpoint(edge.Start, edge.End), left.Points)))
        {
            return true;
        }

        if (HasCollinearBoundaryOverlapOnSameInteriorSide(
                left.Points,
                right.Points))
        {
            return true;
        }

        return Edges(left.Points).Any(leftEdge =>
            Edges(right.Points).Any(rightEdge => ProperlyIntersects(
                leftEdge.Start,
                leftEdge.End,
                rightEdge.Start,
                rightEdge.End)));
    }

    private static bool HasCollinearBoundaryOverlapOnSameInteriorSide(
        IReadOnlyList<SpaceCadMillimeterPointV1> left,
        IReadOnlyList<SpaceCadMillimeterPointV1> right)
    {
        var leftInteriorSide = Math.Sign(SignedDoubleArea(left));
        var rightInteriorSide = Math.Sign(SignedDoubleArea(right));
        if (leftInteriorSide == 0 || rightInteriorSide == 0)
            return false;

        foreach (var leftEdge in Edges(left))
        {
            var leftDeltaX = (decimal)leftEdge.End.X - leftEdge.Start.X;
            var leftDeltaY = (decimal)leftEdge.End.Y - leftEdge.Start.Y;
            if (leftDeltaX == 0 && leftDeltaY == 0)
                continue;

            foreach (var rightEdge in Edges(right))
            {
                if (Orientation(
                        leftEdge.Start,
                        leftEdge.End,
                        rightEdge.Start) != 0
                    || Orientation(
                        leftEdge.Start,
                        leftEdge.End,
                        rightEdge.End) != 0
                    || !HasPositiveCollinearOverlap(leftEdge, rightEdge))
                {
                    continue;
                }

                var rightDeltaX = (decimal)rightEdge.End.X - rightEdge.Start.X;
                var rightDeltaY = (decimal)rightEdge.End.Y - rightEdge.Start.Y;
                var sameDirection = (leftDeltaX * rightDeltaX)
                                    + (leftDeltaY * rightDeltaY) > 0;
                var mappedRightInteriorSide = sameDirection
                    ? rightInteriorSide
                    : -rightInteriorSide;
                if (mappedRightInteriorSide == leftInteriorSide)
                    return true;
            }
        }

        return false;
    }

    private static bool HasPositiveCollinearOverlap(
        Edge left,
        Edge right)
    {
        var deltaX = Math.Abs((long)left.End.X - left.Start.X);
        var deltaY = Math.Abs((long)left.End.Y - left.Start.Y);
        return deltaX >= deltaY
            ? HasPositiveIntervalOverlap(
                left.Start.X,
                left.End.X,
                right.Start.X,
                right.End.X)
            : HasPositiveIntervalOverlap(
                left.Start.Y,
                left.End.Y,
                right.Start.Y,
                right.End.Y);
    }

    private static bool HasPositiveIntervalOverlap(
        int leftStart,
        int leftEnd,
        int rightStart,
        int rightEnd) =>
        Math.Min(Math.Max(leftStart, leftEnd), Math.Max(rightStart, rightEnd))
        > Math.Max(Math.Min(leftStart, leftEnd), Math.Min(rightStart, rightEnd));

    private static decimal SignedDoubleArea(
        IReadOnlyList<SpaceCadMillimeterPointV1> points)
    {
        var area = 0m;
        foreach (var edge in Edges(points))
        {
            area += ((decimal)edge.Start.X * edge.End.Y)
                    - ((decimal)edge.End.X * edge.Start.Y);
        }

        return area;
    }

    private static bool PointInPolygonStrict(
        SpaceCadMillimeterPointV1 point,
        IReadOnlyList<SpaceCadMillimeterPointV1> polygon)
    {
        if (Edges(polygon).Any(edge => OnSegment(point, edge.Start, edge.End)))
            return false;

        var inside = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var previous = polygon[(index + polygon.Count - 1) % polygon.Count];
            if ((current.Y > point.Y) == (previous.Y > point.Y))
                continue;

            var intersectionX = current.X
                + ((decimal)(point.Y - current.Y) * (previous.X - current.X)
                   / (previous.Y - current.Y));
            if (point.X < intersectionX)
                inside = !inside;
        }

        return inside;
    }

    private static bool ProperlyIntersects(
        SpaceCadMillimeterPointV1 a,
        SpaceCadMillimeterPointV1 b,
        SpaceCadMillimeterPointV1 c,
        SpaceCadMillimeterPointV1 d)
    {
        var first = Orientation(a, b, c);
        var second = Orientation(a, b, d);
        var third = Orientation(c, d, a);
        var fourth = Orientation(c, d, b);
        return first != 0
               && second != 0
               && third != 0
               && fourth != 0
               && Math.Sign(first) != Math.Sign(second)
               && Math.Sign(third) != Math.Sign(fourth);
    }

    private static bool OnSegment(
        SpaceCadMillimeterPointV1 point,
        SpaceCadMillimeterPointV1 start,
        SpaceCadMillimeterPointV1 end) =>
        Orientation(start, end, point) == 0
        && point.X >= Math.Min(start.X, end.X)
        && point.X <= Math.Max(start.X, end.X)
        && point.Y >= Math.Min(start.Y, end.Y)
        && point.Y <= Math.Max(start.Y, end.Y);

    private static decimal Orientation(
        SpaceCadMillimeterPointV1 a,
        SpaceCadMillimeterPointV1 b,
        SpaceCadMillimeterPointV1 c) =>
        ((decimal)b.X - a.X) * (c.Y - a.Y)
        - ((decimal)b.Y - a.Y) * (c.X - a.X);

    private static decimal DistanceSquared(
        SpaceCadMillimeterPointV1 left,
        SpaceCadMillimeterPointV1 right)
    {
        var deltaX = (decimal)left.X - right.X;
        var deltaY = (decimal)left.Y - right.Y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private static decimal DistanceToSegmentSquared(
        SpaceCadMillimeterPointV1 point,
        SpaceCadMillimeterPointV1 start,
        SpaceCadMillimeterPointV1 end)
    {
        var deltaX = (decimal)end.X - start.X;
        var deltaY = (decimal)end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared == 0)
            return DistanceSquared(point, start);

        var projection = (((decimal)point.X - start.X) * deltaX
                          + ((decimal)point.Y - start.Y) * deltaY)
                         / lengthSquared;
        projection = Math.Clamp(projection, 0m, 1m);
        var nearestX = start.X + (projection * deltaX);
        var nearestY = start.Y + (projection * deltaY);
        var distanceX = point.X - nearestX;
        var distanceY = point.Y - nearestY;
        return (distanceX * distanceX) + (distanceY * distanceY);
    }

    private static SpaceCadMillimeterPointV1 Midpoint(
        SpaceCadMillimeterPointV1 start,
        SpaceCadMillimeterPointV1 end) =>
        new(
            checked((int)(((long)start.X + end.X) / 2)),
            checked((int)(((long)start.Y + end.Y) / 2)),
            checked((int)(((long)start.Z + end.Z) / 2)));

    private static IEnumerable<Edge> Edges(
        IReadOnlyList<SpaceCadMillimeterPointV1> points)
    {
        for (var index = 0; index < points.Count; index++)
            yield return new Edge(points[index], points[(index + 1) % points.Count]);
    }

    private readonly record struct Edge(
        SpaceCadMillimeterPointV1 Start,
        SpaceCadMillimeterPointV1 End);
}
