namespace TradeCopilot.Core.Setup;

/// <summary>
/// One downloadable model asset: where it lives on Hugging Face, where it lands on disk,
/// and how to verify it. TargetPath is relative to the app root (the repository root in
/// the dev layout) unless an absolute path is supplied.
/// </summary>
/// <param name="Name">Display name shown in the first-run wizard.</param>
/// <param name="Url">Direct HTTPS download URL.</param>
/// <param name="TargetPath">Destination path, relative to the app root (e.g. <c>models/x.gguf</c>).</param>
/// <param name="ExpectedSize">Approximate size in bytes used for progress before the server reports a length; 0 when unknown.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the complete file; empty when not pinned yet (verification is skipped).</param>
public sealed record ModelFile(string Name, string Url, string TargetPath, long ExpectedSize, string Sha256);

/// <summary>
/// The model files TradeCopilot needs. <see cref="Required"/> files back the LLM, ASR, and
/// TTS engines; when any of them is missing the app runs in degraded mode (rules + cache
/// only) instead of failing. Paths match <c>appsettings.json</c> (<c>Models:*</c>).
/// </summary>
public static class ModelManifest
{
    private const string RootMarkerFile = "TradeCopilot.sln";
    private const string SecondaryRootMarkerFile = "setup.ps1";

    /// <summary>Models the app cannot serve full answers without (LLM, ASR, TTS).</summary>
    public static IReadOnlyList<ModelFile> Required { get; } = new ModelFile[]
    {
        new(
            "Qwen2.5-1.5B-Instruct (chat model)",
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            "models/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            1_176_000_000,
            string.Empty),
        new(
            "Whisper base (speech recognition)",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            "models/ggml-base.bin",
            148_000_000,
            string.Empty),
        new(
            "Piper Hindi voice (text-to-speech)",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx",
            "models/piper/hi_IN-rohan-medium.onnx",
            63_000_000,
            string.Empty),
        new(
            "Piper Hindi voice config",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx.json",
            "models/piper/hi_IN-rohan-medium.onnx.json",
            5_000,
            string.Empty),
    };

    /// <summary>Extras that improve quality but are not needed for degraded mode to end; none yet.</summary>
    public static IReadOnlyList<ModelFile> Optional { get; } = Array.Empty<ModelFile>();
    /// <summary>
    /// The root folder that relative TargetPath values resolve against: the working
    /// directory when it is the app root (where <c>run.ps1</c> starts both processes),
    /// otherwise the nearest folder at or above the working directory or the executable
    /// that contains the solution or setup script. Falls back to the working directory
    /// for layouts without either marker (e.g. a future install dir).
    /// </summary>
    public static string DefaultBaseDirectory =>
        FindAppRoot(Environment.CurrentDirectory)
        ?? FindAppRoot(AppContext.BaseDirectory)
        ?? Environment.CurrentDirectory;

    /// <summary>Resolves a manifest path against an app root (absolute paths pass through).</summary>
    /// <param name="file">Manifest entry.</param>
    /// <param name="baseDirectory">App root.</param>
    /// <returns>Absolute path of the target file.</returns>
    public static string ResolvePath(ModelFile file, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return Path.IsPathRooted(file.TargetPath)
            ? file.TargetPath
            : Path.Combine(baseDirectory, file.TargetPath);
    }

    /// <summary>Whether the file for <paramref name="file"/> exists on disk.</summary>
    /// <param name="file">Manifest entry.</param>
    /// <param name="baseDirectory">App root.</param>
    /// <returns>True when the target file exists.</returns>
    public static bool IsPresent(ModelFile file, string baseDirectory) =>
        File.Exists(ResolvePath(file, baseDirectory));

    /// <summary>Required files whose target file is missing under <paramref name="baseDirectory"/>.</summary>
    /// <param name="baseDirectory">App root.</param>
    /// <returns>Missing required entries, in manifest order.</returns>
    public static IReadOnlyList<ModelFile> Missing(string baseDirectory) =>
        Required.Where(f => !IsPresent(f, baseDirectory)).ToArray();

    /// <summary>Whether every required file is present.</summary>
    /// <param name="baseDirectory">App root.</param>
    /// <returns>True when nothing is missing.</returns>
    public static bool AllPresent(string baseDirectory) => Missing(baseDirectory).Count == 0;

    /// <summary>The engine a required file feeds, for degraded-mode log lines.</summary>
    /// <param name="file">Manifest entry.</param>
    /// <returns><c>LLM</c>, <c>ASR</c>, or <c>TTS</c>.</returns>
    public static string EngineName(ModelFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.TargetPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            return "LLM";
        }

        return file.TargetPath.Contains("ggml", StringComparison.OrdinalIgnoreCase) ? "ASR" : "TTS";
    }

    /// <summary>Walks up from <paramref name="start"/> looking for a directory marker of the app root.</summary>
    /// <param name="start">Directory to start from.</param>
    /// <returns>The marker directory, or null when none is found.</returns>
    private static string? FindAppRoot(string start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RootMarkerFile)) ||
                File.Exists(Path.Combine(directory.FullName, SecondaryRootMarkerFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}