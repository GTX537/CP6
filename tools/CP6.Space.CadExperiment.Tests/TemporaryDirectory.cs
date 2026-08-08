namespace CP6.Space.CadExperiment.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cp6-space-cad-experiment-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(
            System.IO.Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException());
        File.WriteAllText(
            fullPath,
            content.ReplaceLineEndings("\n"),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
