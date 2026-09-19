/** One row of GET /api/v1/reconciliation-adjustments. */
export interface ReconciliationAdjustmentSummary {
  id: string;
  accountNumber: string;
  name: string;
  amount: number;
  paymentMode: string;
  reconciliationDate: string;
  reference: string;
  balanceAfter: number;
  appliedAt: string;
}

/** POST /api/v1/consumers/{accountNumber}/reconciliation-adjustments request body. */
export interface ApplyReconciliationAdjustmentRequest {
  amount: number;
  reconciliationDate: string;
  reference: string;
}

/** GET /api/v1/reconciliation-adjustments/summary: totals computed by the database. */
export interface ReconciliationSummaryStats {
  total: number;
  totalCredited: number;
  totalDebited: number;
}
