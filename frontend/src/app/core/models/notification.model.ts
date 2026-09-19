export enum NotificationEventType {
  LowBalance = 0,
  EmergencyCredit = 1,
  DisconnectionEligible = 2,
  BillingProvisional = 3,
  /** Postpaid→prepaid conversion completed — "you are now in prepaid mode". */
  PrepaidConversionCompleted = 4,
  /** Auto-dispatched by the emergency-credit guard when the wallet falls to/below the
   * emergency-credit limit. */
  AutoDisconnected = 5,
  /** Auto-dispatched by the emergency-credit guard when a recharge/adjustment brings the wallet
   * back to a positive balance. */
  AutoReconnected = 6,
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

/** GET /api/v1/notifications/summary: counts computed by the database. */
export interface NotificationSummaryStats {
  total: number;
  pending: number;
  sent: number;
  failed: number;
}
