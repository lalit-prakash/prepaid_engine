export enum MeterAssignmentEventType {
  Installed = 0,
  Replaced = 1,
  Removed = 2,
}

/** One row of GET /api/v1/meter-replacements. */
export interface MeterReplacementSummary {
  id: string;
  accountNumber: string;
  name: string;
  eventType: MeterAssignmentEventType;
  oldMeterNumber: string | null;
  newMeterNumber: string;
  effectiveFrom: string;
  oldMeterClosingReadingKwh: number | null;
  newMeterOpeningReadingKwh: number;
  reason: string | null;
  recordedAt: string;
}
