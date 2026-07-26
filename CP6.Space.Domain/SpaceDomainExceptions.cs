namespace CP6.Space.Domain;

public sealed class SpaceVersionStateException : InvalidOperationException
{
    public SpaceVersionStateException(string message) : base(message)
    {
    }
}

public sealed class SpaceVersionConflictException : InvalidOperationException
{
    public SpaceVersionConflictException(string message) : base(message)
    {
    }
}

public sealed class SpaceTenantScopeException : InvalidOperationException
{
    public SpaceTenantScopeException(string message) : base(message)
    {
    }
}

public sealed class SpaceFileStateException : InvalidOperationException
{
    public SpaceFileStateException(string message) : base(message)
    {
    }
}

public sealed class SpaceFileReferenceException : InvalidOperationException
{
    public SpaceFileReferenceException(string message) : base(message)
    {
    }
}
