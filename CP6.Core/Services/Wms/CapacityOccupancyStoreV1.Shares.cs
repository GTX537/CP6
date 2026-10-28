using System.Numerics;
using System.Text.Json;

namespace CP6.Core.Services.Wms;

internal sealed partial class CapacityOccupancyStoreV1
{
    private async Task<Share?> FindShare(Domain d, ShareKey key, CancellationToken ct)
    {
        await using var cmd = Command("SELECT body_bytes FROM cp6_capacity_v1.shares WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND domain_key=@d AND share_key=@key", ("d", DomainKey(d)), ("key", Key(key)));
        var body = await cmd.ExecuteScalarAsync(ct);
        return body is byte[] bytes ? JsonSerializer.Deserialize<Share>(bytes, Json)! : null;
    }
    private async Task InsertShare(Domain d, Share share, string operation, CancellationToken ct)
    {
        Validate(share);
        if (await FindShare(d, share.Key, ct) != null) throw Error("CAPACITY_SHARE_ALREADY_REGISTERED");
        var body = Bytes(share); var range = Bounds(share.Range);
        await Execute("""
            INSERT INTO cp6_capacity_v1.shares(environment_id,sandbox_id,tenant_id,domain_key,share_key,kind,range_from,range_to,capacity_quantity,body_bytes,digest,operation_key)
            VALUES(@e,@s,@t,@d,@key,@kind,@from,@to,@quantity,@body,@digest,@op)
            """, ct, ("d", DomainKey(d)), ("key", Key(share.Key)), ("kind", share.Kind), ("from", Number(range.From)), ("to", Number(range.To)),
            ("quantity", Number(Quantity(share.CapacityQuantity))), ("body", body), ("digest", Hash(body)), ("op", operation));
    }
    private async Task<BigInteger> Use(Domain d, Share share, Range range, string kind, string operation, CancellationToken ct)
    {
        var amount = Measure(share, range); var bounds = Bounds(range);
        await using (var cmd = Command("""
            SELECT EXISTS(SELECT FROM cp6_capacity_v1.used_ranges WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t
            AND domain_key=@d AND share_key=@key AND range_from<@to AND range_to>@from)
            """, ("d", DomainKey(d)), ("key", Key(share.Key)), ("from", Number(bounds.From)), ("to", Number(bounds.To))))
            if ((bool)(await cmd.ExecuteScalarAsync(ct))!) throw Error("CAPACITY_SHARE_RANGE_ALREADY_USED");
        await Execute("""
            INSERT INTO cp6_capacity_v1.used_ranges(environment_id,sandbox_id,tenant_id,domain_key,share_key,operation_key,kind,range_from,range_to,capacity_quantity)
            VALUES(@e,@s,@t,@d,@key,@op,@kind,@from,@to,@quantity)
            """, ct, ("d", DomainKey(d)), ("key", Key(share.Key)), ("op", operation), ("kind", kind), ("from", Number(bounds.From)),
            ("to", Number(bounds.To)), ("quantity", Number(amount)));
        return amount;
    }

    internal Task<string> Register(Operation operation, Domain domain, Share input, Condition condition, CancellationToken ct = default)
    {
        var op = Freeze(operation); var d = Freeze(domain); var share = Freeze(input); var c = Freeze(condition);
        Validate(share);
        return Mutate(op, d, "REGISTER", share, c, async (key, t) =>
        {
            await InsertShare(d, share, key, t); var amount = Quantity(share.CapacityQuantity);
            return share.Kind == "PHYSICAL" ? (amount, BigInteger.Zero) : (BigInteger.Zero, amount);
        }, ct);
    }

