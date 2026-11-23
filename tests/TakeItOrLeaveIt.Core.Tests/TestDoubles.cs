using System;
using System.Collections.Generic;
using System.IO;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Environment;

namespace TakeItOrLeaveIt.Core.Tests;

internal sealed class FakeFileProbe : IFileProbe
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileProbe File(params string[] relativeSegments)
    {
        _files.Add(Path.Combine(Root, Path.Combine(relativeSegments)));
        return this;
    }

    public FakeFileProbe Dir(params string[] relativeSegments)
    {
        _dirs.Add(Path.Combine(Root, Path.Combine(relativeSegments)));
        return this;
    }

    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "fake-schedule-i");

    public bool FileExists(string path) => _files.Contains(path);

    public bool DirectoryExists(string path) => _dirs.Contains(path);
}

internal sealed class RecordingLog : IModLog
{
    public List<string> Infos { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<(string Message, Exception? Exception)> Errors { get; } = new();

    public void Info(string message) => Infos.Add(message);

    public void Warning(string message) => Warnings.Add(message);

    public void Error(string message) => Errors.Add((message, null));

    public void Error(string message, Exception exception) => Errors.Add((message, exception));
}
