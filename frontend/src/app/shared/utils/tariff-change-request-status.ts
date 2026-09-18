import { StatusTone } from '../components/badge/status-badge';
import { TariffChangeRequestStatus } from '../../core/models/tariff-change-request.model';

const LABELS: Record<TariffChangeRequestStatus, string> = {
  [TariffChangeRequestStatus.Draft]: 'Draft',
  [TariffChangeRequestStatus.PendingApproval]: 'Pending Approval',
  [TariffChangeRequestStatus.Rejected]: 'Rejected',
  [TariffChangeRequestStatus.Scheduled]: 'Scheduled',
  [TariffChangeRequestStatus.Activated]: 'Activated',
  [TariffChangeRequestStatus.Cancelled]: 'Cancelled',
};

const TONES: Record<TariffChangeRequestStatus, StatusTone> = {
  [TariffChangeRequestStatus.Draft]: 'neutral',
  [TariffChangeRequestStatus.PendingApproval]: 'warning',
  [TariffChangeRequestStatus.Rejected]: 'danger',
  [TariffChangeRequestStatus.Scheduled]: 'info',
  [TariffChangeRequestStatus.Activated]: 'success',
  [TariffChangeRequestStatus.Cancelled]: 'neutral',
};

export function changeRequestStatusLabel(status: TariffChangeRequestStatus): string {
  return LABELS[status] ?? 'Unknown';
}

export function changeRequestStatusTone(status: TariffChangeRequestStatus): StatusTone {
  return TONES[status] ?? 'neutral';
}
