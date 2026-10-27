using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Shared state for one self-test run: the live stage the tests render into and the report sink.
/// </summary>
internal sealed class SelfTestContext
{
    private readonly StringBuilder _report = new();

    public SelfTestContext(Window window, Grid stage)
    {
        Window = window;
        Stage = stage;
    }

    public Window Window { get; }

    /// <summary>
    /// A grid that is part of the live window tree; tests add and remove content here.
    /// </summary>
    public Grid Stage { get; }

    public int PassCount { get; private set; }

    public int FailCount { get; private set; }

    public string CurrentGroup { get; set; } = string.Empty;

    /// <summary>
    /// When set, <see cref="Trace"/> appends to this file immediately, so the last step before a
    /// native crash (which loses the in-memory report) can be recovered.
    /// </summary>
    public string? TracePath { get; set; }

    public void Trace(string message)
    {
        if (TracePath is not null)
        {
            File.AppendAllText(TracePath, message + Environment.NewLine);
        }
    }

    public void Info(string message) => _report.AppendLine($"  [info] {message}");

    public void Check(bool condition, string name, string? detail = null)
    {
        if (condition)
        {
            PassCount++;
            _report.AppendLine($"  [pass] {name}{FormatDetail(detail)}");
        }
        else
        {
            FailCount++;
            _report.AppendLine($"  [FAIL] {name}{FormatDetail(detail)}");
        }
    }

    public void Fail(string name, Exception exception)
    {
        FailCount++;
        _report.AppendLine($"  [FAIL] {name}: {exception.GetType().Name}: {exception.Message}");
    }

    public void BeginGroup(string name)
    {
        CurrentGroup = name;
        _report.AppendLine();
        _report.AppendLine($"== {name}");
    }

    public string BuildReport()
    {
        var header = new StringBuilder();
        header.AppendLine($"Quill.Playground self-test report ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
        header.AppendLine($"Summary: {PassCount} passed, {FailCount} failed");
        return header.ToString() + _report;
    }

    private static string FormatDetail(string? detail) => string.IsNullOrEmpty(detail) ? string.Empty : $" -- {detail}";
}
