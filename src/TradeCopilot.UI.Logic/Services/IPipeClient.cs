using TradeCopilot.UI.Logic.Models;

namespace TradeCopilot.UI.Logic.Services;

/// <summary>Abstraction over the named-pipe transport. Mocked in view-model tests.</summary>
public interface IPipeClient
{
    /// <summary>Sends one request line and returns the parsed response line. Never throws.</summary>
    /// <param name="request">Request to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The service response, or an "error" response on transport failure.</returns>
    Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken ct = default);
}
