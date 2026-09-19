/**
 * Mirrors RechargeStatus (backend/PrepaidEngine.Domain/Enums/RechargeStatus.cs). Note there is
 * no explicit "Pending" value — the RMS-Pending branch of POST .../recharge deliberately
 * leaves a transaction in its initial "Initiated" state (RMS hasn't confirmed Success or
 * Failed yet), so the UI treats "Initiated" as the pending state.
 */
export enum RechargeStatus {
  Initiated = 0,
  Success = 1,
  Failed = 2,
  Reversed = 3,
}

/**
 * Mirrors MeterCommandStatus (backend/PrepaidEngine.Domain/Enums/MeterCommandStatus.cs). A
 * separate lifecycle from RechargeStatus on purpose — RMS confirming payment and the meter
 * itself being credited are two different systems succeeding or failing independently, and
 * only Acknowledged means the meter was actually credited.
 */
export enum MeterCommandStatus {
  Queued = 0,
  Sent = 1,
  Acknowledged = 2,
  Failed = 3,
  TimedOut = 4,
}

/** One row of GET /api/v1/recharges. */
export interface RechargeSummary {
  id: string;
  accountNumber: string;
  name: string;
  amount: number;
  rmsReferenceId: string;
  status: RechargeStatus;
  initiatedAt: string;
  completedAt: string | null;
  /** Null when this recharge never reached RMS Success (no command was ever dispatched). */
  meterCommandStatus: MeterCommandStatus | null;
}

/** One row of GET /api/v1/recharges/search. */
export interface RechargeListItem extends RechargeSummary {
  meterNumber: string;
}

export interface RechargeSearchPage {
  items: RechargeListItem[];
  nextCursor: string | null;
  totalCount: number;
}

/** GET /api/v1/recharges/summary - database-side aggregates for the KPI strip. */
export interface RechargeSummaryStats {
  total: number;
  paymentSuccess: number;
  paymentFailed: number;
  paymentPending: number;
  paymentReversed: number;
  amountSucceeded: number;
  meterCredited: number;
  meterCreditAwaiting: number;
  meterCreditFailed: number;
}

/** GET /api/v1/recharges/{id}. */
export interface RechargeDetail {
  id: string;
  consumer: { accountNumber: string; name: string; meterNumber: string };
  amount: number;
  rmsReferenceId: string;
  status: RechargeStatus;
  initiatedAt: string;
  completedAt: string | null;
  walletBalance: number;
  meterCommand: {
    id: string;
    status: MeterCommandStatus;
    retryCount: number;
    errorMessage: string | null;
    externalCommandId: string | null;
    responseCode: string | null;
    responseMessage: string | null;
    createdAt: string;
    sentAt: string | null;
    acknowledgedAt: string | null;
  } | null;
}
