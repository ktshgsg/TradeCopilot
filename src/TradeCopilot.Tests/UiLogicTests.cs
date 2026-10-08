using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeCopilot.UI.Logic;
using TradeCopilot.UI.Logic.Models;
using TradeCopilot.UI.Logic.Services;
using TradeCopilot.UI.Logic.ViewModels;

namespace TradeCopilot.Tests;

/// <summary>Tests for the named-pipe client and the overlay view model (UI.Logic, no WPF).</summary>
public sealed class UiLogicTests
{
    private static string NewPipeName() => "TradeCopilot.Test." + Guid.NewGuid().ToString("N");

    /// <summary>Runs a one-shot pipe server that reads one request line and writes one response line.</summary>
    private static Task RunEchoServerAsync(string pipeName, string responseLine, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

            using var request = new MemoryStream();
            var chunk = new byte[4096];
            while (request.Length < 65536)
            {
                int read = await server.ReadAsync(chunk, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                int newline = Array.IndexOf(chunk, (byte)'\n', 0, read);
                if (newline >= 0)
                {
                    request.Write(chunk, 0, newline);
                    break;
                }

                request.Write(chunk, 0, read);
            }

            byte[] payload = Encoding.UTF8.GetBytes(responseLine + "\n");
            await server.WriteAsync(payload, ct).ConfigureAwait(false);
            await server.FlushAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <summary>A live pipe server round-trip returns the echoed status response.</summary>
    [Fact]
    public async Task PipeClient_SendAsync_ReturnsServerResponse()
    {
        string pipeName = NewPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task server = RunEchoServerAsync(
            pipeName,
            """{"type":"status","captureActive":true,"version":"0.1.0"}""",
            cts.Token);

        var client = new PipeClient(
            new UiOptions { PipeName = pipeName },
            NullLogger<PipeClient>.Instance);

        PipeResponse resp = await client.SendAsync(new PipeRequest("status"), cts.Token);

        Assert.Equal("status", resp.Type);
        Assert.True(resp.CaptureActive);
        Assert.Equal("0.1.0", resp.Version);
        await server;
    }

    /// <summary>With no server listening, SendAsync returns an error response instead of throwing.</summary>
    [Fact]
    public async Task PipeClient_ServerNotRunning_ReturnsErrorWithoutThrowing()
    {
        var client = new PipeClient(
            new UiOptions { PipeName = NewPipeName() },
            NullLogger<PipeClient>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        PipeResponse resp = await client.SendAsync(new PipeRequest("status"), cts.Token);

        Assert.Equal("error", resp.Type);
        Assert.False(string.IsNullOrWhiteSpace(resp.Message));
    }

    /// <summary>Sending "hello" appends the user bubble and the mocked assistant answer.</summary>
    [Fact]
    public async Task MainViewModel_SendAsync_AddsUserAndAssistantMessages()
    {
        var pipe = Substitute.For<IPipeClient>();
        pipe.SendAsync(Arg.Any<PipeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PipeResponse { Type = "answer", Text = "RSI 72 pe hai.", Source = "rule", ElapsedMs = 42 });

        var vm = new MainViewModel(pipe, NullLogger<MainViewModel>.Instance)
        {
            InputText = "hello",
        };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal(MessageRole.User, vm.Messages[0].Role);
        Assert.Equal("hello", vm.Messages[0].Text);
        Assert.Equal(MessageRole.Assistant, vm.Messages[1].Role);
        Assert.Equal("RSI 72 pe hai.", vm.Messages[1].Text);
        Assert.Equal("rule", vm.Messages[1].Source);
        Assert.Equal(42, vm.Messages[1].ElapsedMs);
    }

    /// <summary>Whitespace-only input adds no messages and never touches the pipe.</summary>
    [Fact]
    public async Task MainViewModel_SendAsync_EmptyInput_AddsNoMessages()
    {
        var pipe = Substitute.For<IPipeClient>();
        var vm = new MainViewModel(pipe, NullLogger<MainViewModel>.Instance)
        {
            InputText = "   ",
        };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Empty(vm.Messages);
        await pipe.DidNotReceive().SendAsync(Arg.Any<PipeRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A status reply updates the capture, version, and status bound properties.</summary>
    [Fact]
    public async Task MainViewModel_RefreshStatusAsync_UpdatesBoundProperties()
    {
        var pipe = Substitute.For<IPipeClient>();
        pipe.SendAsync(Arg.Any<PipeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PipeResponse { Type = "status", CaptureActive = true, Version = "0.1.0" });

        var vm = new MainViewModel(pipe, NullLogger<MainViewModel>.Instance);

        await vm.RefreshStatusAsync();

        Assert.True(vm.CaptureActive);
        Assert.Equal("v0.1.0", vm.VersionText);
        Assert.Equal("Capturing", vm.StatusText);
    }
}
