using Microsoft.Extensions.Options;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Embeddings;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Privacy;
using TradeCopilot.Core.Processing;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Setup;
using TradeCopilot.Core.Speech;
using TradeCopilot.Core.Vision;
using TradeCopilot.Service;

// Windows services start with Working Directory = C:\Windows\System32 (the SCM
// default), which would make appsettings.json, models/, data/, and tools/
// resolve against System32 and crash the service instantly on Start-Service.
// Anchor the working directory to the binaries before anything else runs; the
// dev-layout detection below then re-roots to the solution checkout when present.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// The service runs as NT AUTHORITY\LocalService, whose LocalApplicationData is
// the service profile (C:\Windows\ServiceProfiles\LocalService\AppData\Local),
// not the installing user's. The installer writes TRADECOPILOT_HOME to the
// service's Environment key pointing at the user's %LOCALAPPDATA%\TradeCopilot;
// rely on it so model/data paths resolve to a shared, writable location.
var customHome = Environment.GetEnvironmentVariable("TRADECOPILOT_HOME");
if (!string.IsNullOrEmpty(customHome))
{
    Environment.SetEnvironmentVariable("TRADECOPILOT_HOME", customHome);
}

var builder = Host.CreateApplicationBuilder(args);

// Resolve content root: the solution root in a dev layout (so run.ps1 keeps
// working from any subfolder), AppContext.BaseDirectory in production (the
// published/installed layout, where no TradeCopilot.sln exists above the
// binaries). Model files are NOT resolved here — ModelManifest maps them to
// %LOCALAPPDATA%\TradeCopilot\models outside a dev layout.
string? solutionRoot = FindSolutionRoot(AppContext.BaseDirectory)
    ?? FindSolutionRoot(Environment.CurrentDirectory);
bool isDevLayout = solutionRoot is not null;
string rootPath = solutionRoot ?? AppContext.BaseDirectory;
builder.Environment.ContentRootPath = rootPath;
Environment.CurrentDirectory = rootPath;
if (!isDevLayout)
{
    // Production: relative model/data paths from appsettings must resolve to the
    // per-user directory — Program Files is not writable without elevation, and the
    // first-run wizard downloads into %LOCALAPPDATA%\TradeCopilot\models. Piper's
    // executable stays install-relative (tools/piper ships next to the binaries and
    // resolves against the working directory set above).
    string userData = ModelManifest.UserDataDirectory;
    Directory.CreateDirectory(Path.Combine(userData, "models"));
    Directory.CreateDirectory(Path.Combine(userData, "data"));
    builder.Services.PostConfigure<AgentOptions>(options =>
    {
        options.Models.EmbeddingPath = RebasePath(options.Models.EmbeddingPath, userData);
        options.Models.LlmPath = RebasePath(options.Models.LlmPath, userData);
        options.Models.WhisperPath = RebasePath(options.Models.WhisperPath, userData);
        options.Models.PiperVoicePath = RebasePath(options.Models.PiperVoicePath, userData);
        options.Cache.DbPath = RebasePath(options.Cache.DbPath, userData);
    });
}

// Models a relative appsettings path (e.g. "models/x.gguf") against the per-user
// directory; absolute paths pass through unchanged.
static string RebasePath(string path, string baseDirectory) =>
    Path.IsPathRooted(path)
        ? path
        : Path.Combine(baseDirectory, path.Replace('/', Path.DirectorySeparatorChar));

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

// Classical CV chart analyzer (default chart reader; Moondream2 stays opt-in).
builder.Services.AddSingleton(sp => new PriceAxisMapper(
    sp.GetRequiredService<ILogger<PriceAxisMapper>>()));
builder.Services.AddSingleton<IChartAnalyzer, ChartAnalyzer>();

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

static string? FindSolutionRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "TradeCopilot.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}
