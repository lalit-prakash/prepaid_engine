import { ConsumerCategory } from './bill.model';

/** One row of GET /api/v1/tariffs. */
export interface TariffSummary {
  id: string;
  name: string;
  category: ConsumerCategory;
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
