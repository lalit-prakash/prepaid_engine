// Domain models mirroring the real Prepaid Engine API contracts
// (backend/PrepaidEngine.Api/Program.cs). Anything not yet exposed by the API
// is marked "Illustrative" at the point it's used, never modeled here as if
// it were real — see docs/frontend-scope.md for the real-vs-mock boundary.

export enum ConnectionStatus {
  Connected = 0,
  Disconnected = 1,
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

export interface ConsumerDetail {
  accountNumber: string;
  name: string;
  serviceAddress: string;
  connectionStatus: ConnectionStatus;
  connectedLoadKw: number;
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
