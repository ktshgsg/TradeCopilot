using Microsoft.Extensions.Options;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Embeddings;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Privacy;
using TradeCopilot.Core.Processing;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Speech;
using TradeCopilot.Core.Vision;
using TradeCopilot.Service;

var builder = Host.CreateApplicationBuilder(args);

// Console logging only: no file sinks. The installer can redirect stdout/stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddWindowsService(o => o.ServiceName = "TradeCopilot");

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgentOptions>>().Value);

// Capture: the DXGI duplication surface is fixed at construction, so the configured
// region and FPS are supplied here.
builder.Services.AddSingleton<IScreenCapture>(sp => new DxgiScreenCapture(
    sp.GetRequiredService<AgentOptions>().Capture.Region(),
    sp.GetRequiredService<AgentOptions>().Capture.Fps,
    sp.GetRequiredService<ILogger<DxgiScreenCapture>>()));
builder.Services.AddSingleton(sp => new FrameGate());

builder.Services.AddSingleton<IOcrEngine>(sp => new WindowsOcrEngine(
    sp.GetRequiredService<ILogger<WindowsOcrEngine>>()));

// YOLO is wired later; the pipeline must not depend on the ONNX file being present.
builder.Services.AddSingleton<IChartPatternDetector>(_ => DisabledChartPatternDetector.Instance);

// OnnxEmbeddingProvider is wired later; the cache must not depend on the model file.
builder.Services.AddSingleton<IEmbeddingProvider>(_ => new FakeEmbeddingProvider());

builder.Services.AddSingleton<ISemanticCache>(sp => new SqliteSemanticCache(
    sp.GetRequiredService<IEmbeddingProvider>(),
    sp.GetRequiredService<ILogger<SqliteSemanticCache>>(),
    sp.GetRequiredService<AgentOptions>().Privacy.EncryptCache));

// IRule is internal to Core, so the public default-rules constructor is used; it wires
// all 11 built-in rules in evaluation order.
builder.Services.AddSingleton<IRuleEngine>(sp => new TradingRuleEngine(
    sp.GetRequiredService<ILogger<TradingRuleEngine>>()));

builder.Services.AddSingleton<ILlmEngine>(sp => new LlamaSharpEngine(
    sp.GetRequiredService<ILogger<LlamaSharpEngine>>()));

// Vision: Moondream via a Python worker process. A missing worker, interpreter, or
// model simply disables vision at startup (Enabled=false) instead of failing.
builder.Services.AddSingleton<IVisionEngine, MoondreamVisionEngine>();
builder.Services.AddSingleton<ISpeechRecognizer>(sp => new WhisperSpeechRecognizer(
    sp.GetRequiredService<ILogger<WhisperSpeechRecognizer>>()));
builder.Services.AddSingleton<ISpeechSynthesizer>(sp => new PiperSpeechSynthesizer(
    sp.GetRequiredService<ILogger<PiperSpeechSynthesizer>>()));
builder.Services.AddSingleton<AudioCapture>(sp => new AudioCapture(
    sp.GetRequiredService<ILogger<AudioCapture>>()));

builder.Services.AddSingleton<IWindowFilter>(_ => new WindowFilter());
builder.Services.AddSingleton<IAuditSink>(sp =>
    new AuditLog(
        Path.Combine(AppContext.BaseDirectory, "data", "audit.log"),
        sp.GetRequiredService<ILogger<AuditLog>>()));
builder.Services.AddSingleton(sp => sp.GetRequiredService<AgentOptions>().Privacy);
builder.Services.AddSingleton<PrivacyGateCounters>();

builder.Services.AddSingleton<FrameProcessor>(sp => new FrameProcessor(
    sp.GetRequiredService<FrameGate>(),
    sp.GetRequiredService<IOcrEngine>(),
    sp.GetRequiredService<IChartPatternDetector>(),
    sp.GetRequiredService<ILogger<FrameProcessor>>()));

builder.Services.AddSingleton<AgentState>();
builder.Services.AddSingleton<AgentOrchestrator>(sp => new AgentOrchestrator(
    sp.GetRequiredService<AgentState>(),
    sp.GetRequiredService<ISemanticCache>(),
    sp.GetRequiredService<IRuleEngine>(),
    sp.GetRequiredService<ILlmEngine>(),
    sp.GetRequiredService<IVisionEngine>(),
    sp.GetRequiredService<ILogger<AgentOrchestrator>>()));

builder.Services.AddHostedService<StartupHostedService>();
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<CacheMaintenanceService>();

var host = builder.Build();
await host.RunAsync();
