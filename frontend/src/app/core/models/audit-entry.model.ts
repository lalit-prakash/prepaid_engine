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
