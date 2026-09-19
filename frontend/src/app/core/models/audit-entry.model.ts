/** One row of GET /api/v1/audit-entries. */
export interface AuditEntrySummary {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  actor: string;
  /** Role of the signed-in user; null for system actions and entries recorded before this was captured. */
  actorRole: string | null;
  sourceIp: string | null;
  /** Shared by everything recorded for one request. */
  correlationId: string | null;
  oldValue: string | null;
  newValue: string | null;
  details: string | null;
  occurredAt: string;
}

export interface AuditSearchPage {
  items: AuditEntrySummary[];
  nextCursor: string | null;
  totalCount: number;
}

/** GET /api/v1/audit-entries/summary. */
export interface AuditSummaryStats {
  total: number;
  last24Hours: number;
  entityTypes: string[];
  actors: string[];
}
