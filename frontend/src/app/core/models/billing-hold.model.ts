/** One row of GET /api/v1/meter-data/billing-holds. */
export interface BillingHoldSummary {
  id: string;
  meterId: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  actualBillingBlocked: boolean;
  blockReason: string;
  blockedAt: string;
  clearedAt: string | null;
}
