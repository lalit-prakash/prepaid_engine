using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Application.Connectivity;

/// <summary>
/// Central place every wallet-mutating flow calls afterward so a consumer's connection state
/// always reacts the same way to their wallet balance, regardless of which flow moved it
/// (recharge, a reconciliation adjustment, or the daily DLP charge):
///
/// <list type="bullet">
/// <item><description>Balance falls at/below (i.e. no longer <see cref="PrepaidWallet.IsWithinEmergencyCredit"/>)
/// the emergency-credit limit while the consumer is Active — auto-dispatch a Disconnect
/// <see cref="ConnectivityCommand"/> and queue an alert notification.</description></item>
/// <item><description>Balance becomes positive again while the consumer is Disconnected for
/// exactly this reason — auto-dispatch a Reconnect <see cref="ConnectivityCommand"/>.</description></item>
/// </list>
///
/// Automatic disconnection only happens inside the disconnection window (11 AM to 4 PM IST; the tariff book's credit hours are 4 PM to 11 AM): outside it the
/// consumer stays connected and <c>DeferredDisconnectionWorker</c> disconnects them once the window opens. Reconnection is never held back. This guard does not apply the manual RC/DC endpoint's
/// dispatch window — that window exists for operator-initiated actions; a wallet crossing the emergency-
/// credit line is a system-triggered event with no such restriction in the source requirement.
/// Never calls <c>SaveChangesAsync</c> itself — the caller's own save covers whatever this method
/// adds to the same <c>DbContext</c>, matching how <c>IBillingEngineService</c> already batches
/// its own writes.
/// </summary>
public interface IEmergencyCreditGuard
{
    Task EvaluateAsync(Consumer consumer, CancellationToken cancellationToken = default);
}
