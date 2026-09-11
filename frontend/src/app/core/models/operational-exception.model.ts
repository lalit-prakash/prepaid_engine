export enum OperationalExceptionSourceType {
  MeterCommand = 0,
  ConnectivityCommand = 1,
}

export enum OperationalExceptionStatus {
  Open = 0,
  Resolved = 1,
}

/** One row of GET /api/v1/exceptions. */
export interface OperationalExceptionSummary {
  id: string;
  accountNumber: string;
  name: string;
  sourceType: OperationalExceptionSourceType;
  sourceId: string;
  description: string;
  status: OperationalExceptionStatus;
  resolutionNote: string | null;
  createdAt: string;
  resolvedAt: string | null;
}
