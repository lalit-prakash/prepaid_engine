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
}

/** GET /api/v1/recharges/{id}. */
export interface RechargeDetail {
  id: string;
  consumer: { accountNumber: string; name: string };
  amount: number;
  rmsReferenceId: string;
  status: RechargeStatus;
  initiatedAt: string;
  completedAt: string | null;
  walletBalance: number;
}
