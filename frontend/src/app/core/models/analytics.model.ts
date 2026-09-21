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

/** GET /api/v1/analytics/balance-history: the daily wallet totals that were recorded, oldest first. */
export interface BalanceHistory {
  from: string;
  to: string;
  rows: BalanceHistoryRow[];
}

export interface BalanceHistoryRow {
  date: string;
  totalConsumers: number;
  activeConsumers: number;
  disconnectedConsumers: number;
  lowBalanceConsumers: number;
  walletTotal: number;
  recordedAt: string;
}

/** GET /api/v1/analytics/meter-communication: how many installed meters have been heard from recently. The 7-day group is inside the 3-day group. */
export interface MeterCommunication {
  asOf: string;
  total: number;
  communicating: number;
  nonCommunicating3Days: number;
  nonCommunicating7Days: number;
  neverCommunicated: number;
}

/** GET /api/v1/analytics/wallet-distribution: consumers by wallet balance band. */
export interface WalletDistribution {
  total: number;
  bands: { label: string; count: number; totalBalance: number }[];
}
