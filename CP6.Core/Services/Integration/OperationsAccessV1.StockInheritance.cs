namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    private StockTransactionInheritanceV1? stockInheritance;
    internal StockTransactionInheritanceV1 StockInheritance()
    {
        if(stockInheritance!=null)return stockInheritance;
        stockInheritance=new(db,ActorId,Actor,authority.Resource,authority.Action,Authorities[0].OrganizationVersion,
            before=>Authorities.All(a=>a.OrganizationVersion==before),after=>
            {
                for(var i=0;i<Authorities.Count;i++)Authorities[i]=Authorities[i] with {OrganizationVersion=after};
            });
        AtCommit(stockInheritance.ValidateFinalAsync);return stockInheritance;
    }
}
