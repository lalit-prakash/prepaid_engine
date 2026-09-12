export enum NotificationEventType {
  LowBalance = 0,
  EmergencyCredit = 1,
  DisconnectionEligible = 2,
  BillingProvisional = 3,
}

export enum NotificationStatus {
  Pending = 0,
  Sent = 1,
  Failed = 2,
}

/** One row of GET /api/v1/notifications. */
export interface NotificationSummary {
  id: string;
  accountNumber: string;
  name: string;
  eventType: NotificationEventType;
  message: string;
  status: NotificationStatus;
  createdAt: string;
  sentAt: string | null;
  providerReference: string | null;
}
