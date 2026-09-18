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

export enum RegisterReadingStatus {
  Received = 0,
  Validated = 1,
  Rejected = 2,
}

/** One row of GET /api/v1/meter-data/bp — a Billing Profile register reading, used to validate
 * DLP against the meter's actual cumulative register. Never a billing input itself. */
export interface RegisterReadingSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  readingTimestamp: string;
  cumulativeImportKwh: number;
  status: RegisterReadingStatus;
  receivedAt: string;
  sourceReference: string | null;
}

/** One row of GET /api/v1/meter-data/ls — a Load Survey interval. Consumption intelligence
 * only (load pattern, peak demand, depletion forecasting) — never a billing input. */
export interface LoadSurveyIntervalSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  intervalStart: string;
  intervalEnd: string;
  importKwh: number;
  sourceReference: string | null;
}

export enum MeterRelayStatus {
  Closed = 0,
  Open = 1,
}

/** One row of GET /api/v1/meter-data/ip/latest — the meter's latest instantaneous electrical
 * state. Meter-health intelligence only — never aggregated into consumption or billing. */
export interface InstantaneousReadingSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  timestamp: string;
  voltageVolts: number;
  currentAmps: number;
  powerKw: number;
  powerFactor: number;
  frequencyHz: number;
  relayStatus: MeterRelayStatus;
}

export enum MeterEventCode {
  PowerFailure = 0,
  PowerRestoration = 1,
  CommunicationFailure = 2,
  CommunicationRestoration = 3,
  RelayClosed = 4,
  RelayOpened = 5,
  MeterClockChanged = 6,
  Other = 99,
}

export enum MeterEventStatus {
  Received = 0,
  Processed = 1,
}

/** One row of GET /api/v1/meter-data/events — informational meter history, never requiring
 * acknowledgement (contrast MeterAlarmSummary, which does). */
export interface MeterEventSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  eventCode: MeterEventCode;
  eventTimestamp: string;
  description: string | null;
  status: MeterEventStatus;
}

export enum MeterAlarmCode {
  Tamper = 0,
  MagneticInfluence = 1,
  CoverOpen = 2,
  ReverseEnergy = 3,
  VoltageAbnormality = 4,
  CurrentAbnormality = 5,
  Other = 99,
}

export enum MeterAlarmSeverity {
  Info = 0,
  Warning = 1,
  Critical = 2,
}

export enum MeterAlarmStatus {
  Open = 0,
  Acknowledged = 1,
  Resolved = 2,
}

/** One row of GET /api/v1/meter-data/alarms — a severity-bearing meter condition that always
 * requires an acknowledge/resolve workflow, kept separate from MeterEventSummary. */
export interface MeterAlarmSummary {
  id: string;
  accountNumber: string;
  name: string;
  meterNumber: string;
  alarmCode: MeterAlarmCode;
  severity: MeterAlarmSeverity;
  raisedAt: string;
  status: MeterAlarmStatus;
  acknowledgedAt: string | null;
  acknowledgedBy: string | null;
  resolvedAt: string | null;
  resolutionNote: string | null;
}
