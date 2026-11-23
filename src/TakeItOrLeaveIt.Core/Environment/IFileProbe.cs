namespace TakeItOrLeaveIt.Core.Environment;

/// <summary>Read-only file-system queries. Abstracted so runtime detection is unit-testable.</summary>
public interface IFileProbe
{
    bool FileExists(string path);

    bool DirectoryExists(string path);
}

/// <summary><see cref="IFileProbe"/> backed by the real file system (read-only).</summary>
public sealed class PhysicalFileProbe : IFileProbe
{
    public static readonly PhysicalFileProbe Instance = new();

    private PhysicalFileProbe()
    {
    }

    public bool FileExists(string path) => System.IO.File.Exists(path);

    public bool DirectoryExists(string path) => System.IO.Directory.Exists(path);
}
