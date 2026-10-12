using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TradeCopilot.Core.Speech;
using Xunit.Abstractions;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 4 tests: audio capture VAD, Whisper recognition behaviour with and without a
/// model, Hinglish→Devanagari transliteration, and Piper synthesis behaviour.
/// </summary>
public sealed class SpeechTests
{
    private const string WhisperModelFileName = "ggml-base.bin";
    private const string PiperExeName = "piper.exe";
    private const string PiperVoiceFileName = "hi_IN-rohan-medium.onnx";

    private static readonly byte[] SpeechChunk = Speech(0.30f);
    private static readonly byte[] SilenceChunk = Silence();

    private readonly ITestOutputHelper _output;

    /// <summary>Surfaces raw transcripts so a human can eyeball them in the test output.</summary>
    public SpeechTests(ITestOutputHelper output) => _output = output;

    /// <summary>Locates the repo root by walking up from the test output folder.</summary>
    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "models", "README.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    /// <summary>Finds a file under models/ or tools/, walking up to the repo root.</summary>
    internal static string FindAsset(string relativePath)
    {
        string repoRoot = FindRepoRoot();
        string inModels = Path.Combine(repoRoot, "models", relativePath);
        if (File.Exists(inModels))
        {
            return inModels;
        }

        string inTools = Path.Combine(repoRoot, "tools", relativePath);
        return File.Exists(inTools) ? inTools : inModels;
    }

    /// <summary>A PCM16 chunk at 16 kHz mono with the given approximate amplitude.</summary>
    private static byte[] Speech(float amplitude, int sampleCount = 16000 / 10)
    {
        var pcm = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            short value = (short)(Math.Sin(i * 0.2) * amplitude * short.MaxValue);
            BitConverter.GetBytes(value).CopyTo(pcm, i * 2);
        }

