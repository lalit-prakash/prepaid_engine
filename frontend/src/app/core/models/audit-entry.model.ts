/** One row of GET /api/v1/audit-entries. */
export interface AuditEntrySummary {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  actor: string;
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
