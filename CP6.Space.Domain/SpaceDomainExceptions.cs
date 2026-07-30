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

public sealed class SpaceJobStateException : InvalidOperationException
{
    public SpaceJobStateException(string message) : base(message)
    {
    }
}

public sealed class SpaceJobLeaseLostException : InvalidOperationException
{
    public SpaceJobLeaseLostException(string message) : base(message)
    {
    }

    public SpaceJobLeaseLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class SpaceJobNotRetryableException : InvalidOperationException
{
    public SpaceJobNotRetryableException(string message) : base(message)
    {
    }
}

public sealed class SpaceGenerationStateException :
    InvalidOperationException
{
    public SpaceGenerationStateException(string message) : base(message)
    {
    }
}

public sealed class SpaceProposalStateException :
    InvalidOperationException
{
    public SpaceProposalStateException(string message) : base(message)
    {
    }
}
