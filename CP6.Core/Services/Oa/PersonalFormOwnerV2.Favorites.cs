using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormFavoriteInputV2([property: JsonRequired] bool On);
public sealed record PersonalFormFavoriteResultV2(string FormKey, bool Favorite, DateTimeOffset UpdatedAtUtc);
public sealed record PersonalFormFavoriteItemV2(string FormKey, bool Available, PersonalFormCatalogItemV2? Publication);
public sealed record PersonalFormFavoritesPageV2(PersonalFormFavoriteItemV2[] Items, int Total, int Page, int PageSize);

public sealed partial class PersonalFormOwnerV2
{
    internal const string Favorite = "FORM_FAVORITE_SET";
    private sealed record FavoriteRow(Guid Id, string FormKey, DateTimeOffset Updated);
    private IQueryable<FavoriteRow> Favorites(Guid actor) => db.Database.SqlQuery<FavoriteRow>($"""
        SELECT id AS "Id",form_key AS "FormKey",updated_at AS "Updated" FROM cp6_personal_form_v2.favorites
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor}
        """);
    private async Task FavoritesReady(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_personal_form_v2.favorites_ready() AS \"Value\"").SingleAsync(ct)) throw Error("FAVORITE_WRITER_UNAVAILABLE");
    }
    public async Task<PersonalFormFavoritesPageV2> FavoritesAsync(int page = 1, int pageSize = 20, string? search = null, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 || search?.Length > 200) throw Error("CATALOG_QUERY_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); var lease = await ReadLease(ct);
        var actor = await Actor(ct); await Action(actor, "query", ct);
        var favorites = await Favorites(actor.UserId).ToArrayAsync(ct);
        var available = await CatalogSource(actor).Where(x => favorites.Select(f => f.FormKey).Contains(x.FormKey)).ToDictionaryAsync(x => x.FormKey, StringComparer.Ordinal, ct);
        // Only an actor's own saved key survives loss of current form visibility. No cached name/schema is stored.
        var rows = favorites.Select(f => new PersonalFormFavoriteItemV2(f.FormKey, available.ContainsKey(f.FormKey), available.GetValueOrDefault(f.FormKey)))
            .Where(x => string.IsNullOrWhiteSpace(search) || x.FormKey.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Publication?.Name.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(x => x.FormKey, StringComparer.Ordinal).ToArray();
        await Final(actor, lease, ct, false);
        return new(rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), rows.Length, page, pageSize);
    }
    public async Task<PersonalFormResultV2> SetFavoriteAsync(string key, byte[] raw, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Contains('\0')) throw Error("DEFINITION_KEY_INVALID", 400);
        // A new explicit preference assignment has no definition/draft CAS predecessor.
        // WF_AUTH serializes its set semantics; command replay returns its original historical result.
        var command = CommandInput<PersonalFormFavoriteInputV2>(raw, false);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            var lease = await ReadLease(ct); var actor = await Actor(ct); await Action(actor, "query", ct);
            var old = await Original(actor.UserId, Favorite, key, command.Id, ct);
            if (old != null) { Match(old, command.Digest); await Final(actor, lease, ct, false); return Parse<PersonalFormResultV2>(old.Result); }
            await WriteReady(lease, ct); await FavoritesReady(ct); await Action(actor, "favorite", ct);
            if (command.Body.On && !await CatalogSource(actor).AnyAsync(x => x.FormKey == key, ct)) throw Error("FORM_NOT_FOUND", 404);
            var prior = await Favorites(actor.UserId).SingleOrDefaultAsync(x => x.FormKey == key, ct);
            var wasFavorite = prior != null;
            var write = Guid.NewGuid(); var at = Clock(); Guid? favoriteId = prior?.Id;
            await BeginCommand(write, actor.UserId, Favorite, key, command, null, null, null, null, ct);
            if (command.Body.On && prior == null)
            {
                var head = await Heads().SingleAsync(x => x.Key == key, ct); favoriteId = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_personal_form_v2.favorites(tenant_id,environment_id,actor_id,form_key,id,head_id,updated_at)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{key},{favoriteId},{head.Id},{at})
                    """, ct);
                // The persisted PostgreSQL microsecond value is the authoritative receipt time.
                prior = await Favorites(actor.UserId).SingleAsync(x => x.FormKey == key, ct);
            }
            else if (!command.Body.On && prior != null)
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_personal_form_v2.favorites WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor.UserId} AND form_key={key}", ct);
            var value = new PersonalFormFavoriteResultV2(key, command.Body.On, command.Body.On ? prior!.Updated : at);
            var result = await Complete(write, command, command.Body.On ? favoriteId : null, null, value,
                new { actor.UserId, Scope = lease.Snapshot, Before = wasFavorite, After = command.Body.On }, ct);
            await Final(actor, lease, ct); await FavoritesReady(ct); committing = true; await tx.CommitAsync(ct); return result;
        }
        catch (Exception ex) { await Rollback(tx, committing, ex); throw; }
    }
}
