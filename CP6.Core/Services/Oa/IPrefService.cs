namespace CP6.Core.Services.Oa;

public interface IPrefService
{
    Task<string> GetAsync(Guid userId);           // 无则 "{}"
    Task SaveAsync(Guid userId, string prefsJson); // upsert
}
