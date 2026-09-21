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
  /** The tariff book's schedule code (DLT, CLT, GP, IHT ...); null on a tariff created before codes were recorded. */
  scheduleCode: string | null;
  voltageLevel: VoltageLevel;
  energyUnit: EnergyUnit;
  fixedChargeBasis: FixedChargeBasis;
  /** The first slab's rate, or null for a Time-of-Day-only tariff. */
  firstSlabRate: number | null;
  /** The Normal-band rate of a Time-of-Day tariff. */
  normalTouRate: number | null;
}

export enum VoltageLevel { LT = 0, HT = 1, EHT = 2 }
export enum EnergyUnit { Kwh = 0, Kvah = 1 }
export enum FixedChargeBasis { PerKw = 0, PerKva = 1, PerKwOrHp = 2, None = 3 }

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
  scheduleCode: string | null;
  voltageLevel: VoltageLevel;
  energyUnit: EnergyUnit;
  fixedChargeBasis: FixedChargeBasis;
  minimumChargeableDemand: number | null;
  initialCreditSinglePhase: number | null;
  initialCreditThreePhase: number | null;
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

/** GET /api/v1/tariffs/book-check: each FY 2026-27 book schedule against the tariff in force for it. */
export interface BookCheck {
  book: string;
  matches: number;
  differs: number;
  missing: number;
  rows: { scheduleCode: string; name: string; tariffId: string | null; status: 'Matches' | 'Differs' | 'Missing'; differences: { field: string; book: string; current: string }[] }[];
}

/** GET /api/v1/tariff-parameters: the tariff book's fixed figures, from the same constants billing uses. */
export interface TariffParameters {
  source: string;
  sections: { title: string; items: { label: string; value: string; note: string | null }[] }[];
}
