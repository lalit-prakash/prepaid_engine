import { MeterCommandStatus } from './recharge.model';

export { MeterCommandStatus };

/** One row of GET /api/v1/meter-commands. */
export interface MeterCommandSummary {
  id: string;
  accountNumber: string;
  name: string;
  creditAmount: number;
  status: MeterCommandStatus;
  retryCount: number;
  errorMessage: string | null;
  createdAt: string;
  sentAt: string | null;
  acknowledgedAt: string | null;
  rechargeTransactionId: string;
  rmsReferenceId: string;
}

/** GET /api/v1/meter-commands/{id}. */
export interface MeterCommandDetail {
  id: string;
  consumer: { accountNumber: string; name: string };
  creditAmount: number;
  status: MeterCommandStatus;
  retryCount: number;
  errorMessage: string | null;
  createdAt: string;
  sentAt: string | null;
  acknowledgedAt: string | null;
  recharge: { id: string; rmsReferenceId: string; amount: number };
}

/** POST /api/v1/meter-commands/{id}/retry response. */
export interface RetryMeterCommandResult {
  id: string;
  status: MeterCommandStatus;
  retryCount: number;
  errorMessage: string | null;
}
