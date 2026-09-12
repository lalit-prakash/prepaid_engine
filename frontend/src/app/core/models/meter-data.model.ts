export enum LoadSurveyQuality {
  Valid = 0,
  Duplicate = 1,
  Missing = 2,
  NegativeConsumption = 3,
  MeterReset = 4,
  Rollover = 5,
  OutOfSequence = 6,
  Estimated = 7,
}

export enum LoadSurveyStatus {
  Received = 0,
  Validated = 1,
  Rejected = 2,
  Processed = 3,
  Provisional = 4,
}

export enum DailyProfileStatus {
  Received = 0,
  Validated = 1,
  Rejected = 2,
  Billed = 3,
  Provisional = 4,
  Reconciled = 5,
}

/** One row of GET /api/v1/meter-data/ls. */
export interface LoadSurveyIntervalSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  intervalStart: string;
  intervalEnd: string;
  cumulativeKwh: number;
  intervalKwh: number;
  quality: LoadSurveyQuality;
  status: LoadSurveyStatus;
  receivedAt: string;
  sourceReference: string | null;
}

/** One row of GET /api/v1/meter-data/dlp. */
export interface DailyLoadProfileSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  profileDate: string;
  generatedAt: string;
  startCumulativeKwh: number;
  endCumulativeKwh: number;
  totalKwh: number;
  status: DailyProfileStatus;
  isProvisional: boolean;
  sourceReference: string | null;
}
