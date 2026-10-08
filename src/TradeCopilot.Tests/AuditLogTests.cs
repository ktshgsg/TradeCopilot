using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Privacy;

namespace TradeCopilot.Tests;

/// <summary>Task 5 stage 3b tests: the append-only content-free audit log.</summary>
public sealed class AuditLogTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc-audit-" + Guid.NewGuid().ToString("N"));

    private string NewPath()
    {
        Directory.CreateDirectory(_dir);
        return Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".log");
    }

    /// <summary>100 writes produce 100 parseable JSON lines with ts, evt, and meta.</summary>
    [Fact]
    public async Task Write_100Events_AllLinesParse()
    {
        string path = NewPath();
        await using (var log = new AuditLog(path, NullLogger<AuditLog>.Instance))
        {
            for (int i = 0; i < 100; i++)
            {
                log.Write("AnswerSource", $"source=rule,ms={i}");
            }
        }

        string[] lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(100);
        foreach (string line in lines)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            doc.RootElement.TryGetProperty("ts", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("evt", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
        }
    }

    /// <summary>Meta longer than 512 chars is truncated but stays valid JSON.</summary>
    [Fact]
    public async Task Write_LongMeta_TruncatedTo512()
    {
        string path = NewPath();
        await using (var log = new AuditLog(path, NullLogger<AuditLog>.Instance))
        {
            log.Write("AnswerSource", new string('x', 2000));
        }

        string line = (await File.ReadAllLinesAsync(path))[0];
        using JsonDocument doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("meta").GetString()!.Length.Should().Be(512);
    }

    /// <summary>Concurrent writers produce complete, non-interleaved lines.</summary>
    [Fact]
    public async Task Write_Concurrent_AllLinesPresent()
    {
        string path = NewPath();
        await using (var log = new AuditLog(path, NullLogger<AuditLog>.Instance))
        {
            var tasks = new List<Task>();
            for (int t = 0; t < 10; t++)
            {
                int thread = t;
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        log.Write("Query", $"thread={thread},i={i}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }

        string[] lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(100);
        foreach (string line in lines)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("evt").GetString().Should().Be("Query");
        }
    }

    /// <summary>Writing after disposal is a no-op that never throws.</summary>
    [Fact]
    public async Task Write_AfterDispose_DoesNotThrow()
    {
        string path = NewPath();
        var log = new AuditLog(path, NullLogger<AuditLog>.Instance);
        await log.DisposeAsync();

        Action write = () => log.Write("Panic", "source=pipe");
        write.Should().NotThrow();
    }
}
