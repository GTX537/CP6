using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DTOs.Space;
using CP6.Space.Application;
using CP6.Space.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class Cp6SpaceLocationCodeRuleProvider(CP6Context context) :
    ISpaceLocationCodeRuleProvider
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceLocationCodingCatalog> GetCatalogAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var siteCode = await context.Space_Sites
            .AsNoTracking()
            .Where(site => site.Id == siteId && site.Enable)
            .Select(site => site.SiteCode)
            .SingleOrDefaultAsync(cancellationToken);
        var rules = await context.Space_CodeRules
            .AsNoTracking()
            .OrderBy(rule => rule.ScopeType)
            .ThenBy(rule => rule.RuleName)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);
        var floorIds = rules
            .Where(rule => rule.ScopeType == 1 && rule.ScopeId.HasValue)
            .Select(rule => rule.ScopeId!.Value)
            .ToArray();
        var zoneIds = rules
            .Where(rule => rule.ScopeType == 2 && rule.ScopeId.HasValue)
            .Select(rule => rule.ScopeId!.Value)
            .ToArray();
        var floors = await context.Space_Floors
            .AsNoTracking()
            .Where(floor => floor.SiteId == siteId && floorIds.Contains(floor.Id))
            .ToDictionaryAsync(floor => floor.Id, cancellationToken);
        var zones = await (
                from zone in context.Space_Zones.AsNoTracking()
                join floor in context.Space_Floors.AsNoTracking()
                    on zone.FloorId equals floor.Id
                where floor.SiteId == siteId && zoneIds.Contains(zone.Id)
                select new
                {
                    zone.Id,
                    zone.ZoneCode,
                    floor.FloorCode,
                })
            .ToDictionaryAsync(zone => zone.Id, cancellationToken);
        return new SpaceLocationCodingCatalog(
            siteCode,
            rules.Select(rule =>
                {
                    floors.TryGetValue(rule.ScopeId ?? Guid.Empty, out var floor);
                    zones.TryGetValue(rule.ScopeId ?? Guid.Empty, out var zone);
                    return new SpaceLocationCodingRuleDefinition(
                        rule.Id,
                        rule.RuleName,
                        rule.ScopeType,
                        rule.ScopeId,
                        DeserializeSegments(rule.Segments),
                        rule.IsDefault,
                        floor?.FloorCode ?? zone?.FloorCode,
                        zone?.ZoneCode);
                })
                .ToArray());
    }

    private static IReadOnlyList<SpaceLocationCodeSegmentDto>
        DeserializeSegments(string json)
    {
        try
        {
            var segments = JsonSerializer.Deserialize<List<CodeSegmentDef?>>(
                json,
                JsonOptions) ?? [];
            if (segments.Any(segment => segment is null))
            {
                throw new JsonException(
                    "Coding rule segments cannot contain null entries.");
            }
            return segments
                .Select(segment => new SpaceLocationCodeSegmentDto(
                    segment!.Key,
                    segment.Name,
                    segment.Source,
                    segment.Width,
                    segment.Pad,
                    segment.Start,
                    segment.Step,
                    segment.Sep,
                    segment.Upper,
                    segment.FixedValue,
                    segment.Optional))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingRuleInvalid,
                422,
                "A location coding rule is invalid.",
                exception.Message,
                "repair-coding-rule");
        }
    }
}
