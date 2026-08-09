namespace CP6.Space.CadExperiment;

public sealed class CommandLine
{
    private readonly string[] _arguments;

    public CommandLine(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            throw new ArgumentException("A command is required.");
        }

        Command = arguments[0];
        _arguments = arguments[1..];
    }

    public string Command { get; }

    public bool HasFlag(string name)
    {
        return _arguments.Contains(name, StringComparer.Ordinal);
    }

    public string Required(string name)
    {
        return Optional(name)
            ?? throw new ArgumentException($"Required option '{name}' is missing.");
    }

    public string? Optional(string name)
    {
        for (var index = 0; index < _arguments.Length; index++)
        {
            if (_arguments[index] != name)
            {
                continue;
            }

            if (index + 1 >= _arguments.Length || _arguments[index + 1].StartsWith("--"))
            {
                throw new ArgumentException($"Option '{name}' requires a value.");
            }

            return _arguments[index + 1];
        }

        return null;
    }

    public IReadOnlyList<string> All(string name)
    {
        var values = new List<string>();
        for (var index = 0; index < _arguments.Length; index++)
        {
            if (_arguments[index] != name)
            {
                continue;
            }

            if (index + 1 >= _arguments.Length || _arguments[index + 1].StartsWith("--"))
            {
                throw new ArgumentException($"Option '{name}' requires a value.");
            }

            values.Add(_arguments[index + 1]);
        }

        return values;
    }

    public int Integer(string name, int defaultValue)
    {
        var value = Optional(name);
        return value is null
            ? defaultValue
            : int.TryParse(value, out var parsed)
                ? parsed
                : throw new ArgumentException($"Option '{name}' must be an integer.");
    }
}
