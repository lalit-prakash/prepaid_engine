import { BillStatus } from './consumer.model';

export enum ConsumerCategory {
  Domestic = 0,
  Bpl = 1,
  Industrial = 2,
  Commercial = 3,
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
  };
  reading: { consumptionKwh: number; periodStart: string; periodEnd: string };
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
