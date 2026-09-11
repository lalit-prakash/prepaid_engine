export enum ConversionStatus {
  Requested = 0,
  Approved = 1,
  Rejected = 2,
  Completed = 3,
}

export enum ConversionConsumerType {
  Residential = 0,
  Vip = 1,
  Hospital = 2,
  School = 3,
  ShoppingComplex = 4,
  Other = 5,
}

/** One row of GET /api/v1/conversions. */
export interface ConversionSummary {
  id: string;
  accountNumber: string;
  name: string;
  transactionId: string;
  meterSerialNumber: string;
  requestType: string;
  consumerType: ConversionConsumerType;
  initialReading: number;
  initialReadingDateTime: string;
  conversionDate: string;
  gracePeriodEndDate: string;
  status: ConversionStatus;
  decisionNote: string | null;
  requestedAt: string;
  decidedAt: string | null;
  completedAt: string | null;
}
