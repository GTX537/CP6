using System.Threading;

namespace CP6.Core.Services.Space.Observability;

public sealed class SpaceExecutionContextAccessor :
    ISpaceExecutionContextAccessor,
    ISpaceExecutionContextManager
{
    private readonly AsyncLocal<ContextHolder?> _current = new();

    public ISpaceExecutionContext? Current
    {
        get
        {
            var holder = _current.Value;
            if (holder is null)
                return null;

            lock (holder.Sync)
                return holder.Outcome.IsActive
                    ? holder.Context
                    : null;
        }
    }

    public ISpaceExecutionContext? OutcomeCurrent
    {
        get
        {
            var holder = _current.Value;
            if (holder is null)
                return null;

            lock (holder.Sync)
                return holder.Outcome.IsActive &&
                       holder.Context is not null
                    ? holder.Outcome.Context
                    : null;
        }
    }

    public ISpaceExecutionContext RequireCurrent()
    {
        var holder = _current.Value
            ?? throw Required();

        lock (holder.Sync)
        {
            if (!holder.Outcome.IsActive)
                throw Required();
            return holder.Context ?? throw Required();
        }
    }

    public ISpaceExecutionContext RequireOutcomeCurrent()
    {
        var holder = _current.Value
            ?? throw Required();

        lock (holder.Sync)
        {
            if (!holder.Outcome.IsActive ||
                holder.Context is null)
            {
                throw Required();
            }
            return holder.Outcome.Context ?? throw Required();
        }
    }

    public IDisposable Push(SpaceExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context = SpaceExecutionContext.Validate(context);

        var previous = _current.Value;
        var outcome = new OutcomeState(context);
        var owned = new ContextHolder(
            context,
            outcome,
            ownsOutcome: true);
        _current.Value = owned;
        return new RestoreScope(this, owned, previous);
    }

    public IDisposable PushDerived(SpaceExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context = SpaceExecutionContext.Validate(context);

        var previous = _current.Value
            ?? throw Required();
        ContextHolder owned;
        lock (previous.Sync)
        {
            if (!previous.Outcome.IsActive ||
                previous.Context is null)
            {
                throw Required();
            }
            EnsureDerivedIdentity(previous.Context, context);

            owned = new ContextHolder(
                context,
                previous.Outcome,
                ownsOutcome: false);
            _current.Value = owned;
        }

        return new RestoreScope(this, owned, previous);
    }

    public void Enrich(
        Guid? jobId = null,
        Guid? runId = null,
        Guid? publishAttemptId = null,
        string? traceId = null)
    {
        var holder = _current.Value
            ?? throw Required();

        lock (holder.Sync)
        {
            if (!holder.Outcome.IsActive)
                throw Required();
            var value = holder.Context ?? throw Required();
            var enriched = value with
            {
                JobId = Merge(value.JobId, jobId),
                RunId = Merge(value.RunId, runId),
                PublishAttemptId = Merge(value.PublishAttemptId, publishAttemptId),
                TraceId = Merge(value.TraceId, traceId),
            };
            holder.Context = enriched;
            holder.Outcome.Context = enriched;
        }
    }

    private static T? Merge<T>(T? current, T? incoming)
        where T : struct
    {
        if (incoming is null)
            return current;
        if (current is null ||
            EqualityComparer<T>.Default.Equals(current.Value, incoming.Value))
        {
            return incoming;
        }

        throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_CONFLICT");
    }

    private static string Merge(string current, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming) || current == incoming)
            return current;

        throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_CONFLICT");
    }

    private static InvalidOperationException Required()
        => new("SPACE_EXECUTION_CONTEXT_REQUIRED");

    private static void EnsureDerivedIdentity(
        SpaceExecutionContext current,
        SpaceExecutionContext derived)
    {
        if (current.TenantId != derived.TenantId ||
            current.CorrelationId != derived.CorrelationId ||
            !string.Equals(
                current.ActorType,
                derived.ActorType,
                StringComparison.Ordinal) ||
            !string.Equals(
                current.ActorId,
                derived.ActorId,
                StringComparison.Ordinal) ||
            !string.Equals(
                current.ActorName,
                derived.ActorName,
                StringComparison.Ordinal) ||
            !string.Equals(
                current.OrganizationContextId,
                derived.OrganizationContextId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SPACE_EXECUTION_CONTEXT_CONFLICT");
        }
    }

    private sealed class OutcomeState
    {
        public OutcomeState(SpaceExecutionContext context)
        {
            Context = context;
        }

        public object Sync { get; } = new();
        public SpaceExecutionContext? Context { get; set; }
        public bool IsActive { get; set; } = true;
    }

    private sealed class ContextHolder
    {
        public ContextHolder(
            SpaceExecutionContext context,
            OutcomeState outcome,
            bool ownsOutcome)
        {
            Context = context;
            Outcome = outcome;
            OwnsOutcome = ownsOutcome;
        }

        public object Sync => Outcome.Sync;
        public SpaceExecutionContext? Context { get; set; }
        public OutcomeState Outcome { get; }
        public bool OwnsOutcome { get; }
    }

    private sealed class RestoreScope : IDisposable
    {
        private SpaceExecutionContextAccessor? _owner;
        private readonly ContextHolder _owned;
        private readonly ContextHolder? _previous;

        public RestoreScope(
            SpaceExecutionContextAccessor owner,
            ContextHolder owned,
            ContextHolder? previous)
        {
            _owner = owner;
            _owned = owned;
            _previous = previous;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
            {
                lock (_owned.Sync)
                {
                    _owned.Context = null;
                    if (_owned.OwnsOutcome)
                    {
                        _owned.Outcome.Context = null;
                        _owned.Outcome.IsActive = false;
                    }
                }

                if (ReferenceEquals(owner._current.Value, _owned))
                    owner._current.Value = _previous;
            }
        }
    }
}
