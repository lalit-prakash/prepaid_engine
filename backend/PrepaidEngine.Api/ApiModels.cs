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

/// <param name="Amount">Recharge amount.</param>
/// <param name="IdempotencyKey">
/// Required. Caller-supplied and must stay the same across retries of this exact recharge
/// attempt — the server deliberately does not generate one, since a server-generated key
/// would not survive a client retry after a lost response, defeating the whole guarantee.
/// In the mock RMS, prefix this with "FAIL-", "PENDING-", or "UNAVAILABLE-" to demo those outcomes.
/// </param>
public record RechargeRequest(decimal Amount, string? IdempotencyKey = null);

/// <param name="TariffId">The tariff to simulate against — must have at least one ordinary energy slab.</param>
/// <param name="ConsumptionKwh">Hypothetical consumption for this simulation.</param>
/// <param name="ConnectedLoadOrContractDemand">Hypothetical connected load/contract demand.</param>
public record SimulateChargeRequest(Guid TariffId, decimal ConsumptionKwh, decimal ConnectedLoadOrContractDemand);

/// <param name="Reason">Required. An auditable justification for the disconnect/reconnect — never optional metadata.</param>
/// <param name="CorrelationId">
/// Optional. Propagated to IConnectivityCommandClient for logs/telemetry; also doubles as the
/// mock client's outcome control (see MockConnectivityCommandClient's CONNFAIL-/CONNTIMEOUT-
/// markers). Defaults to a fresh id if omitted.
/// </param>
public record ConnectivityRequest(string Reason, string? CorrelationId = null);

/// <summary>One RMS-pushed conversion request, per AMISP integration requirement doc section 1.
/// Requests arrive as an array — a single consumer or a batch.</summary>
/// <param name="TransactionId">RMS's own transaction identifier, echoed back in the response.</param>
/// <param name="MeterSerialNumber">The smart meter's serial number.</param>
/// <param name="ConsumerNumber">RMS's consumer number — matched against Consumer.AccountNumber.</param>
/// <param name="ConsumerType">VIP/Hospital/School/ShoppingComplex/etc.</param>
/// <param name="InitialReading">Reading the last post-paid bill was based on (1st of the conversion month, 00:00 hrs).</param>
/// <param name="InitialReadingDateTime">Date/time of that initial reading.</param>
/// <param name="ConversionDate">The day RMS pushed this request.</param>
/// <param name="RequestType">"PRE" by default per spec.</param>
/// <summary>RMS's finalized postpaid->prepaid conversion request parameter set, plus the
/// pre-existing InitialReading/InitialReadingDateTime (the 1st-of-conversion-month opening
/// reading this project's conversion opening-bill calculation requires).</summary>
public record ConversionRequestItem(
    string TransactionId,
    string MeterSerialNumber,
    string ConsumerNumber,
    ConversionConsumerType ConsumerType,
    decimal InitialReading,
    DateTime InitialReadingDateTime,
    DateTime ConversionDate,
    DateTime LastReadingDate,
    DateTime LastBillingDate,
    decimal LastBillFrKwh,
    decimal LastBillFrKvah,
    decimal LastBillMaxDemandKw,
    decimal OutstandingAmount,
    ConversionMeterStatus MeterStatus,
    bool IsPermanentConsumer,
    decimal FoaAmount,
    decimal DiaAmount,
    DateTime? TemporaryDisconnectionDate = null,
    DateTime? ReconnectionDate = null,
    string? RequestType = "PRE");

/// <summary>MDMS's synchronous per-request acknowledgement back to RMS.</summary>
/// <param name="PaymentModeChangeStatus">The MDMS -> HES -> Meter command's final status, when a
/// command was actually dispatched (null for requests rejected before dispatch, e.g. a NET meter).</param>
public record ConversionResponseItem(string TransactionId, string ConsumerNumber, string ResponseCode, string? ResponseMessage, string? PaymentModeChangeStatus);

/// <param name="Note">Required resolution note, mirroring ConnectivityCommand's mandatory Reason pattern.</param>
public record ResolutionRequest(string Note);

/// <param name="MeterIds">The meters whose active billing hold should be cleared.</param>
/// <param name="Note">Required — one shared resolution note applied to every meter in the batch.</param>
public record BulkClearBillingHoldsRequest(List<Guid> MeterIds, string Note);

/// <summary>An RMS-pushed reconciliation gap/credit, per spec sections 7-8.</summary>
/// <param name="Amount">Signed: positive credits the wallet, negative debits it.</param>
/// <param name="ReconciliationDate">RMS's reconciliation date for this adjustment.</param>
/// <param name="Reference">Required free-text reference/reason RMS supplied.</param>
public record ReconciliationAdjustmentRequest(decimal Amount, DateTime ReconciliationDate, string Reference);

/// <param name="FieldName">The Tariff property that changed (e.g. "FixedChargePerUnitPerMonth").</param>
/// <param name="OldValue">The prior value, as a string (this is a change log, not a typed diff).</param>
/// <param name="NewValue">The new value, as a string.</param>
/// <param name="ChangeNote">Required — why the change was made.</param>
/// <param name="EffectiveDate">When the new value takes effect.</param>
public record TariffVersionRequest(string FieldName, string OldValue, string NewValue, string ChangeNote, DateTime EffectiveDate);

