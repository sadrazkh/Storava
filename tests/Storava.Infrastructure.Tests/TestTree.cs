namespace Storava.Infrastructure.Tests;

/// <summary>Creates a disposable, deterministic directory tree for scanner tests.</summary>
internal sealed class TestTree : IDisposable
{
    public TestTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "storava-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddFile(string relativePath, int sizeBytes)
    {
        string full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[sizeBytes]);
        return full;
    }

    public string AddDirectory(string relativePath)
    {
        string full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
