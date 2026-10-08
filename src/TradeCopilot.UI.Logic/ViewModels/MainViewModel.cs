using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TradeCopilot.UI.Logic.Models;
using TradeCopilot.UI.Logic.Services;

namespace TradeCopilot.UI.Logic.ViewModels;

/// <summary>Conversation state and pipe commands for the overlay window. Owns no timers or WPF types.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IPipeClient _pipe;
    private readonly ILogger<MainViewModel> _log;

    /// <summary>Creates the view model.</summary>
    /// <param name="pipe">Pipe transport.</param>
    /// <param name="log">Logger. The UI passes <c>NullLogger&lt;MainViewModel&gt;.Instance</c>.</param>
    public MainViewModel(IPipeClient pipe, ILogger<MainViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(log);
        _pipe = pipe;
        _log = log;
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
            PipeResponse resp = await _pipe.SendAsync(new PipeRequest("query", Text: userMessage.Text), ct);
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

    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
}
