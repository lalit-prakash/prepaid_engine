import { EnergyUnit, FixedChargeBasis, VoltageLevel } from './tariff.model';

/** POST /api/v1/calculation-workbench/simulate. Only the tariff, the day's consumption and the load are required. */
export interface SimulateChargeRequest {
  tariffId: string;
  consumptionKwh: number;
  connectedLoadOrContractDemand: number;
  monthToDateKwh?: number;
  loadInHp?: boolean;
  meteredOnLtSide?: boolean;
  tmcMonthly?: number;
  cpmcMonthly?: number;
  priorMonthEnergyCharge?: number;
  fppasRatePercent?: number;
  daysInMonth?: number;
  touKvahByBand?: Record<string, number>;
  dayKvah?: number;
  monthToDateKvah?: number;
}

/**
 * The response is one day's prepaid bill, worked out by the same DailyBillCalculator the daily billing run debits from. The frontend never
 * re-derives any of these numbers; it only lays them out.
 */
export interface SimulateChargeResult {
  simulation: true;
  tariff: {
    id: string;
    name: string;
    category: number;
    scheduleCode: string | null;
    voltageLevel: VoltageLevel;
    energyUnit: EnergyUnit;
    fixedChargeBasis: FixedChargeBasis;
    fixedChargePerUnitPerMonth: number;
    prepaidEnergyRebatePercent: number;
    minimumChargeableDemand: number | null;
    isTimeOfDay: boolean;
    bands: string[];
  };
  inputs: { consumptionKwh: number; dayKvah: number | null; monthToDateKwh: number; monthToDateKvah: number | null; loadUsed: number; loadInHp: boolean; meteredOnLtSide: boolean };
  /** The energy the charge was worked on, and its unit (kVAh for HT/EHT/Industrial LT when kVAh was given, otherwise kWh). */
  billedEnergy: number;
  billedUnit: 'kWh' | 'kVAh';
  grossEnergyCharge: number;
  prepaidRebatePercent: number;
  rebateAmount: number;
  netEnergyCharge: number;
  fixedChargeDaily: number;
  fixedChargeMonthly: number;
  electricityDuty: number;
  ltSideMeteringSurcharge: number;
  tmc: number;
  cpmc: number;
  fppasShare: number;
  /** The exact sum of the components. */
  total: number;
  /** What a real day would debit from the wallet, rounded to paise. */
  totalDebited: number;
  notes: string[];
}
