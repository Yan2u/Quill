using System.Collections.Concurrent;
using Quill.Settings.Taskbar;

namespace Quill.Settings.Tests;

/// <summary>Per-executable instance keys and named-pipe launch forwarding tests.</summary>
public sealed class SingleInstanceBridgeTests
{
    private static string UniquePipeName() => $"Quill.Test.{Guid.NewGuid():N}";

    [Fact]
    public void ComputeInstanceKey_IsCaseInsensitiveAndDistinguishesDifferentPaths()
    {
        string debugKeyLower = SingleInstanceBridge.ComputeInstanceKey(@"c:\build\debug\Quill.exe");
        string debugKeyUpper = SingleInstanceBridge.ComputeInstanceKey(@"C:\BUILD\DEBUG\QUILL.EXE");
        string releaseKey = SingleInstanceBridge.ComputeInstanceKey(@"C:\build\Release\Quill.exe");

        Assert.Equal(16, debugKeyLower.Length);
        Assert.Equal(debugKeyLower, debugKeyUpper);
        Assert.NotEqual(debugKeyLower, releaseKey);
        Assert.Equal($"Quill.Instance.{debugKeyLower}", SingleInstanceBridge.PipeNameForKey(debugKeyLower));
        Assert.Equal($"Local\\Quill.Instance.{debugKeyLower}", SingleInstanceBridge.MutexNameForKey(debugKeyLower));
    }

    [Fact]
    public void TryForwardToPipe_DeliversRequestsWithUnicodeAndDriveRoot()
    {
        string pipeName = UniquePipeName();
        using var server = SingleInstanceServer.StartForPipe(pipeName);

        var received = new ConcurrentQueue<LaunchRequest>();
        using var signal = new CountdownEvent(3);
        server.SetHandler(req =>
        {
            received.Enqueue(req);
            signal.Signal();
        });

        var req1 = new LaunchRequest(LaunchActionKind.OpenFile, @"C:\Users\19559\Desktop\补习速记.md");
        var req2 = new LaunchRequest(LaunchActionKind.OpenFolder, @"C:\");
        var req3 = new LaunchRequest(LaunchActionKind.NewWindow);

        Assert.True(SingleInstanceBridge.TryForwardToPipe(pipeName, req1, timeoutMs: 2000));
        Assert.True(SingleInstanceBridge.TryForwardToPipe(pipeName, req2, timeoutMs: 2000));
        Assert.True(SingleInstanceBridge.TryForwardToPipe(pipeName, req3, timeoutMs: 2000));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Equal([req1, req2, req3], received.ToArray());
    }

    [Fact]
    public void Server_BuffersRequestsReceivedBeforeSetHandler()
    {
        string pipeName = UniquePipeName();
        using var server = SingleInstanceServer.StartForPipe(pipeName);

        var earlyFile = new LaunchRequest(LaunchActionKind.OpenFile, @"C:\notes\early.md");
        var earlyFolder = new LaunchRequest(LaunchActionKind.OpenFolder, @"C:\notes");

        Assert.True(SingleInstanceBridge.TryForwardToPipe(pipeName, earlyFile, timeoutMs: 2000));
        Assert.True(SingleInstanceBridge.TryForwardToPipe(pipeName, earlyFolder, timeoutMs: 2000));

        var drained = new List<LaunchRequest>();
        server.SetHandler(drained.Add);

        Assert.Equal([earlyFile, earlyFolder], drained);
    }

    [Fact]
    public void TryForwardToPipe_ReturnsFalsePromptlyWhenNoServerListening()
    {
        string pipeName = UniquePipeName();
        bool forwarded = SingleInstanceBridge.TryForwardToPipe(
            pipeName,
            new LaunchRequest(LaunchActionKind.NewWindow),
            timeoutMs: 120);

        Assert.False(forwarded);
    }

    [Fact]
    public async Task Server_HandlesConcurrentForwardBurst()
    {
        string pipeName = UniquePipeName();
        using var server = SingleInstanceServer.StartForPipe(pipeName);

        const int total = 8;
        var received = new ConcurrentBag<string?>();
        using var allReceived = new CountdownEvent(total);
        server.SetHandler(req =>
        {
            received.Add(req.TargetPath);
            allReceived.Signal();
        });

        var expectedPaths = Enumerable.Range(0, total)
            .Select(i => $@"C:\burst\file-{i}.md")
            .ToArray();

        var tasks = expectedPaths.Select(path => Task.Run(
            () => SingleInstanceBridge.TryForwardToPipe(
                pipeName,
                new LaunchRequest(LaunchActionKind.OpenFile, path),
                timeoutMs: 3000),
            TestContext.Current.CancellationToken));

        bool[] results = await Task.WhenAll(tasks);

        Assert.All(results, Assert.True);
        Assert.True(allReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(expectedPaths.OrderBy(static x => x), received.OrderBy(static x => x));
    }
}
