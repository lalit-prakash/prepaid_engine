/** GET /api/v1/system/health: measured, not assumed. */
export interface SystemHealth {
  overall: 'Healthy' | 'Degraded' | 'Down';
  checkedAt: string;
  api: { state: string; environment: string; version: string; startedAt: string; uptimeSeconds: number };
  database: { state: string; latencyMs: number | null; appliedMigrations: number; pendingMigrations: number; error: string | null };
  workers: WorkerHealth[];
  queues: {
    meterCommandsQueued: number;
    oldestQueuedSeconds: number | null;
    meterCommandsInFlight: number;
    connectivityCommandsPending: number;
    notificationsPending: number;
    openExceptions: number;
    activeBillingHolds: number;
  } | null;
  recentBillingRuns: BillingRunRow[] | null;
}

export interface WorkerHealth {
  name: string;
  expectedEverySeconds: number;
  lastSuccessAt: string | null;
  lastFailureAt: string | null;
  lastError: string | null;
  runs: number;
  failures: number;
  /** Healthy, Stale (no recent success), Failing or NotYetRun. */
  state: string;
}

export interface BillingRunRow {
  runType: string;
  billingDate: string;
  status: string;
  consumerCount: number;
  exceptionCount: number;
  startedAt: string;
  completedAt: string | null;
  lastHeartbeatAt: string;
}

/** GET /api/v1/system/integrations. */
export interface SystemIntegrations {
  checkedAt: string;
  outbound: OutboundIntegration[];
  inbound: InboundFeed[];
}

export interface OutboundIntegration {
  name: string;
  purpose: string;
  /** The class behind the port. */
  adapter: string;
  /** Mock (a simulator) or Live. */
  mode: string;
  lastActivityAt: string | null;
  last24hOk: number;
  last24hFailed: number;
}

export interface InboundFeed {
  name: string;
  latestAt: string | null;
  last24h: number;
}
