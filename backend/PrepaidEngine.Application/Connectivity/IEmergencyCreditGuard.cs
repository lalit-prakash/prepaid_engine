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
/// Deliberately does not apply the manual RC/DC endpoint's "Happy Hours" (9 AM-2 PM) dispatch
/// window — that window exists for operator-initiated actions; a wallet crossing the emergency-
/// credit line is a system-triggered event with no such restriction in the source requirement.
/// Never calls <c>SaveChangesAsync</c> itself — the caller's own save covers whatever this method
/// adds to the same <c>DbContext</c>, matching how <c>IBillingEngineService</c> already batches
/// its own writes.
/// </summary>
public interface IEmergencyCreditGuard
{
    Task EvaluateAsync(Consumer consumer, CancellationToken cancellationToken = default);
}
