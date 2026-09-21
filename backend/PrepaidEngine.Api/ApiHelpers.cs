using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Security;
using PrepaidEngine.Api.Dashboard;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Conversion;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Sla;
using PrepaidEngine.Infrastructure.Tariffs;
using PrepaidEngine.Application.Sla;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using PrepaidEngine.Infrastructure.Rms;

namespace PrepaidEngine.Api;

/// <summary>Helpers shared by the endpoint modules: audit and exception recording, cursor parsing and the like.</summary>
internal static class ApiHelpers
{
    // --- Local helpers: audit / operational-exception / reconciliation wiring ------------------
    // These are plain local functions (no external port needed) called from the endpoints below at
    // the specific, documented trigger points (see README.md's Audit and Reconciliation sections).

    /// <summary>Whether a disconnect may be dispatched now: only 11:00 AM to 4:00 PM IST, because the tariff book gives prepaid consumers credit hours from
    /// 4:00 PM to 11:00 AM (and on official holidays) when supply continues whatever the balance. See <see cref="PrepaidEngine.Domain.DisconnectionWindow"/>.</summary>
    internal static bool IsWithinDisconnectWindow(DateTime utcNow) => PrepaidEngine.Domain.DisconnectionWindow.IsOpen(utcNow);


    // Why a change request cannot proceed, or null. Checked at create, submit and approve so two requests
    // can never both retire the same tariff, and a new tariff can never reuse the name of a different
    // Active one (the unique index on Active names would otherwise fail later, at activation).
    internal static async Task<string?> TariffChangeConflictAsync(PrepaidEngineDbContext db, Guid? selfId, Guid? supersedesId, string proposedName)
    {
        if (supersedesId.HasValue)
        {
            var superseded = await db.Tariffs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == supersedesId.Value);
            if (superseded is null)
                return $"No tariff found with id '{supersedesId}' to revise.";
            if (superseded.Status == TariffLifecycleStatus.Retired)
                return "Cannot revise a tariff that has already been retired.";

            var otherOpen = await db.TariffChangeRequests.AnyAsync(r =>
                r.Id != selfId && r.SupersedesTariffId == supersedesId.Value &&
                (r.Status == TariffChangeRequestStatus.PendingApproval || r.Status == TariffChangeRequestStatus.Scheduled));
            if (otherOpen)
                return "This tariff already has a pending-approval or scheduled change request. Resolve it before creating another.";
        }

        var nameClash = await db.Tariffs.AnyAsync(t => t.Status == TariffLifecycleStatus.Active && t.Name == proposedName && t.Id != supersedesId);
        if (nameClash)
            return $"An active tariff named '{proposedName}' already exists. Revise it instead, or choose a different name.";

        return null;
    }


    internal static AuditEntry Audit(PrepaidEngineDbContext db, string entityType, string entityId, string action, string actor,
        string? oldValue = null, string? newValue = null, string? details = null)
    {
        var entry = new AuditEntry(Guid.NewGuid(), entityType, entityId, action, actor, DateTime.UtcNow, oldValue, newValue, details);
        db.AuditEntries.Add(entry);
        return entry;
    }


    internal static OperationalException RaiseException(PrepaidEngineDbContext db, OperationalExceptionSourceType sourceType, Guid sourceId, Guid consumerId, string description)
    {
        var exception = new OperationalException(Guid.NewGuid(), sourceType, sourceId, consumerId, description, DateTime.UtcNow);
        db.OperationalExceptions.Add(exception);
        return exception;
    }


    // Applies a ReconciliationAdjustment (RMS-pushed signed amount, spec sections 7-8) to a
    // consumer's wallet exactly like a recharge credit/debit, tagged distinctly in the ledger. Shared
    // by the reconciliation endpoint below — kept as a local function so the wallet-mutation logic
    // lives in exactly one place rather than being duplicated alongside the recharge endpoint's own.
    internal static ReconciliationAdjustment ApplyReconciliationAdjustment(
        PrepaidEngineDbContext db, Consumer consumer, decimal amount, DateTime reconciliationDate, string reference)
    {
        // Explicitly track the new ledger entry as Added, same as the recharge endpoint does and for
        // the same reason (see its comment): the wallet was loaded from the DB (already tracked, not
        // part of a brand-new graph), and EF's change detection does not reliably infer "newly added"
        // for an entity appended to an already-tracked entity's backing-field collection — it can
        // mis-detect it as Modified and emit a bogus UPDATE for a row that doesn't exist yet.
        var walletTransaction = amount > 0
            ? consumer.Wallet.Credit(amount, WalletTransactionType.Reconciliation, reference)
            : consumer.Wallet.Debit(-amount, WalletTransactionType.Reconciliation, reference);
        db.WalletTransactions.Add(walletTransaction);

        var adjustment = new ReconciliationAdjustment(
            Guid.NewGuid(), consumer.Id, consumer.AccountNumber, amount, reconciliationDate, reference,
            balanceAfter: consumer.Wallet.Balance, appliedAt: DateTime.UtcNow);
        db.ReconciliationAdjustments.Add(adjustment);
        return adjustment;
    }


    // Cross-consumer operator visibility into every Daily Load Profile — real, provisional, or
    // billed — never hidden behind the daily settlement result alone.
    // --- Meter Data: server-side paged search for the time-series profiles ---------------------------
    // Each endpoint is keyset-paginated newest first ("<sort key>_<Id>" cursor), filters and counts in the
    // database, and never caps silently: the old list endpoints returned at most 500/1000 rows and the
    // browser then searched within those, which could hide older data. q matches account and meter number
    // by prefix and consumer name by substring; from/to are inclusive calendar dates.
    internal static (DateTime? From, DateTime? ToExclusive) MeterDataRange(DateTime? from, DateTime? to) =>
        (from.HasValue ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : null,
         to.HasValue ? DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc) : null);


    internal static (string Prefix, string Contains) MeterDataTerms(string q)
    {
        var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return (term + "%", "%" + term + "%");
    }


    internal static bool TryParseCursor(string? after, out long key, out Guid id)
    {
        key = 0; id = Guid.Empty;
        if (string.IsNullOrEmpty(after)) return true;
        var parts = after.Split('_', 2);
        return parts.Length == 2 && long.TryParse(parts[0], out key) && Guid.TryParse(parts[1], out id);
    }
}
