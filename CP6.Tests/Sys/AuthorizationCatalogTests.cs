using CP6.Core.Services.Sys;

namespace CP6.Tests.Sys;

public sealed class AuthorizationCatalogTests
{
    [Fact]
    public void Sealed_catalog_rejects_changes_but_accepts_identical_startup_registration()
    {
        var store = new AuthorizationCatalogStoreV1();
        store.RegisterFields("order", [new("Amount", "Amount")]);
        store.RegisterScope("order", "Orders", [1, 5], 1);
        store.Seal();
        store.RegisterFields("order", [new("Amount", "Amount")]);
        store.RegisterScope("order", "Orders", [5, 1], 1);
        Assert.Throws<InvalidOperationException>(() => store.RegisterFields("order", [new("Cost", "Cost")]));
        Assert.Throws<InvalidOperationException>(() => store.RegisterScope("order", "Orders", [1, 5], 5));
        Assert.Throws<InvalidOperationException>(() => store.RegisterScope("new", "New", [1], 1));
        var snapshot = store.Capture();
        Assert.True(snapshot.IsSealed);
        Assert.Equal("Amount", Assert.Single(snapshot.Fields["order"]).Name);
        Assert.Equal(1, snapshot.Scopes["order"].Default);
    }

    [Fact]
    public void Captured_definitions_are_immutable_and_remain_coherent_during_later_registrations()
    {
        var store = new AuthorizationCatalogStoreV1();
        store.RegisterFields("order", [new("Amount", "Amount")]);
        store.RegisterScope("order", "Orders", [1, 5], 1);
        var snapshot = store.Capture();
        Assert.Throws<NotSupportedException>(() => ((IList<int>)snapshot.Scopes["order"].Supports)[0] = 5);
        Assert.Throws<NotSupportedException>(() => ((IList<FieldRegistry.Field>)snapshot.Fields["order"]).Clear());
        Parallel.For(0, 100, n =>
        {
            store.RegisterFields("field-" + n, [new("Field", "Label")]);
            Assert.All(store.Capture().Fields.Values, domain => Assert.Single(domain));
        });
        Assert.Single(snapshot.Fields);
        Assert.Single(snapshot.Scopes);
        Assert.Equal(101, store.Capture().Fields.Count);
    }

    [Fact]
    public void Scope_registration_and_returned_arrays_cannot_mutate_authoritative_supports()
    {
        var key = "scope-" + Guid.NewGuid().ToString("N");
        int[] input = [1, 2];
        DataScopeRegistry.Register(key, "Scope", input, 1);
        input[0] = 5;
        Assert.False(DataScopeRegistry.Supports(key, 5));
        var output = DataScopeRegistry.All.Single(x => x.Key == key);
        output.Supports[0] = 5;
        Assert.False(DataScopeRegistry.Supports(key, 5));
        Assert.True(DataScopeRegistry.Supports(key, 1));
    }

    [Fact]
    public void Field_views_cannot_mutate_registered_domain_and_resource_enumerations_are_snapshots()
    {
        var key = "field-" + Guid.NewGuid().ToString("N");
        FieldRegistry.Register(key, new FieldRegistry.Field("Amount", "Amount"));
        var fields = FieldRegistry.Fields(key);
        if (fields is IList<FieldRegistry.Field> writable && !writable.IsReadOnly) writable.Clear();
        Assert.True(FieldRegistry.Has(key, "Amount"));
        var resources = FieldRegistry.Resources;
        var added = "field-" + Guid.NewGuid().ToString("N");
        FieldRegistry.Register(added, new FieldRegistry.Field("Cost", "Cost"));
        Assert.DoesNotContain(added, resources);
    }

    [Fact]
    public void Invalid_field_replacement_preserves_previous_valid_definition()
    {
        var key = "field-" + Guid.NewGuid().ToString("N");
        FieldRegistry.Register(key, new FieldRegistry.Field("Amount", "Amount"));
        Assert.Throws<ArgumentException>(() => FieldRegistry.Register(key, new FieldRegistry.Field("Amount", "A"), new FieldRegistry.Field("Amount", "B")));
        Assert.Equal("Amount", Assert.Single(FieldRegistry.Fields(key)).Label);
    }

    [Fact]
    public void Invalid_scope_default_preserves_previous_valid_definition()
    {
        var key = "scope-" + Guid.NewGuid().ToString("N");
        DataScopeRegistry.Register(key, "Scope", [1, 2], 1);
        Assert.Throws<ArgumentException>(() => DataScopeRegistry.Register(key, "Scope", [1, 2], 5));
        Assert.Equal(1, DataScopeRegistry.DefaultScope(key));
    }
}
