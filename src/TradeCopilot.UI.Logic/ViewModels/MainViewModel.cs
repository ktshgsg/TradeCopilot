using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TradeCopilot.UI.Logic.Models;
using TradeCopilot.UI.Logic.Services;
using TradeCopilot.UI.Logic.Speech;

namespace TradeCopilot.UI.Logic.ViewModels;

/// <summary>Conversation state and pipe commands for the overlay window. Owns no timers or WPF types.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IPipeClient _pipe;
    private readonly ILogger<MainViewModel> _log;
    private readonly IAudioRecorder? _audio;
    private readonly Action? _shutdown;
    private readonly SynchronizationContext? _ui;

    /// <summary>Creates the view model.</summary>
    /// <param name="pipe">Pipe transport.</param>
    /// <param name="log">Logger. The UI passes <c>NullLogger&lt;MainViewModel&gt;.Instance</c>.</param>
    /// <param name="audio">Optional microphone recorder for push-to-talk (null in tests and when voice is unavailable).</param>
    /// <param name="shutdown">Process exit action invoked by <see cref="QuitCommand"/> (the WPF app passes <c>Application.Shutdown</c>).</param>
    public MainViewModel(IPipeClient pipe, ILogger<MainViewModel> log, IAudioRecorder? audio = null, Action? shutdown = null)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(log);
        _pipe = pipe;
        _log = log;
        _audio = audio;
        _shutdown = shutdown;
        _ui = SynchronizationContext.Current;
        MicAvailable = audio is not null;
        if (_audio is not null)
        {
            _audio.UtteranceCaptured += Audio_UtteranceCaptured;
        }
    }

    /// <summary>Conversation bubbles, oldest first.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>Current input-box text.</summary>
    [ObservableProperty]
    private string _inputText = string.Empty;

    /// <summary>True while a query round-trip is in flight.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Footer status line.</summary>
    [ObservableProperty]
    private string _statusText = "Idle";

    /// <summary>Whether the service reports capture as running.</summary>
    [ObservableProperty]
    private bool _captureActive;

    /// <summary>Service version string shown in the footer.</summary>
    [ObservableProperty]
    private string _versionText = string.Empty;

    /// <summary>True while push-to-talk recording is on (toggle started by <see cref="ToggleRecordingCommand"/>).</summary>
    [ObservableProperty]
    private bool _isRecording;

    /// <summary>Mic button caption: "Mic" idle, "Listening..." while recording.</summary>
    public string MicButtonText => IsRecording ? "Listening..." : "Mic";

    /// <summary>Whether voice input is offered at all (recorder present and enabled in Settings).</summary>
    [ObservableProperty]
    private bool _micAvailable;

    /// <summary>True when required AI models are missing (degraded mode: rules + cache only).</summary>
    [ObservableProperty]
    private bool _degradedMode;

    /// <summary>Amber banner text shown when <see cref="DegradedMode"/> is true.</summary>
    [ObservableProperty]
    private string _degradedBannerText = "Degraded mode: AI models are not downloaded, so AI answers and voice are disabled. Rules and cache still work.";

    /// <summary>True when a microphone recorder was injected (independent of the Settings toggle).</summary>
    public bool HasRecorder => _audio is not null;

    /// <summary>Raised when the tray menu or a hotkey asks the shell to show the overlay.</summary>
    public event Action? ShowOverlayRequested;

    /// <summary>Raised when the tray menu asks the shell to open the Settings window.</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>Raised when the tray menu asks the shell to reveal the audit log folder.</summary>
    public event Action? OpenAuditLogRequested;

    /// <summary>
    /// Sends the input text as a query and appends the answer. No-ops on empty input or while busy.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken ct)
    {
        string text = InputText.Trim();
        if (string.IsNullOrEmpty(text) || IsBusy)
        {
            return;
        }

        var userMessage = new ChatMessage(MessageRole.User, text);
        Messages.Add(userMessage);
        InputText = string.Empty;
        IsBusy = true;
        try
        {
            // No ConfigureAwait(false): Messages is UI-bound, continuations must
            // return to the dispatcher thread.
            PipeResponse resp;
            try
            {
                resp = await _pipe.SendAsync(new PipeRequest("query", Text: userMessage.Text), ct);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The pipe dropped mid-query (e.g. service killed). Keep the chat
                // alive, reset the mic, and tell the user what happened.
                ResetRecordingState();
                Messages.Add(new ChatMessage(MessageRole.System, "Error: Service unreachable. Is the background service running?"));
                StatusText = "Service unreachable";
                _log.LogWarning(ex, "Query failed: service unreachable.");
                return;
            }

            if (resp.Type == "answer")
            {
                Messages.Add(new ChatMessage(MessageRole.Assistant, resp.Text ?? string.Empty, resp.Source, resp.ElapsedMs));
                StatusText = $"Answered from {resp.Source} in {resp.ElapsedMs}ms";
            }
            else
            {
                Messages.Add(new ChatMessage(MessageRole.Assistant, resp.Message ?? "Unknown error", "error"));
                StatusText = "Error";
            }
        }
        finally
        {
            IsBusy = false;
        }

        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Whether the send command can execute.</summary>
    /// <returns>True when not busy. Empty input is filtered in <see cref="SendAsync"/>.</returns>
    private bool CanSend() => !IsBusy;

    /// <summary>Pauses capture on the service, then refreshes status.</summary>
    [RelayCommand]
    private async Task PauseCaptureAsync(CancellationToken ct)
    {
        // No ConfigureAwait(false): CaptureActive/StatusText are UI-bound.
        await _pipe.SendAsync(new PipeRequest("pause_capture"), ct);
        await RefreshStatusAsync(ct);
    }

    /// <summary>Resumes capture on the service, then refreshes status.</summary>
    [RelayCommand]
    private async Task ResumeCaptureAsync(CancellationToken ct)
    {
        // No ConfigureAwait(false): CaptureActive/StatusText are UI-bound.
        await _pipe.SendAsync(new PipeRequest("resume_capture"), ct);
        await RefreshStatusAsync(ct);
    }

    /// <summary>Asks the shell to reveal the overlay window (tray menu / hotkey).</summary>
    [RelayCommand]
    private void ShowOverlay() => ShowOverlayRequested?.Invoke();

    /// <summary>Asks the shell to open the Settings window (tray menu).</summary>
    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke();

    /// <summary>Asks the shell to open the folder containing the audit log (tray menu).</summary>
    [RelayCommand]
    private void OpenAuditLog() => OpenAuditLogRequested?.Invoke();

    /// <summary>
    /// Stops voice recording, disposes the pipe client and recorder, then exits the process.
    /// Disposal is idempotent: <c>App.OnExit</c> disposes the provider again on the way out.
    /// </summary>
    [RelayCommand]
    private async Task QuitAsync()
    {
        if (IsRecording)
        {
            StopRecording();
        }

        _audio?.Dispose();
        if (_pipe is IAsyncDisposable asyncPipe)
        {
            await asyncPipe.DisposeAsync();
        }

        _shutdown?.Invoke();
    }

    /// <summary>Purges low-value rows from the service-side semantic cache, then reports the result.</summary>
    [RelayCommand]
    private async Task PurgeCacheAsync(CancellationToken ct)
    {
        // No ConfigureAwait(false): StatusText is UI-bound.
        PipeResponse resp = await _pipe.SendAsync(new PipeRequest("purge_cache"), ct);
        StatusText = resp.Type == "ok" ? "Cache purged." : $"Purge failed: {resp.Message ?? "service unreachable"}";
    }

    /// <summary>Emergency stop: halts recording, sends "panic" over the pipe, then refreshes status.</summary>
    [RelayCommand]
    private async Task PanicAsync(CancellationToken ct)
    {
        if (IsRecording)
        {
            StopRecording();
        }

        // No ConfigureAwait(false): StatusText is UI-bound.
        await _pipe.SendAsync(new PipeRequest("panic"), ct);
        await RefreshStatusAsync(ct);
        StatusText = "Panic - capture stopped.";
    }

    /// <summary>
    /// Push-to-talk toggle: press once to open the mic, speak, and the voice-activity detector
    /// fires the utterance (800 ms of silence) which is transcribed and sent as a query;
    /// press again to close the mic.
    /// </summary>
    [RelayCommand]
    private void ToggleRecording()
    {
        if (_audio is null || !MicAvailable)
        {
            StatusText = "Voice input is disabled.";
            return;
        }

        if (!IsRecording)
        {
            // AudioCapture.Start never throws (it logs and leaves IsRecording false when
            // the device is missing or held by the service), so mic contention is
            // surfaced through the status line instead of an exception. The recorder
            // abstraction is still guarded: a throwing adapter must not stick the
            // button on "Listening...".
            try
            {
                _audio.Start();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogWarning(ex, "Recorder start failed (e.g. no mic connected).");
                ResetRecordingState();
                StatusText = "Mic unavailable - no recording device found.";
                return;
            }

            IsRecording = _audio.IsRecording;
            StatusText = IsRecording
                ? "Recording - press Ctrl+Alt+Space to stop."
                : "Mic unavailable - the service may be using it.";
            if (!IsRecording)
            {
                _audio.StopRecording();
            }
        }
        else
        {
            StopRecording();
        }
    }

    /// <summary>Clears the conversation.</summary>
    [RelayCommand]
    private void ClearChat() => Messages.Clear();

    /// <summary>Asks the service for status and updates the bound status properties.</summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        try
        {
            // No ConfigureAwait(false): CaptureActive/StatusText are UI-bound.
            PipeResponse resp = await _pipe.SendAsync(new PipeRequest("status"), ct);
            if (resp.Type != "status")
            {
                StatusText = "Service unreachable";
                return;
            }

            CaptureActive = resp.CaptureActive ?? false;
            VersionText = resp.Version is { Length: > 0 } v ? $"v{v}" : string.Empty;
            StatusText = CaptureActive ? "Capturing" : "Paused";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // SendAsync never throws, so this guards only against binding bugs on this side.
            _log.LogWarning(ex, "Status refresh failed.");
            StatusText = "Service unreachable";
        }
    }

    /// <summary>Stops the recorder and flips <see cref="IsRecording"/> off.</summary>
    private void StopRecording()
    {
        try
        {
            _audio?.StopRecording();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Recorder stop failed; resetting mic state anyway.");
        }
        finally
        {
            ResetRecordingState();
        }

        StatusText = "Recording stopped";
    }

    /// <summary>
    /// Resets mic state so the button never sticks on "Listening...". Called from
    /// every error path (pipe drop, recorder failure) and from the normal stop flow.
    /// <see cref="MicButtonText"/> follows <see cref="IsRecording"/> automatically.
    /// </summary>
    private void ResetRecordingState()
    {
        IsRecording = false;
    }

    /// <summary>
    /// Bridges the recorder's worker-thread utterance event onto the captured
    /// <see cref="SynchronizationContext"/> (the WPF dispatcher in the app, null in tests).
    /// </summary>
    /// <param name="pcm">PCM16 samples of the completed utterance.</param>
    private void Audio_UtteranceCaptured(byte[] pcm)
    {
        if (pcm.Length == 0)
        {
            return;
        }

        if (_ui is null)
        {
            _ = SendUtteranceAsync(pcm);
            return;
        }

        _ui.Post(_ => { _ = SendUtteranceAsync(pcm); }, null);
    }

    /// <summary>Transcribes an utterance over the pipe and feeds the transcript through the normal query path.</summary>
    /// <param name="pcm">PCM16 mono samples of one utterance.</param>
    /// <returns>Completed work; errors are logged and reflected in <see cref="StatusText"/>.</returns>
    private async Task SendUtteranceAsync(byte[] pcm)
    {
        try
        {
            // No ConfigureAwait(false): StatusText/InputText/Messages are UI-bound.
            PipeResponse tr = await _pipe.SendAsync(
                new PipeRequest("transcribe", AudioBase64: Convert.ToBase64String(pcm)));
            if (tr.Type != "answer" || string.IsNullOrWhiteSpace(tr.Text))
            {
                StatusText = "No speech detected.";
                return;
            }

            InputText = tr.Text.Trim();
            await SendAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Voice query failed.");
            Messages.Add(new ChatMessage(MessageRole.System, "Error: Service unreachable. Is the background service running?"));
            StatusText = "Service unreachable";
        }
        finally
        {
            // The pipe dropped mid-transcribe: never leave the mic stuck on "Listening...".
            ResetRecordingState();
        }
    }

    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(MicButtonText));
}
