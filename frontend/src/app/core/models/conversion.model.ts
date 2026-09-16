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

/** Mirrors backend ConversionMeterStatus. */
export enum ConversionMeterStatus {
  Normal = 0,
  Faulty = 1,
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
  lastReadingDate: string;
  lastBillingDate: string;
  temporaryDisconnectionDate: string | null;
  reconnectionDate: string | null;
  lastBillFrKwh: number;
  lastBillFrKvah: number;
  lastBillMaxDemandKw: number;
  outstandingAmount: number;
  meterStatus: ConversionMeterStatus;
  isPermanentConsumer: boolean;
  foaAmount: number;
  diaAmount: number;
  /** Null until the payment-mode-change command has been acknowledged. */
  readingAtConversion: number | null;
}
