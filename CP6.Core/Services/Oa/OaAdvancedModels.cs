namespace CP6.Core.Services.Oa;

// ── act-as 授权 ──
public record GrantUser(Guid UserId, string UserName);
public record MyGrants(IReadOnlyList<GrantUser> ICanActAs, IReadOnlyList<GrantUser> CanActForMe);
public record DelegateItem(Guid Id, Guid GrantorId, Guid DelegateId, string DelegateName,
    DateTime ValidFrom, DateTime ValidTo, bool Enable, string? Scope, string? Remark);

// ── 填單表单库 ──
public record FormCard(string FormKey, string FormName, string? Category, string? SubCategory, bool Favorite);
public record CatalogNode(string Category, IReadOnlyList<CatalogSub> Subs);
public record CatalogSub(string SubCategory, IReadOnlyList<FormCard> Forms);

// ── 表單查詢 ──
public record FormQueryFilter(Guid? StarterId, Guid? HandlerId, string? FlowKey, string? Keyword,
    int? Status, DateTime? From, DateTime? To, int Page = 1, int PageSize = 20);
public record FormQueryItem(Guid InstanceId, string FlowKey, string? FlowName, Guid StarterId, string StarterName,
    int Status, string CurrentNode, DateTime CreateDate);
public record FormQueryPage(IReadOnlyList<FormQueryItem> Items, int Total, int Page, int PageSize);
