using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace TradeCopilot.Core.Speech;

/// <summary>
/// Microphone capture that emits 100 ms PCM16 mono chunks and detects utterance
/// boundaries with an energy (RMS) voice-activity detector.
/// </summary>
/// <remarks>
/// <para>
/// Capture is 16 kHz, mono, 16-bit PCM, matching the Whisper input format and
/// <see cref="WhisperSpeechRecognizer"/>. Incoming samples are appended to a
/// 10-second ring buffer so an utterance that closes later can still be sliced out
/// of the recent past.
/// </para>
/// <para>
/// The VAD treats a chunk as speech when its RMS is at least
/// <see cref="SpeechRmsThreshold"/> and as silence when it is below
/// <see cref="SilenceRmsThreshold"/>. When silence lasts at least
/// <see cref="SilenceHangoverMs"/> after a speech chunk, the utterance closes and is
/// emitted trimmed by <see cref="TailTrimMs"/> of trailing silence, unless it is
/// shorter than <see cref="MinUtteranceMs"/>. <see cref="Start"/> is a no-op when no
/// recording device exists; this type never throws.
/// </para>
/// </remarks>
public sealed class AudioCapture : IDisposable
{
    /// <summary>Chunk length in milliseconds (also the device buffer length).</summary>
    public const int ChunkMilliseconds = 100;

    /// <summary>RMS at or above which a chunk counts as speech.</summary>
    public const float SpeechRmsThreshold = 0.02f;

    /// <summary>RMS below which a chunk counts as silence.</summary>
    public const float SilenceRmsThreshold = 0.008f;

    /// <summary>Silence duration (ms) that closes an open utterance.</summary>
    public const int SilenceHangoverMs = 800;

    /// <summary>Trailing silence trimmed from the end of an emitted utterance (ms).</summary>
    public const int TailTrimMs = 200;

    /// <summary>Utterances shorter than this (ms) are discarded as noise.</summary>
    public const int MinUtteranceMs = 300;

    /// <summary>Ring buffer capacity in seconds.</summary>
    public const int RingBufferSeconds = 10;

    /// <summary>Default capture sample rate in Hz.</summary>
    public const int DefaultSampleRateHz = 16000;

    private const int BitsPerSample = 16;
    private const int ChannelCount = 1;
    private const int DeviceNumberOfBuffers = 4;

    private readonly ILogger<AudioCapture> _log;
    private readonly int _sampleRateHz;
    private readonly int _bytesPerSample;
    private readonly byte[] _ring;
    private readonly object _gate = new();

    private WaveInEvent? _waveIn;
    private long _totalBytesWritten;
    private long _utteranceStart = -1;
    private long _utteranceSpeechBytes;
    private int _silenceBytes;
    private bool _disposed;

