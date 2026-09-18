/** One row of GET /api/v1/sla — real SLA performance, never a fabricated percentage. A
 * `sampleSize` of 0 means the workflow has no completed samples yet ("Unavailable"). */
export interface SlaMetric {
  name: string;
  targetMinutes: number;
  actualAverageMinutes: number | null;
  sampleSize: number;
  breachCount: number;
  breachPercentage: number | null;
  status: 'Met' | 'AtRisk' | 'Breached' | 'Unavailable';
}

/** GET /api/v1/risk-indicators — real open-condition counts only, never a fabricated ₹ figure. */
export interface RiskIndicatorsSummary {
  openExceptions: number;
  activeBillingHolds: number;
  unresolvedMeterAlarms: number;
  disconnectedConsumers: number;
  failedEnergyValidations: number;
}
