/** GET /api/v1/analytics/overview - database-side aggregates over a bounded date range. */
export interface AnalyticsOverview {
  from: string;
  to: string;
  generatedAt: string;
  totals: {
    consumptionKwh: number;
    rechargeAttempts: number;
    rechargeReceived: number;
    rechargeFailed: number;
    bills: number;
    billed: number;
    settled: number;
    collectionRatePercent: number | null;
  };
  consumption: { date: string; totalKwh: number; meterCount: number }[];
  recharges: { date: string; attempts: number; amountReceived: number; failed: number }[];
  billing: { date: string; billCount: number; billed: number; settled: number }[];
  commands: { date: string; disconnects: number; reconnects: number }[];
  communication: { date: string; failures: number; restorations: number }[];
  walletDistribution: { overdrawn: number; upTo100: number; upTo500: number; upTo1000: number; upTo5000: number; over5000: number };
  tariffMix: { tariffId: string; tariffName: string; category: number; tariffStatus: number; billCount: number; billed: number }[];
  exceptions: { sourceType: number; status: number; count: number }[];
}
