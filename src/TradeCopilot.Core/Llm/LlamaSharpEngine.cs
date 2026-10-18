using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TradeCopilot.Core.Llm;

/// <summary>
/// <see cref="ILlmEngine"/> backed by LLamaSharp 0.27 (CPU-only for v1). A missing
/// model or a load failure leaves the engine disabled and callers receive the
/// "Model not available." sentinel; generation is serialized internally so one
/// engine instance serves one request at a time.
/// </summary>
public sealed class LlamaSharpEngine : ILlmEngine, IDisposable
{
    /// <summary>Response (and single streaming token) used while the model is unavailable.</summary>
    public const string NotAvailableMessage = "Model not available.";

    /// <summary>Default GGUF model path, relative to the working directory.</summary>
    public const string DefaultModelPath = "models/qwen2.5-3b-instruct-q4_k_m.gguf";

    private const int ContextSize = 4096;
    private const int MaxNewTokens = 100;

    private static readonly string[] StopStrings = ["<|im_end|>", "<|endoftext|>"];

    private readonly ILogger<LlamaSharpEngine> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private StatelessExecutor? _executor;
    private InferenceParams? _inferenceParams;
    private bool _disposed;

    /// <summary>Creates the engine in a disabled state until <see cref="InitializeAsync"/> succeeds.</summary>
    /// <param name="logger">Logger (defaults to null logger).</param>
    public LlamaSharpEngine(ILogger<LlamaSharpEngine>? logger = null)
    {
        _log = logger ?? NullLogger<LlamaSharpEngine>.Instance;
    }

    /// <inheritdoc />
    public bool Enabled { get; private set; }

    /// <inheritdoc />
    public Task InitializeAsync(string modelPath, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        if (_weights is not null)
        {
            return Task.CompletedTask;
        }

        if (!File.Exists(modelPath))
        {
            _log.LogWarning("LLM model not found at {ModelPath}; LLM disabled.", modelPath);
            return Task.CompletedTask;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var modelParams = new ModelParams(modelPath)
            {
                ContextSize = ContextSize,
                GpuLayerCount = 0,
                Threads = Math.Max(1, Environment.ProcessorCount - 1),
            };
            _weights = LLamaWeights.LoadFromFile(modelParams);
            _context = new LLamaContext(_weights, modelParams, _log);
            _executor = new StatelessExecutor(_weights, modelParams, _log) { ApplyTemplate = false };
            _inferenceParams = new InferenceParams
            {
                MaxTokens = MaxNewTokens,
                AntiPrompts = StopStrings,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.7f,
                    TopK = 40,
                    TopP = 0.9f,
                    RepeatPenalty = 1.1f,
                },
            };
            stopwatch.Stop();
            Enabled = true;
            _log.LogInformation(
                "LLM model loaded from {ModelPath} in {ElapsedMs} ms (working set {WorkingSetMb} MB).",
                modelPath,
                stopwatch.ElapsedMilliseconds,
                Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load LLM model from {ModelPath}; LLM disabled.", modelPath);
            DisposeModel();
            Enabled = false;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enabled || _executor is null || _inferenceParams is null)
        {
            return NotAvailableMessage;
        }

        string prompt = PromptBuilder.BuildChatPrompt(system, user);
        var builder = new StringBuilder();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await foreach (string token in _executor
                .InferAsync(prompt, _inferenceParams, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                builder.Append(token);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "LLM completion failed.");
            return builder.Length == 0 ? NotAvailableMessage : builder.ToString();
        }
        finally
        {
            _gate.Release();
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        string system,
        string user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enabled || _executor is null || _inferenceParams is null)
        {
            yield return NotAvailableMessage;
            yield break;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string prompt = PromptBuilder.BuildChatPrompt(system, user);
            await foreach (string token in _executor
                .InferAsync(prompt, _inferenceParams, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                yield return token;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeModel();
        _gate.Dispose();
    }

    private void DisposeModel()
    {
        _executor = null;
        _inferenceParams = null;
        _context?.Dispose();
        _context = null;
        _weights?.Dispose();
        _weights = null;
    }

}
