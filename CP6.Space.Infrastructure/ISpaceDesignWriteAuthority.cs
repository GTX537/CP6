namespace CP6.Space.Infrastructure;

public interface ISpaceDesignWriteAuthority
{
    Task AuthorizeModelEditAsync(SpaceContext context, CancellationToken cancellationToken);
    Task ConfirmSessionAsync(SpaceContext context, CancellationToken cancellationToken);
}
