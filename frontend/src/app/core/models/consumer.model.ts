// Domain models mirroring the real Prepaid Engine API contracts
// (backend/PrepaidEngine.Api/Program.cs). Anything not yet exposed by the API
// is marked "Illustrative" at the point it's used, never modeled here as if
// it were real — see docs/ARCHITECTURE.md for the real-vs-mock boundary.

/** Mirrors ConnectionStatus (backend/PrepaidEngine.Domain/Enums/ConnectionStatus.cs). The
 * *Pending values record local intent only — see the "no fake success states" rule: a consumer
 * only reaches a final Active/Disconnected once a dispatched ConnectivityCommand is actually
 * Acknowledged, not merely sent. */
export enum ConnectionStatus {
  Active = 0,
  Disconnected = 1,
  ReconnectionPending = 2,
  DisconnectionPending = 3,
}

export enum MeterPhase {
  SinglePhase = 0,
  ThreePhase = 1,
}

export enum WalletTransactionType {
  Recharge = 0,
  BillDebit = 1,
}

export enum BillStatus {
  Generated = 0,
  Paid = 1,
  PartiallyPaid = 2,
  Overdue = 3,
  Cancelled = 4,
}

export interface WalletTransaction {
  amount: number;
  type: WalletTransactionType;
  occurredAt: string;
  reference: string;
}

/**
 * The RMS wallet ledger as mirrored by the Prepaid Engine for billing/recharge
 * orchestration. RMS remains the system of record for the consumer's real
 * financial wallet — see the "RMS Wallet Balance" labeling convention used
 * everywhere this is displayed (never call it just "Wallet Balance").
 */
export interface Wallet {
  balance: number;
  emergencyCreditLimit: number;
  /** Decided by the API from the configured low-balance threshold. */
  lowBalance: boolean;
  isWithinEmergencyCredit: boolean;
  transactions: WalletTransaction[];
}

export interface Bill {
  id: string;
  energyChargeGross: number;
  prepaidRebateAmount: number;
  energyChargeNet: number;
  fixedCharge: number;
  electricityDutyAmount: number;
  fppasAmount: number;
  fppasChargeId: string | null;
  tmcAmount: number;
  cpmcAmount: number;
  arrearsAmount: number;
  arrearsRecovered: number;
  amount: number;
  amountPaid: number;
  status: BillStatus;
  generatedAt: string;
}

export interface Meter {
  meterNumber: string;
  phase: MeterPhase;
  lastReadingKwh: number;
}

export interface ConsumerSummary {
  accountNumber: string;
  name: string;
  connectionStatus: ConnectionStatus;
  meterNumber: string;
  walletBalance: number;
  emergencyCreditLimit: number;
}

/** Where the consumer sits in the supply network; every level is null until the consumer is mapped to a DTR. */
export interface ConsumerNetwork {
  zone: string | null;
  circle: string | null;
  division: string | null;
  subDivision: string | null;
  substation: string | null;
  feeder: string | null;
  feederCode: string | null;
  dtr: string | null;
  dtrCode: string | null;
}

export interface ConsumerDetail {
  network: ConsumerNetwork;
  id: string;
  accountNumber: string;
  name: string;
  mobileNumber: string | null;
  serviceAddress: string;
  connectionStatus: ConnectionStatus;
  connectedLoadKw: number;
  isDisconnectEligibleOnCredit: boolean;
  meter: Meter;
  wallet: Wallet;
  bills: Bill[];
}

export interface RechargeRequest {
  amount: number;
  idempotencyKey: string;
}

export interface RechargeResult {
  rmsReferenceId: string;
  status: string;
  walletBalance: number;
  /** MeterCommandStatus name (e.g. "Acknowledged", "Failed", "TimedOut"), or null/undefined
   * when no meter command was dispatched (a replayed request, or RMS did not report Success). */
  meterCommandStatus?: string | null;
  replayed?: boolean;
}

export interface ConnectivityRequest {
  reason: string;
  correlationId?: string;
}

export interface ConnectivityResult {
  connectivityCommandId: string;
  commandStatus: string;
  consumerConnectionStatus: string;
}

/** One row of GET /api/v1/consumers/search. */
export interface ConsumerListItem {
  accountNumber: string;
  name: string;
  mobileNumber: string | null;
  connectionStatus: ConnectionStatus;
  meterNumber: string;
  walletBalance: number;
  /** When the latest conversion request in the current view was requested; null when the consumer has none. */
  requestedAt: string | null;
  /** When the consumer's latest completed postpaid-to-prepaid conversion completed. */
  convertedAt: string | null;
  /** The decision note of the latest rejected conversion request; null when none was recorded. */
  failureReason: string | null;
  lastRechargeAt: string | null;
  /** Network position of the consumer's DTR; all null when not mapped to a DTR yet. */
  zone: string | null;
  circle: string | null;
  division: string | null;
  subDivision: string | null;
  substation: string | null;
  feeder: string | null;
  feederCode: string | null;
  dtr: string | null;
  dtrCode: string | null;
}

/** GET /api/v1/consumers/summary: counts of postpaid-to-prepaid conversion requests by outcome, computed by the database. */
export interface ConsumerSummaryStats {
  totalRequests: number;
  completed: number;
  failed: number;
  pending: number;
}

export interface ConsumerSearchPage {
  items: ConsumerListItem[];
  nextCursor: string | null;
  totalCount: number;
}
