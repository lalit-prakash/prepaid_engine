import { BillStatus } from '../../../../core/models/consumer.model';
import { categoryLabel } from '../../../../shared/utils/category-label';

export type ColumnType = 'text' | 'number' | 'money' | 'date' | 'datetime' | 'enum';

export interface ReportColumn {
  key: string;
  label: string;
  type: ColumnType;
  /** For 'enum' columns: numeric value -> display text. */
  labels?: Record<number, string>;
  /** Show a total for this column in the footer when the API supplies `totals[totalKey]`. */
  totalKey?: string;
  link?: (row: Record<string, unknown>) => string | null;
  /** Only shown when the report is broken down by a network level. */
  groupedOnly?: boolean;
}

export interface ReportDefinition {
  id: string;
  title: string;
  description: string;
  endpoint: string;
  columns: ReportColumn[];
  /** Day-wise reports can be broken down by a network level (zone ... DTR); the API then adds a `group` column. */
  groupable?: boolean;
  /** The API's key for a full background export of this report, when it has one (row-level reports only). */
  exportKey?: string;
  /** Extra server-side status filter (bill status), when the report supports one. */
  statusFilter?: { label: string; options: { label: string; value: string }[] };
}

/** The consumer's place in the supply network, shown on every row of the row-level reports. */
const HIERARCHY_COLUMNS: ReportColumn[] = [
  { key: 'zone', label: 'Zone', type: 'text' },
  { key: 'circle', label: 'Circle', type: 'text' },
  { key: 'division', label: 'Division', type: 'text' },
  { key: 'subDivision', label: 'Subdivision', type: 'text' },
  { key: 'substation', label: 'Sub Station', type: 'text' },
  { key: 'feeder', label: 'Feeder', type: 'text' },
  { key: 'feederCode', label: 'Feeder code', type: 'text' },
  { key: 'dtr', label: 'DTR', type: 'text' },
  { key: 'dtrCode', label: 'DTR Code', type: 'text' },
];
const GROUP_COLUMN: ReportColumn = { key: 'group', label: 'Network group', type: 'text', groupedOnly: true };

const RECHARGE_STATUS: Record<number, string> = { 0: 'Payment pending', 1: 'Payment received', 2: 'Payment failed', 3: 'Reversed' };
const METER_COMMAND_STATUS: Record<number, string> = { 0: 'Queued', 1: 'Sent', 2: 'Acknowledged', 3: 'Failed', 4: 'Timed out' };
const BILL_STATUS_LABELS: Record<number, string> = {
  [BillStatus.Generated]: 'Generated',
  [BillStatus.Paid]: 'Paid',
  [BillStatus.PartiallyPaid]: 'Partially paid',
  [BillStatus.Overdue]: 'Overdue',
  [BillStatus.Cancelled]: 'Cancelled',
};
const CATEGORY_LABELS: Record<number, string> = { 0: categoryLabel(0), 1: categoryLabel(1), 2: categoryLabel(2), 3: categoryLabel(3) };

