import { ConsumerCategory } from './bill.model';
import { TariffSlab, TariffTouPeriod } from './tariff.model';

/** Mirrors backend TariffChangeRequestStatus. */
export enum TariffChangeRequestStatus {
  Draft = 0,
  PendingApproval = 1,
  Rejected = 2,
  Scheduled = 3,
  Activated = 4,
  Cancelled = 5,
}

export interface TariffSlabInput {
  fromKwh: number;
  upToKwh: number | null;
  ratePerKwh: number;
}

export interface TouPeriodInput {
  label: string;
  /** "HH:MM" or "HH:MM:SS". */
  startTime: string;
  endTime: string;
  ratePerKvah: number;
}

/** One row of GET /api/v1/tariff-change-requests. */
export interface TariffChangeRequestSummary {
  id: string;
  supersedesTariffId: string | null;
  resultingTariffId: string | null;
  proposedName: string;
  proposedCategory: ConsumerCategory;
  status: TariffChangeRequestStatus;
  createdBy: string;
  createdAt: string;
  submittedBy: string | null;
  submittedAt: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  commencementDate: string | null;
  rejectedBy: string | null;
  rejectedAt: string | null;
  rejectionReason: string | null;
  activatedAt: string | null;
}

export interface TariffChangeRequestProposed {
  proposedName: string;
  proposedCategory: ConsumerCategory;
  proposedFixedChargePerUnitPerMonth: number;
  proposedPrepaidEnergyRebatePercent: number;
  proposedEmergencyCreditLimit: number;
  proposedMinVendAmountSinglePhase: number | null;
  proposedMaxVendAmountSinglePhase: number | null;
  proposedMinVendAmountThreePhase: number | null;
  proposedMaxVendAmountThreePhase: number | null;
  slabs: TariffSlab[];
  touPeriods: TariffTouPeriod[];
}

export interface TariffChangeRequestCurrent {
  name: string;
  category: ConsumerCategory;
  fixedChargePerUnitPerMonth: number;
  prepaidEnergyRebatePercent: number;
  emergencyCreditLimit: number;
  minVendAmountSinglePhase: number | null;
  maxVendAmountSinglePhase: number | null;
  minVendAmountThreePhase: number | null;
  maxVendAmountThreePhase: number | null;
  slabs: TariffSlab[];
  touPeriods: TariffTouPeriod[];
}

/** GET /api/v1/tariff-change-requests/{id}. */
export interface TariffChangeRequestDetail {
  id: string;
  supersedesTariffId: string | null;
  resultingTariffId: string | null;
  status: TariffChangeRequestStatus;
  proposed: TariffChangeRequestProposed;
  current: TariffChangeRequestCurrent | null;
  createdBy: string;
  createdAt: string;
  changeReason: string | null;
  submittedBy: string | null;
  submittedAt: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  commencementDate: string | null;
  rejectedBy: string | null;
  rejectedAt: string | null;
  rejectionReason: string | null;
  activatedAt: string | null;
}

export interface CreateTariffChangeRequestBody {
  supersedesTariffId: string | null;
  proposedName: string;
  proposedCategory: ConsumerCategory;
  proposedSlabs: TariffSlabInput[];
  proposedFixedChargePerUnitPerMonth: number;
  proposedPrepaidEnergyRebatePercent: number;
  proposedEmergencyCreditLimit: number;
  proposedMinVendAmountSinglePhase?: number | null;
  proposedMaxVendAmountSinglePhase?: number | null;
  proposedMinVendAmountThreePhase?: number | null;
  proposedMaxVendAmountThreePhase?: number | null;
  proposedTouPeriods?: TouPeriodInput[];
}

export type UpdateTariffChangeRequestBody = Omit<CreateTariffChangeRequestBody, 'supersedesTariffId'>;