    /// <summary>Creates a capture session.</summary>
    /// <param name="log">Logger.</param>
    /// <param name="sampleRateHz">Sample rate in Hz; must be positive.</param>
    public AudioCapture(ILogger<AudioCapture> log, int sampleRateHz = DefaultSampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRateHz);
        _log = log;
        _sampleRateHz = sampleRateHz;
        _bytesPerSample = BitsPerSample / 8 * ChannelCount;
        _ring = new byte[sampleRateHz * RingBufferSeconds * _bytesPerSample];
    }

    /// <summary>Raised for every captured 100 ms PCM16 chunk, speech or silence.</summary>
    public event Action<byte[]>? OnAudioChunk;

    /// <summary>
    /// Raised once per completed utterance with the PCM16 samples from the first speech
    /// chunk up to the close point, trimmed by <see cref="TailTrimMs"/> of trailing
    /// silence. Never raised for utterances shorter than <see cref="MinUtteranceMs"/>.
    /// </summary>
    public event Action<byte[]>? OnUtteranceEnd;

    /// <summary>True while the capture device is running.</summary>
    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _waveIn is not null;
            }
        }
    }

    /// <summary>
    /// Starts capture. Idempotent and a no-op (with a warning) when the machine has no
    /// recording device or the device refuses to start. Never throws.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // A previous Stop() dropped the silence hangover but kept the open
            // utterance slice; clear it so the first chunk after Start() cannot
            // emit a stale half-utterance from an earlier session.
            _utteranceStart = -1;
            _utteranceSpeechBytes = 0;
            _silenceBytes = 0;
            if (_waveIn is not null)
            {
                return;
            }

            int deviceCount;
            try
            {
                deviceCount = WaveInEvent.DeviceCount;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogWarning(ex, "Audio capture unavailable: cannot query recording devices.");
                return;
            }

            if (deviceCount == 0)
            {
                _log.LogWarning("No recording device found; audio capture disabled.");
                return;
            }

            try
            {
                _utteranceStart = -1;
                _silenceBytes = 0;
                _totalBytesWritten = 0;
                Array.Clear(_ring);

                var waveIn = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(_sampleRateHz, BitsPerSample, ChannelCount),
                    BufferMilliseconds = ChunkMilliseconds,
                    NumberOfBuffers = DeviceNumberOfBuffers,
                };
                waveIn.DataAvailable += OnDataAvailable;
                waveIn.RecordingStopped += OnRecordingStopped;
                waveIn.StartRecording();
                _waveIn = waveIn;
                _log.LogInformation(
                    "Audio capture started at {SampleRate} Hz mono PCM16 ({ChunkMs} ms chunks).",
                    _sampleRateHz,
                    ChunkMilliseconds);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogWarning(ex, "Failed to start audio capture; capture disabled.");
                _waveIn?.Dispose();
                _waveIn = null;
            }
        }
    }

    /// <summary>Stops capture and releases the device. Idempotent; never throws.</summary>
    public void Stop()
    {
        WaveInEvent? waveIn;
        lock (_gate)
        {
            waveIn = _waveIn;
            _waveIn = null;
            if (waveIn is null)
            {
                return;
            }
        }

        try
        {
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            waveIn.StopRecording();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Error while stopping audio capture.");
        }
        finally
        {
            waveIn.Dispose();
            _log.LogInformation("Audio capture stopped.");
        }
    }

    /// <summary>
    /// Feeds one PCM16 chunk into the ring buffer, fires the chunk event, and advances
    /// the VAD. Internal so tests can drive the detector without a microphone.
    /// </summary>
    /// <param name="chunk">PCM16 mono samples at the configured sample rate.</param>
    internal void ProcessChunk(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0)
        {
            return;
        }

        byte[]? utterance = null;
        Action<byte[]>? chunkHandler;
        Action<byte[]>? utteranceHandler;

        lock (_gate)
        {
            WriteRing(chunk);
            chunkHandler = OnAudioChunk;

            float rms = ComputeRms(chunk);
            if (rms >= SpeechRmsThreshold)
            {
                // Speech: anchor the window at the FIRST speech chunk of the utterance
                // (not the most recent one) and clear accumulated trailing silence.
                if (_utteranceStart < 0)
                {
                    _utteranceStart = _totalBytesWritten - chunk.Length;
                    _utteranceSpeechBytes = 0;
                }

                _utteranceSpeechBytes += chunk.Length;
                _silenceBytes = 0;
            }
            else if (rms < SilenceRmsThreshold && _utteranceStart >= 0)
            {
                _silenceBytes += chunk.Length;
                if (_silenceBytes >= MillisecondsToBytes(SilenceHangoverMs))
                {
                    // Close the utterance, dropping the trailing silence pad.
                    long trimBytes = MillisecondsToBytes(TailTrimMs);
                    long end = Math.Max(_utteranceStart, _totalBytesWritten - trimBytes);
                    byte[] candidate = ReadRange(_utteranceStart, end);

                    // Reject clicks and other noise: the SPEECH portion must exceed the
                    // minimum, so a burst shorter than MinUtteranceMs never emits even
                    // though the hangover has already elapsed.
                    if (_utteranceSpeechBytes > MillisecondsToBytes(MinUtteranceMs))
                    {
                        utterance = candidate;
                    }

                    _utteranceStart = -1;
                    _utteranceSpeechBytes = 0;
                    _silenceBytes = 0;
                }
            }

            utteranceHandler = OnUtteranceEnd;
        }

        chunkHandler?.Invoke(chunk);
        if (utterance is not null)
        {
            utteranceHandler?.Invoke(utterance);
        }
    }

    /// <summary>Appends a chunk to the ring buffer, wrapping at the end.</summary>
    private void WriteRing(byte[] chunk)
    {
        int ringLength = _ring.Length;
        int offset = (int)(_totalBytesWritten % ringLength);
        int first = Math.Min(chunk.Length, ringLength - offset);
        Buffer.BlockCopy(chunk, 0, _ring, offset, first);
        if (first < chunk.Length)
        {
            Buffer.BlockCopy(chunk, first, _ring, 0, chunk.Length - first);
        }

        _totalBytesWritten += chunk.Length;
    }

    /// <summary>Copies <c>[start, end)</c> out of the ring buffer, honouring wrap-around.</summary>
    private byte[] ReadRange(long start, long end)
    {
        int length = (int)Math.Clamp(end - start, 0, _ring.Length);
        var result = new byte[length];
        int ringLength = _ring.Length;
        int read = 0;
        int index = (int)(start % ringLength);
        while (read < length)
        {
            int take = Math.Min(length - read, ringLength - index);
            Buffer.BlockCopy(_ring, index, result, read, take);
            read += take;
            index = (index + take) % ringLength;
        }

        return result;
    }

    /// <summary>
    /// Empties buffered audio without emitting an utterance. Call before (re)starting
    /// capture so a stale half-utterance from an earlier session is never emitted.
    /// </summary>
    public void DiscardBufferedAudio()
    {
        lock (_gate)
        {
            _utteranceStart = -1;
            _utteranceSpeechBytes = 0;
            _silenceBytes = 0;
        }
    }

    /// <summary>Root-mean-square of a PCM16 buffer, normalized to [-1, 1].</summary>
    private static float ComputeRms(byte[] pcm)
    {
        int samples = pcm.Length / 2;
        if (samples == 0)
        {
            return 0f;
        }

        double sumSquares = 0;
        for (int i = 0; i < samples; i++)
        {
            float normalized = BitConverter.ToInt16(pcm, i * 2) / 32768f;
            sumSquares += normalized * normalized;
        }

        return (float)Math.Sqrt(sumSquares / samples);
    }
    /// <summary>Stops capture and detaches every event handler.</summary>
    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
            OnAudioChunk = null;
            OnUtteranceEnd = null;
        }
    }
    /// <summary>Milliseconds of audio converted to a PCM16 byte count.</summary>
    /// <param name="milliseconds">Duration in milliseconds.</param>
    /// <returns>Number of PCM16 mono bytes for that duration.</returns>
    internal int MillisecondsToBytes(int milliseconds) => _sampleRateHz * milliseconds / 1000 * _bytesPerSample;

    /// <summary>Copies the device buffer into a fresh array and runs the VAD.</summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        var chunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer!, 0, chunk, 0, e.BytesRecorded);
        ProcessChunk(chunk);
    }

    /// <summary>Logs an unexpected device stop.</summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _log.LogWarning(e.Exception, "Audio device stopped unexpectedly.");
        }
    }
}