export const REPORT_DEFINITIONS: ReportDefinition[] = [
  {
    id: 'daily-billing',
    title: 'Daily Billing Report',
    description: 'Every bill generated in the range with its charge breakdown. Totals cover the whole filtered set.',
    endpoint: 'billing',
    exportKey: 'billing',
    statusFilter: {
      label: 'Bill status',
      options: [
        { label: 'Generated', value: 'Generated' },
        { label: 'Partially paid', value: 'PartiallyPaid' },
        { label: 'Paid', value: 'Paid' },
        { label: 'Overdue', value: 'Overdue' },
        { label: 'Cancelled', value: 'Cancelled' },
      ],
    },
    columns: [
      { key: 'generatedAt', label: 'Generated', type: 'datetime' },
      { key: 'accountNumber', label: 'Account', type: 'text' },
      { key: 'name', label: 'Consumer', type: 'text' },
      ...HIERARCHY_COLUMNS,
      { key: 'category', label: 'Category', type: 'enum', labels: CATEGORY_LABELS },
      { key: 'tariffName', label: 'Tariff used', type: 'text' },
      { key: 'energyChargeNet', label: 'Net energy', type: 'money' },
      { key: 'fixedCharge', label: 'Fixed', type: 'money' },
      { key: 'electricityDutyAmount', label: 'Duty', type: 'money' },
      { key: 'fppasAmount', label: 'FPPAS', type: 'money' },
      { key: 'amount', label: 'Total', type: 'money', totalKey: 'totalBilled', link: (r) => `/billing/${r['id']}` },
      { key: 'amountPaid', label: 'Settled', type: 'money', totalKey: 'totalSettled' },
      { key: 'status', label: 'Status', type: 'enum', labels: BILL_STATUS_LABELS },
    ],
  },
  {
    id: 'live-rc-dc',
    title: 'Live RC DC Status',
    description: 'For each day: disconnects sent, the reconnects raised against those disconnects, and the recharges by those consumers with their meter-credit outcome. Eligible DC is a live count, given for today only.',
    endpoint: 'live-rc-dc',
    columns: [
      { key: 'date', label: 'Date', type: 'date' },
      { key: 'eligibleDc', label: 'Eligible DC', type: 'number' },
      { key: 'dcTriggered', label: 'DC Triggered', type: 'number' },
      { key: 'dcPending', label: 'DC Pending', type: 'number' },
      { key: 'dcSuccess', label: 'DC Success', type: 'number' },
      { key: 'rcInitiated', label: "RC Initiated Against Today's DC", type: 'number' },
      { key: 'rcPending', label: "RC Pending against Today's DC", type: 'number' },
      { key: 'rcSuccess', label: 'RC Success', type: 'number' },
      { key: 'rechargeMdmSuccess', label: 'Recharge MDM Success', type: 'number' },
      { key: 'rechargePendingInMdm', label: 'Recharge Pending in MDM', type: 'number' },
      { key: 'rechargeSuccessInHes', label: 'Recharge Success in HES', type: 'number' },
      { key: 'rechargeFailInHes', label: 'Recharge Fail in HES', type: 'number' },
    ],
  },
  {
    id: 'day-wise-rc',
    title: 'Day-wise RC Report',
    description: 'Remote reconnection commands per day, with their outcomes.',
    endpoint: 'day-wise-rc-dc',
    groupable: true,
    columns: [
      { key: 'date', label: 'Date', type: 'date' },
      GROUP_COLUMN,
      { key: 'reconnectCount', label: 'Reconnects requested', type: 'number' },
      { key: 'acknowledgedCount', label: 'Acknowledged (all commands)', type: 'number' },
      { key: 'failedCount', label: 'Failed (all commands)', type: 'number' },
      { key: 'timedOutCount', label: 'Timed out (all commands)', type: 'number' },
    ],
  },
  {
    id: 'day-wise-dc',
    title: 'Day-wise DC Report',
    description: 'Remote disconnection commands per day, with their outcomes.',
    endpoint: 'day-wise-rc-dc',
    groupable: true,
    columns: [
      { key: 'date', label: 'Date', type: 'date' },
      GROUP_COLUMN,
      { key: 'disconnectCount', label: 'Disconnects requested', type: 'number' },
      { key: 'acknowledgedCount', label: 'Acknowledged (all commands)', type: 'number' },
      { key: 'failedCount', label: 'Failed (all commands)', type: 'number' },
      { key: 'timedOutCount', label: 'Timed out (all commands)', type: 'number' },
    ],
  },
  {
    id: 'day-wise-recharge',
    title: 'Day-wise Recharge Summary',
    description: 'Recharge volumes per day, with payment outcome and meter-credit outcome kept separate.',
    endpoint: 'day-wise-recharge',
    groupable: true,
    columns: [
      { key: 'date', label: 'Date', type: 'date' },
      GROUP_COLUMN,
      { key: 'totalCount', label: 'Attempts', type: 'number', totalKey: 'totalCount' },
      { key: 'paymentReceivedCount', label: 'Payment received', type: 'number', totalKey: 'paymentReceivedCount' },
      { key: 'paymentFailedCount', label: 'Payment failed', type: 'number', totalKey: 'paymentFailedCount' },
      { key: 'paymentPendingCount', label: 'Payment pending', type: 'number' },
      { key: 'meterCreditedCount', label: 'Meter credited', type: 'number', totalKey: 'meterCreditedCount' },
      { key: 'meterCreditFailedCount', label: 'Meter credit failed', type: 'number', totalKey: 'meterCreditFailedCount' },
      { key: 'amountReceived', label: 'Amount received', type: 'money', totalKey: 'amountReceived' },
    ],
  },
  {
    id: 'recharge-failure',
    title: 'Recharge Failure Report',
    description: 'Recharge attempts where the payment failed or was declined.',
    endpoint: 'recharge-failures',
    exportKey: 'recharge-failures',
    columns: [
      { key: 'initiatedAt', label: 'Initiated', type: 'datetime' },
      { key: 'accountNumber', label: 'Account', type: 'text' },
      { key: 'name', label: 'Consumer', type: 'text' },
      ...HIERARCHY_COLUMNS,
      { key: 'amount', label: 'Amount', type: 'money' },
      { key: 'rmsReferenceId', label: 'RMS reference', type: 'text', link: (r) => `/recharge/${r['id']}` },
      { key: 'status', label: 'Payment', type: 'enum', labels: RECHARGE_STATUS },
    ],
  },
  {
    id: 'meter-credit-failure',
    title: 'Meter Credit Failure Report',
    description: 'Meter credit commands that failed or timed out after payment was received.',
    endpoint: 'meter-credit-failures',
    exportKey: 'meter-credit-failures',
    columns: [
      { key: 'createdAt', label: 'Created', type: 'datetime' },
      { key: 'accountNumber', label: 'Account', type: 'text' },
      { key: 'name', label: 'Consumer', type: 'text' },
      ...HIERARCHY_COLUMNS,
      { key: 'creditAmount', label: 'Credit amount', type: 'money' },
      { key: 'status', label: 'Status', type: 'enum', labels: METER_COMMAND_STATUS },
      { key: 'retryCount', label: 'Retries', type: 'number' },
      { key: 'responseCode', label: 'Response code', type: 'text' },
      { key: 'errorMessage', label: 'Failure reason', type: 'text', link: (r) => `/meter-credit/${r['id']}` },
    ],
  },
];
