import { ConsumerCategory } from './bill.model';

/** One row of GET /api/v1/tariffs. */
export interface TariffSummary {
  id: string;
  name: string;
  category: ConsumerCategory;
  /** 0 = Active, 1 = Retired. */
  status: number;
  fixedChargePerUnitPerMonth: number;
  prepaidEnergyRebatePercent: number;
  emergencyCreditLimit: number;
  slabCount: number;
  touPeriodCount: number;
}

export interface TariffSlab {
  id: string;
  fromKwh: number;
  upToKwh: number | null;
  ratePerKwh: number;
}

export interface TariffTouPeriod {
  id: string;
  label: string;
  /** "HH:MM:SS" — TimeSpan serialized as a string by System.Text.Json. */
  startTime: string;
  endTime: string;
  ratePerKvah: number;
}

/** GET /api/v1/tariffs/{id}. */
export interface TariffDetail {
  id: string;
  name: string;
  category: ConsumerCategory;
  /** 0 = Active, 1 = Retired. */
  status: number;
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

/** One immutable tariff version in a lineage (GET /api/v1/tariffs/{id}/lineage). */
export interface TariffLineageVersion {
  versionNumber: number;
  tariffId: string;
  name: string;
  /** 0 = Active, 1 = Retired. */
  status: number;
  fixedChargePerUnitPerMonth: number;
  prepaidEnergyRebatePercent: number;
  isCurrentlyViewed: boolean;
  changeRequestId: string | null;
  changeReason: string | null;
  submittedBy: string | null;
  approvedBy: string | null;
  commencementDate: string | null;
  effectiveFrom: string | null;
  retiredAt: string | null;
}

export interface TariffOpenChange {
  id: string;
  /** TariffChangeRequestStatus. */
  status: number;
  proposedName: string;
  createdBy: string;
  submittedAt: string | null;
  commencementDate: string | null;
}

export interface TariffLineage {
  versions: TariffLineageVersion[];
  openChanges: TariffOpenChange[];
}
