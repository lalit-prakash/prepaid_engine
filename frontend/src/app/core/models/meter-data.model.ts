export enum DailyProfileStatus {
  Received = 0,
  Validated = 1,
  Rejected = 2,
  Billed = 3,
  Provisional = 4,
  Reconciled = 5,
}

/** One row of GET /api/v1/meter-data/dlp. */
export interface DailyLoadProfileSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  profileDate: string;
  generatedAt: string;
  /** When MDMS actually ingested this profile — determines which of the two daily billing
   * stages (8:30-9:30 AM / 12:30-1:30 PM) it was billed in. */
  receivedAt: string;
  startCumulativeKwh: number;
  endCumulativeKwh: number;
  totalKwh: number;
  status: DailyProfileStatus;
  isProvisional: boolean;
  sourceReference: string | null;
}