// --- Tariff governance request DTOs -------------------------------------------------------------
/// <summary>One proposed energy slab. <see cref="StartTime"/>/<see cref="EndTime"/> equivalents
/// for ToD periods are plain "HH:mm:ss"-parseable strings (see <see cref="TouPeriodInput"/>) since
/// minimal APIs don't model-bind <see cref="TimeSpan"/> from JSON as cleanly as ISO-8601 duration.</summary>
public record TariffSlabInput(decimal FromKwh, decimal? UpToKwh, decimal RatePerKwh);

public record TouPeriodInput(string Label, string StartTime, string EndTime, decimal RatePerKvah);

public record CreateTariffChangeRequestBody(
    Guid? SupersedesTariffId,
    string ProposedName,
    ConsumerCategory ProposedCategory,
    IReadOnlyList<TariffSlabInput> ProposedSlabs,
    decimal ProposedFixedChargePerUnitPerMonth,
    decimal ProposedPrepaidEnergyRebatePercent,
    decimal ProposedEmergencyCreditLimit,
    decimal? ProposedMinVendAmountSinglePhase = null,
    decimal? ProposedMaxVendAmountSinglePhase = null,
    decimal? ProposedMinVendAmountThreePhase = null,
    decimal? ProposedMaxVendAmountThreePhase = null,
    IReadOnlyList<TouPeriodInput>? ProposedTouPeriods = null);

public record UpdateTariffChangeRequestBody(
    string ProposedName,
    ConsumerCategory ProposedCategory,
    IReadOnlyList<TariffSlabInput> ProposedSlabs,
    decimal ProposedFixedChargePerUnitPerMonth,
    decimal ProposedPrepaidEnergyRebatePercent,
    decimal ProposedEmergencyCreditLimit,
    decimal? ProposedMinVendAmountSinglePhase = null,
    decimal? ProposedMaxVendAmountSinglePhase = null,
    decimal? ProposedMinVendAmountThreePhase = null,
    decimal? ProposedMaxVendAmountThreePhase = null,
    IReadOnlyList<TouPeriodInput>? ProposedTouPeriods = null);

public record SubmitTariffChangeRequestBody(string ChangeReason);

public record ApproveTariffChangeRequestBody(DateTime CommencementDate);

public record RejectTariffChangeRequestBody(string RejectionReason);
public record CancelTariffChangeRequestBody(string Reason);

/// <summary>POST /api/v1/meter-data/dlp request body.</summary>
public record DailyLoadProfileIngestRequest(
    Guid ConsumerId, Guid MeterId, DateOnly ProfileDate, DateTime GeneratedAt,
    decimal StartCumulativeKwh, decimal EndCumulativeKwh, string? SourceReference = null);

/// <summary>POST /api/v1/consumers/{consumerId}/meter-replacement request body.</summary>
public record MeterReplacementApiRequest(
    string NewMeterNumber, MeterPhase Phase, DateTime EffectiveFrom,
    decimal OldMeterClosingReadingKwh, decimal NewMeterOpeningReadingKwh, string Reason);

/// <summary>POST /api/v1/conversions/reverse request body.</summary>
public record ReverseConversionApiRequest(string ConsumerNumber, string Reason, string RequestedBy);

// --- MDMS data foundation request DTOs (BP/LS/IP/Events/Alarms/energy-validation) -------------
public record RegisterReadingIngestRequest(Guid ConsumerId, Guid MeterId, DateTime ReadingTimestamp, decimal CumulativeImportKwh, string? SourceReference = null);

public record LoadSurveyIntervalIngestRequest(Guid ConsumerId, Guid MeterId, DateTime IntervalStart, DateTime IntervalEnd, decimal ImportKwh, string? SourceReference = null);

public record InstantaneousReadingIngestRequest(
    Guid ConsumerId, Guid MeterId, DateTime Timestamp, decimal VoltageVolts, decimal CurrentAmps,
    decimal PowerKw, decimal PowerFactor, decimal FrequencyHz, MeterRelayStatus RelayStatus, string? SourceReference = null);

public record MeterEventIngestRequest(Guid ConsumerId, Guid MeterId, MeterEventCode EventCode, DateTime EventTimestamp, string? Description = null, string? SourceReference = null);

public record MeterAlarmIngestRequest(Guid ConsumerId, Guid MeterId, MeterAlarmCode AlarmCode, MeterAlarmSeverity Severity, DateTime RaisedAt, string? SourceReference = null);

public record AcknowledgeAlarmRequest(string AcknowledgedBy);

public record ResolveAlarmRequest(string ResolutionNote);

public record EvaluateEnergyValidationRequest(Guid ConsumerId, Guid MeterId, DateOnly ValidationDate);

// Exposed so WebApplicationFactory-based integration tests can bootstrap this Api project.
