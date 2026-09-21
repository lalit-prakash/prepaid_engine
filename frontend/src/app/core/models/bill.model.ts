import { BillStatus } from './consumer.model';

/** Mirrors the backend's ConsumerCategory (the tariff book's consumption categories); the numbers must match the API. */
export enum ConsumerCategory {
  Domestic = 0,
  NonDomestic = 1,
  GeneralPurpose = 2,
  PublicWaterSupply = 3,
  Industrial = 4,
  FerroAlloy = 5,
  Agriculture = 6,
  Crematorium = 7,
  ElectricVehicle = 8,
  KutirJyotiBpl = 9,
  PublicLighting = 10,
}

/** One row of GET /api/v1/bills — a bill joined with its consumer and tariff. */
export interface BillSummary {
  id: string;
  accountNumber: string;
  name: string;
  category: ConsumerCategory;
  tariffName: string;
  energyChargeGross: number;
  prepaidRebateAmount: number;
  energyChargeNet: number;
  fixedCharge: number;
  electricityDutyAmount: number;
  fppasAmount: number;
  tmcAmount: number;
  cpmcAmount: number;
  arrearsAmount: number;
  amount: number;
  amountPaid: number;
  status: BillStatus;
  generatedAt: string;
}

/** One row of GET /api/v1/bills/search. */
export interface BillListItem {
  id: string;
  accountNumber: string;
  name: string;
  category: ConsumerCategory;
  tariffName: string;
  tariffId: string;
  energyChargeNet: number;
  fixedCharge: number;
  electricityDutyAmount: number;
  fppasAmount: number;
  amount: number;
  amountPaid: number;
  status: BillStatus;
  generatedAt: string;
}

export interface BillSearchPage {
  items: BillListItem[];
  nextCursor: string | null;
  totalCount: number;
}

/** GET /api/v1/bills/summary - database-side aggregates. */
export interface BillSummaryStats {
  total: number;
  paid: number;
  partiallyPaid: number;
  generated: number;
  overdue: number;
  cancelled: number;
  totalBilled: number;
  totalSettled: number;
  outstanding: number;
}

export interface BillSlabLine {
  fromKwh: number;
  upToKwh: number | null;
  ratePerKwh: number;
  kwhInSlab: number;
  charge: number;
}

/** GET /api/v1/bills/{id} — full calculation trace for one bill. */
export interface BillDetail {
  id: string;
  consumer: { accountNumber: string; name: string };
  tariff: {
    id: string;
    name: string;
    category: ConsumerCategory;
    fixedChargePerUnitPerMonth: number;
    prepaidEnergyRebatePercent: number;
    /** 0 = Active, 1 = Retired (superseded by a later tariff change). */
    status: number;
  };
  reading: { consumptionKwh: number; periodStart: string; periodEnd: string };
  slabBreakdown: BillSlabLine[];
  slabBreakdownReconciles: boolean;
  energyChargeGross: number;
  prepaidRebateAmount: number;
  energyChargeNet: number;
  fixedCharge: number;
  electricityDutyAmount: number;
  fppasAmount: number;
  fppasChargeId: string | null;
  tmcAmount: number;
  cpmcAmount: number;
  arrearsAmount: number;
  arrearsRecovered: number;
  amount: number;
  amountPaid: number;
  status: BillStatus;
  generatedAt: string;
}