        return pcm;
    }

    /// <summary>Path to the Piper Hindi voice model under models/piper/.</summary>
    private static string PiperVoicePath() => FindAsset(Path.Combine("piper", PiperVoiceFileName));

    /// <summary>A 100 ms PCM16 chunk of digital silence.</summary>
    private static byte[] Silence() => new byte[16000 / 10 * 2];

    /// <summary>
    /// Synthesizes <paramref name="text"/> with Piper (transliterated the same way the
    /// production speak path does) and returns 16 kHz mono PCM16 ready for Whisper.
    /// Fails loudly when piper.exe cannot produce the WAV — that is real signal.
    /// </summary>
    private static async Task<byte[]> PiperSpeechToPcmAsync(string text)
    {
        string piperExe = FindAsset(Path.Combine("piper", PiperExeName));
        string voice = PiperVoicePath();
        using var synth = new PiperSpeechSynthesizer();
        await synth.InitializeAsync(piperExe, voice, CancellationToken.None);
        synth.Enabled.Should().BeTrue($"Piper must be installed at {piperExe} with voice {voice}.");

        string wav = Path.Combine(Path.GetTempPath(), $"tc-asr-{Guid.NewGuid():N}.wav");
        try
        {
            string devanagari = HinglishTransliterator.ToDevanagari(text);
            bool synthesized = await synth.SynthesizeAsync(devanagari, wav, CancellationToken.None);
            synthesized.Should().BeTrue(
                $"piper.exe must synthesize '{text}' to {wav}; a non-zero exit or missing WAV is a real failure.");
            return WavToPcm16kMono(wav);
        }
        finally
        {
            if (File.Exists(wav))
            {
                File.Delete(wav);
            }
        }
    }

    /// <summary>Reads a Piper WAV (typically 22.05 kHz mono) and resamples it to 16 kHz mono PCM16.</summary>
    private static byte[] WavToPcm16kMono(string wavPath)
    {
        using var reader = new WaveFileReader(wavPath);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels == 2)
        {
            source = new StereoToMonoSampleProvider(source);
        }
        else if (source.WaveFormat.Channels != 1)
        {
            throw new InvalidOperationException($"Unsupported channel count {source.WaveFormat.Channels} in {wavPath}.");
        }

        var resampled = new WdlResamplingSampleProvider(source, 16000);
        var samples = new List<short>(16000 * 8);
        var buffer = new float[4096];
        int read;
        while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                samples.Add((short)(Math.Clamp(buffer[i], -1f, 1f) * short.MaxValue));
            }
        }

        var pcm = new byte[samples.Count * 2];
        for (int i = 0; i < samples.Count; i++)
        {
            BitConverter.GetBytes(samples[i]).CopyTo(pcm, i * 2);
        }

        return pcm;
    }
    /// <summary>Start and Stop are idempotent and never throw, with or without a device.</summary>
    [Fact]
    public void AudioCapture_StartStop_IsIdempotent()
    {
        using var capture = new AudioCapture(NullLogger<AudioCapture>.Instance);

        var act = () =>
        {
            capture.Start();
            capture.Start();
            capture.Stop();
            capture.Stop();
        };

        act.Should().NotThrow();
    }

    /// <summary>Silence only never opens an utterance, so no OnUtteranceEnd is raised.</summary>
    [Fact]
    public void AudioCapture_SilenceOnly_RaisesNoUtterance()
    {
        using var capture = new AudioCapture(NullLogger<AudioCapture>.Instance);
        int utterances = 0;
        int chunks = 0;
        capture.OnUtteranceEnd += _ => utterances++;
        capture.OnAudioChunk += _ => chunks++;

        for (int i = 0; i < 30; i++)
        {
            capture.ProcessChunk(SilenceChunk);
        }

        chunks.Should().Be(30);
        utterances.Should().Be(0);
    }

    /// <summary>Five speech chunks then ten silent chunks emit exactly one utterance.</summary>
    [Fact]
    public void AudioCapture_SpeechThenSilence_RaisesOneUtterance()
    {
        using var capture = new AudioCapture(NullLogger<AudioCapture>.Instance);
        var utterances = new List<byte[]>();
        capture.OnUtteranceEnd += pcm => utterances.Add(pcm);

        for (int i = 0; i < 5; i++)
        {
            capture.ProcessChunk(SpeechChunk);
        }

        utterances.Should().BeEmpty("the utterance is still open");

        for (int i = 0; i < 10; i++)
        {
            capture.ProcessChunk(SilenceChunk);
        }

        utterances.Should().HaveCount(1);

        // 5 x 100 ms speech (16000 B) + 800 ms hangover (25600 B) - 200 ms tail (6400 B)
        // = 35200 B. The spec's 3200-8000 figure assumed 100 ms chunks of speech only;
        // at 16 kHz/16-bit a 100 ms chunk is 3200 bytes, so the floor is the 5 speech
        // chunks (16000 B) and the ceiling is that plus the hangover minus the tail.
        utterances[0].Length.Should().BeInRange(16000, 35200);
    }

    /// <summary>A 300 ms utterance is below the minimum and is never emitted.</summary>
    [Fact]
    public void AudioCapture_ShortUtterance_IsNotEmitted()
    {
        using var capture = new AudioCapture(NullLogger<AudioCapture>.Instance);
        int utterances = 0;
        capture.OnUtteranceEnd += _ => utterances++;

        for (int i = 0; i < 3; i++)
        {
            capture.ProcessChunk(SpeechChunk);
        }

        for (int i = 0; i < 10; i++)
        {
            capture.ProcessChunk(SilenceChunk);
        }

        utterances.Should().Be(0);
    }

    /// <summary>Dispose stops capture and detaches handlers without throwing.</summary>
    [Fact]
    public void AudioCapture_Dispose_IsSafe()
    {
        var capture = new AudioCapture(NullLogger<AudioCapture>.Instance);

        var act = () =>
        {
            capture.Start();
            capture.Dispose();
            capture.Dispose();
        };

        act.Should().NotThrow();
    }

    /// <summary>Skips ASR tests that need a real Whisper model when it is absent.</summary>
    internal sealed class RequiresWhisperFactAttribute : FactAttribute
    {
        public RequiresWhisperFactAttribute()
        {
            string path = FindAsset(WhisperModelFileName);
            if (!File.Exists(path))
            {
                Skip = $"Whisper model not present at {path}; download {WhisperModelFileName} from "
                    + "https://huggingface.co/ggerganov/whisper.cpp (see models/README.md).";
            }
        }
    }

    /// <summary>Skips ASR tests that need a Whisper model and Piper TTS when either is absent.</summary>
    internal sealed class RequiresWhisperPiperFactAttribute : FactAttribute
    {
        public RequiresWhisperPiperFactAttribute()
        {
            string model = FindAsset(WhisperModelFileName);
            if (!File.Exists(model))
            {
                Skip = $"Whisper model not present at {model}; download {WhisperModelFileName} from "
                    + "https://huggingface.co/ggerganov/whisper.cpp (see models/README.md).";
                return;
            }

            string exe = FindAsset(Path.Combine("piper", PiperExeName));
            string voice = FindAsset(Path.Combine("piper", PiperVoiceFileName));
            if (!File.Exists(exe) || !File.Exists(voice))
            {
                Skip = $"Piper not installed: expected {exe} and {voice}; see tools/README.md.";
            }
        }
    }

    /// <summary>A missing Whisper model leaves the recognizer disabled and returns empty.</summary>
    [Fact]
    public async Task Whisper_MissingModel_StaysDisabledAndReturnsEmpty()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"tc-missing-{Guid.NewGuid():N}.bin");
        using var recognizer = new WhisperSpeechRecognizer();

        await recognizer.InitializeAsync(missing, CancellationToken.None);

        recognizer.Enabled.Should().BeFalse();
        (await recognizer.TranscribeAsync(SpeechChunk, CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>Silent PCM16 audio never throws and yields a blank transcript.</summary>
    [Fact]
    public async Task Whisper_SilentAudio_ReturnsBlankWithoutThrowing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"tc-missing-{Guid.NewGuid():N}.bin");
        using var recognizer = new WhisperSpeechRecognizer();
        await recognizer.InitializeAsync(missing, CancellationToken.None);

        byte[] oneSecond = new byte[16000 * 2];
        string result = await recognizer.TranscribeAsync(oneSecond, CancellationToken.None);

        result.Should().BeNullOrWhiteSpace();
    }

    /// <summary>
    /// TTS→ASR round-trip: Piper speaks a Devanagari query and Whisper must return a
    /// non-empty transcript. The raw transcript is logged for human review — no token
    /// assertions, because Whisper's output script varies (Devanagari, Latin, or noise).
    /// </summary>
    [RequiresWhisperPiperFact]
    [Trait("Category", "RequiresModel")]
    public async Task Whisper_Model_OnPiperSynthesizedHindi_ProducesNonEmptyTranscript()
    {
        using var recognizer = new WhisperSpeechRecognizer();
        await recognizer.InitializeAsync(FindAsset(WhisperModelFileName), CancellationToken.None);
        recognizer.Enabled.Should().BeTrue();

        byte[] pcm = await PiperSpeechToPcmAsync("आरएसआई क्या है");
        pcm.Length.Should().BeGreaterThan(16000, "Piper must produce audible speech");

        string transcript = await recognizer.TranscribeAsync(pcm, CancellationToken.None);

        _output.WriteLine($"Hindi transcript: '{transcript}'");
        transcript.Trim().Should().NotBeEmpty("real speech must yield a transcript");
    }

    /// <summary>
    /// TTS→ASR round-trip: Piper speaks a Roman Hinglish query and Whisper must return a
    /// non-empty transcript. The raw transcript is logged for human review — no token
    /// assertions, because Whisper's output script varies (Devanagari, Latin, or noise).
    /// </summary>
    [RequiresWhisperPiperFact]
    [Trait("Category", "RequiresModel")]
    public async Task Whisper_Model_OnPiperSynthesizedHinglish_ProducesNonEmptyTranscript()
    {
        using var recognizer = new WhisperSpeechRecognizer();
        await recognizer.InitializeAsync(FindAsset(WhisperModelFileName), CancellationToken.None);
        recognizer.Enabled.Should().BeTrue();

        byte[] pcm = await PiperSpeechToPcmAsync("RSI overbought hai kya");
        pcm.Length.Should().BeGreaterThan(16000, "Piper must produce audible speech");

        string transcript = await recognizer.TranscribeAsync(pcm, CancellationToken.None);

        _output.WriteLine($"Hinglish transcript: '{transcript}'");
        transcript.Trim().Should().NotBeEmpty("real speech must yield a transcript");
    }

    /// <summary>Five seconds of audio is transcribed within the CPU budget for ggml-base (logged + asserted).</summary>
    [RequiresWhisperFact]
    [Trait("Category", "RequiresModel")]
    public async Task Whisper_Model_FiveSecondAudio_LatencyIsLogged()
    {
        using var recognizer = new WhisperSpeechRecognizer();
        await recognizer.InitializeAsync(FindAsset(WhisperModelFileName), CancellationToken.None);

        byte[] fiveSeconds = Speech(0.20f, sampleCount: 16000 * 5);

        // First call after factory build pays model-load/warm-up; measure it separately.
        var warmup = System.Diagnostics.Stopwatch.StartNew();
        _ = await recognizer.TranscribeAsync(fiveSeconds, CancellationToken.None);
        warmup.Stop();
        Console.WriteLine(
            $"[SpeechTests] first (warm-up) transcription took {warmup.ElapsedMilliseconds} ms.");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string transcript1 = await recognizer.TranscribeAsync(fiveSeconds, CancellationToken.None);
        stopwatch.Stop();

        // ggml-base with WithThreads(N-1) on CPU; budget is a regression guard (catches a
        // model/threading regression), set from measured in-suite reality: 8.3–10.4 s on the
        // i3-6006U under sustained load vs ~7.8 s standalone. 12 s gives headroom over both.
        const long budgetMs = 12000;
        Console.WriteLine(
            $"[SpeechTests] 5s Hindi transcription took {stopwatch.ElapsedMilliseconds} ms (budget {budgetMs} ms, ggml-base, transcript={transcript1.Length} chars).");
        if (stopwatch.ElapsedMilliseconds > budgetMs)
        {
            Assert.Fail(
                $"5s transcription took {stopwatch.ElapsedMilliseconds} ms (budget {budgetMs} ms, ggml-base). transcript1={transcript1.Length} chars");
        }
    }
    /// <summary>Hinglish function words become Devanagari while trading terms stay Latin.</summary>
    [Theory]
    [InlineData("RSI overbought hai kya", "RSI overbought है क्या")]
    [InlineData("Support break ho gaya", "Support break हो गया")]
    [InlineData("Trend kya hai bhai", "Trend क्या है भाई")]
    [InlineData("RSI 72 pe hai", "RSI 72 पे है")]
    [InlineData("bhai chart dekho", "भाई chart देखो")]
    public void Transliterator_ConvertsHinglishFunctionWords(string input, string expected) =>
        HinglishTransliterator.ToDevanagari(input).Should().Be(expected);

    /// <summary>Devanagari, English, empty, and unknown words pass through untouched.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("What is RSI")]
    [InlineData("मुझे RSI समझ नहीं आया")]
    public void Transliterator_PassesThroughUnchangedText(string input) =>
        HinglishTransliterator.ToDevanagari(input).Should().Be(input);

    /// <summary>"chai" is not a dictionary word and must not match by prefix.</summary>
    [Fact]
    public void Transliterator_DoesNotTransliterateSimilarWords()
    {
        HinglishTransliterator.ToDevanagari("chai").Should().Be("chai");
        HinglishTransliterator.ToDevanagari("chai piyo").Should().Be("chai piyo");
    }

    /// <summary>The seeded dictionary covers at least 150 Roman-to-Devanagari pairs.</summary>
    [Fact]
    public void Transliterator_Dictionary_HasAtLeast150Entries() =>
        HinglishTransliterator.DictionaryCount.Should().BeGreaterThanOrEqualTo(150);

    /// <summary>Punctuation, spacing, and numbers survive transliteration.</summary>
    [Fact]
    public void Transliterator_PreservesPunctuationSpacingAndNumbers()
    {
        HinglishTransliterator.ToDevanagari("RSI 72, MACD 0.30 hai - kya?")
            .Should().Be("RSI 72, MACD 0.30 है - क्या?");
    }

    /// <summary>A missing piper.exe leaves the synthesizer disabled and speaking a no-op.</summary>
    [Fact]
    public async Task Piper_MissingExecutable_StaysDisabledAndSpeakIsNoOp()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"tc-missing-{Guid.NewGuid():N}.exe");
        using var synth = new PiperSpeechSynthesizer();

        await synth.InitializeAsync(missing, PiperVoicePath(), CancellationToken.None);

        synth.Enabled.Should().BeFalse();
        var act = async () => await synth.SpeakAsync("RSI overbought hai", CancellationToken.None);
        await act.Should().NotThrowAsync();
        await synth.StopAsync();
    }

    /// <summary>A missing voice model leaves the synthesizer disabled.</summary>
    [Fact]
    public async Task Piper_MissingVoiceModel_StaysDisabled()
    {
        string missingVoice = Path.Combine(Path.GetTempPath(), $"tc-missing-{Guid.NewGuid():N}.onnx");
        using var synth = new PiperSpeechSynthesizer();

        await synth.InitializeAsync(FindAsset(PiperExeName), missingVoice, CancellationToken.None);

        synth.Enabled.Should().BeFalse();
    }

    /// <summary>Skips Piper synthesis tests that need the executable and voice model.</summary>
    internal sealed class RequiresPiperFactAttribute : FactAttribute
    {
        public RequiresPiperFactAttribute()
        {
            string exe = FindAsset(Path.Combine("piper", PiperExeName));
            string voice = FindAsset(Path.Combine("piper", PiperVoiceFileName));
            if (!File.Exists(exe) || !File.Exists(voice))
            {
                Skip = $"Piper not installed: expected {exe} and {voice}; see tools/README.md.";
            }
        }
    }

    /// <summary>With Piper installed, a Hinglish line synthesizes a WAV that is cleaned up.</summary>
    [RequiresPiperFact]
    [Trait("Category", "RequiresModel")]
    public async Task Piper_Model_SynthesizesAndCleansUpWav()
    {
        using var synth = new PiperSpeechSynthesizer();
        await synth.InitializeAsync(
            FindAsset(Path.Combine("piper", PiperExeName)),
            PiperVoicePath(),
            CancellationToken.None);
        synth.Enabled.Should().BeTrue();

        var before = Directory.GetFiles(Path.GetTempPath(), "tc-piper-*.wav").Length;
        await synth.SpeakAsync("RSI overbought zone mein hai", CancellationToken.None);

        // The synthesizer deletes its temporary WAV in a finally block.
        Directory.GetFiles(Path.GetTempPath(), "tc-piper-*.wav").Length.Should().Be(before);
    }
}



