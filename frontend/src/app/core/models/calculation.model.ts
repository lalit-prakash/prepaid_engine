export interface SimulateChargeRequest {
  tariffId: string;
  consumptionKwh: number;
  connectedLoadOrContractDemand: number;
}

/** POST /api/v1/calculation-workbench/simulate response — every figure is computed by the
 * real domain methods (Tariff.CalculateEnergyCharge/CalculateFixedCharge, ElectricityDuty.
 * Calculate) server-side; the frontend never re-derives these numbers itself. */
export interface SimulateChargeResult {
  simulation: true;
  tariff: { id: string; name: string; category: number };
  inputs: { consumptionKwh: number; connectedLoadOrContractDemand: number };
  grossEnergyCharge: number;
  prepaidRebatePercent: number;
  rebateAmount: number;
  netEnergyCharge: number;
  fixedChargeMonthly: number;
  fixedChargeDaily: number;
  electricityDuty: number;
  totalMonthlyCharge: number;
}