    internal Task<string> BindAlias(Operation operation, Domain domain, ShareKey target, Alias alias, Condition condition, CancellationToken ct = default)
    {
        var op = Freeze(operation); var d = Freeze(domain); var key = Freeze(target); var a = Freeze(alias); var c = Freeze(condition);
        Key(key); Text(a.Owner); Text(a.Key); Ref(a.Authority);
        return Mutate(op, d, "BIND_ALIAS", new { target = key, alias = a }, c, async (operationKey, t) =>
        {
            if (await FindShare(d, key, t) == null) throw Error("CAPACITY_SHARE_NOT_FOUND");
            var aliasKey = Hash(Canonical(new { a.Owner, a.Key }));
            await using (var cmd = Command("SELECT EXISTS(SELECT FROM cp6_capacity_v1.aliases WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND domain_key=@d AND alias_key=@key)", ("d", DomainKey(d)), ("key", aliasKey)))
                if ((bool)(await cmd.ExecuteScalarAsync(t))!) throw Error("CAPACITY_ALIAS_ALREADY_BOUND");
            var body = Bytes(new { target = key, alias = a });
            await Execute("""
                INSERT INTO cp6_capacity_v1.aliases(environment_id,sandbox_id,tenant_id,domain_key,alias_key,share_key,body_bytes,digest,operation_key)
                VALUES(@e,@s,@t,@d,@alias,@share,@body,@digest,@op)
                """, t, ("d", DomainKey(d)), ("alias", aliasKey), ("share", Key(key)), ("body", body), ("digest", Hash(body)), ("op", operationKey));
            return (BigInteger.Zero, BigInteger.Zero);
        }, ct);
    }

    internal async Task<ShareKey?> ResolveAlias(Domain domain, string owner, string alias, CancellationToken ct = default)
    {
        var d = Freeze(domain); Require(d); Text(owner); Text(alias); Enter();
        try
        {
            await using var cmd = Command("""
                SELECT s.body_bytes FROM cp6_capacity_v1.aliases a JOIN cp6_capacity_v1.shares s
                USING(environment_id,sandbox_id,tenant_id,domain_key,share_key)
                WHERE a.environment_id=@e AND a.sandbox_id=@s AND a.tenant_id=@t AND a.domain_key=@d AND a.alias_key=@alias
                """, ("d", DomainKey(d)), ("alias", Hash(Canonical(new { Owner = owner, Key = alias }))));
            return await cmd.ExecuteScalarAsync(ct) is byte[] body ? JsonSerializer.Deserialize<Share>(body, Json)!.Key : null;
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    internal Task<string> Realize(Operation operation, Domain domain, ShareKey unreceived, Range consumed, Share physical,
        Evidence relation, Condition condition, CancellationToken ct = default)
    {
        var op = Freeze(operation); var d = Freeze(domain); var source = Freeze(unreceived); var range = Freeze(consumed);
        var root = Freeze(physical); var proof = Freeze(relation); var c = Freeze(condition);
        Key(source); Bounds(range); Validate(root); Ref(proof);
        if (root.Kind != "PHYSICAL") throw Error("CAPACITY_PHYSICAL_SHARE_REQUIRED");
        return Mutate(op, d, "REALIZE", new { source, range, physical = root, relation = proof }, c, async (key, t) =>
        {
            var original = await FindShare(d, source, t) ?? throw Error("CAPACITY_SHARE_NOT_FOUND");
            if (original.Kind != "UNRECEIVED") throw Error("CAPACITY_UNRECEIVED_SHARE_REQUIRED");
            var amount = await Use(d, original, range, "REALIZED", key, t);
            await InsertShare(d, root, key, t);
            return (Quantity(root.CapacityQuantity), -amount);
        }, ct);
    }

    internal Task<string> Release(Operation operation, Domain domain, ShareKey source, Range consumed,
        Evidence authority, Condition condition, CancellationToken ct = default)
    {
        var op = Freeze(operation); var d = Freeze(domain); var key = Freeze(source); var range = Freeze(consumed);
        var proof = Freeze(authority); var c = Freeze(condition); Key(key); Bounds(range); Ref(proof);
        return Mutate(op, d, "RELEASE", new { source = key, range, authority = proof }, c, async (operationKey, t) =>
        {
            var share = await FindShare(d, key, t) ?? throw Error("CAPACITY_SHARE_NOT_FOUND");
            var amount = await Use(d, share, range, "RELEASED", operationKey, t);
            return share.Kind == "PHYSICAL" ? (-amount, BigInteger.Zero) : (BigInteger.Zero, -amount);
        }, ct);
    }
}
