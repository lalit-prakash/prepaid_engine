import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TariffService } from '../../../../core/services/tariff.service';
import { CalculationService } from '../../../../core/services/calculation.service';
import { EnergyUnit, FixedChargeBasis, TariffSummary, VoltageLevel } from '../../../../core/models/tariff.model';
import { SimulateChargeResult } from '../../../../core/models/calculation.model';
import { categoryLabel } from '../../../../shared/utils/category-label';

interface Scenario {
  label: string;
  hint: string;
  apply: () => void;
}

interface Segment {
  label: string;
  value: number;
  colour: string;
}

/**
 * SIMULATION ONLY. Works out one day's prepaid bill for any tariff, consumption and load, the way the daily billing run does: the slabs continue
 * from the month-to-date consumption, the 2% prepaid rebate applies to the energy charge, the fixed charge accrues daily, and electricity duty,
 * the optional LT-side metering surcharge, maintenance charges and an FPPAS share are added. Never touches a real consumer, bill or wallet, and
 * never computes anything itself: every figure comes from POST /api/v1/calculation-workbench/simulate, which calls the same DailyBillCalculator
 * billing uses (the frontend calculation rule in docs/ARCHITECTURE.md).
 */
@Component({
  selector: 'pe-workbench',
  imports: [FormsModule, DecimalPipe],
  templateUrl: './workbench.html',
  styleUrl: './workbench.scss',
})
export class Workbench implements OnInit {
  protected readonly tariffs = signal<TariffSummary[]>([]);
  protected readonly loadingTariffs = signal(true);
  protected readonly tariffLoadError = signal<string | null>(null);

  protected selectedTariffId = '';
  protected consumptionKwh = 30;
  protected monthToDateKwh = 0;
  protected load = 2;
  protected loadInHp = false;

  protected meteredOnLtSide = false;
  protected tmcMonthly = 0;
  protected cpmcMonthly = 0;
  protected priorMonthEnergyCharge = 0;
  protected fppasRatePercent = 0;
  protected readonly bandKvah: Record<string, number> = {};
  protected readonly optionsOpen = signal(false);

  protected readonly simulating = signal(false);
  protected readonly result = signal<SimulateChargeResult | null>(null);
  protected readonly simulationError = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;
  protected readonly VoltageLevel = VoltageLevel;

  protected readonly selected = signal<TariffSummary | null>(null);

  protected readonly grouped = computed(() => {
    const groups: { name: string; tariffs: TariffSummary[] }[] = [
      { name: 'Low Tension', tariffs: [] },
      { name: 'High Tension', tariffs: [] },
      { name: 'Extra High Tension', tariffs: [] },
    ];
    for (const t of [...this.tariffs()].sort((a, b) => (a.scheduleCode ?? 'zz').localeCompare(b.scheduleCode ?? 'zz') || a.name.localeCompare(b.name))) groups[t.voltageLevel]?.tariffs.push(t);
    return groups.filter((g) => g.tariffs.length > 0);
  });

  /** The bill as stacked segments, for the bar: what each part contributes to the day's total. */
  protected readonly segments = computed<Segment[]>(() => {
    const r = this.result();
    if (!r) return [];
    return [
      { label: 'Energy (net)', value: r.netEnergyCharge, colour: '#2563eb' },
      { label: 'Fixed', value: r.fixedChargeDaily, colour: '#0d9488' },
      { label: 'Duty', value: r.electricityDuty, colour: '#d99a06' },
      { label: 'LT-side surcharge', value: r.ltSideMeteringSurcharge, colour: '#c2410c' },
      { label: 'Maintenance', value: r.tmc + r.cpmc, colour: '#7c3aed' },
      { label: 'FPPAS', value: Math.max(r.fppasShare, 0), colour: '#64748b' },
    ].filter((s) => s.value > 0);
  });

  protected readonly scenarios: Scenario[] = [
    { label: 'Quiet day', hint: '0 kWh: only the fixed charge', apply: () => { this.consumptionKwh = 0; this.monthToDateKwh = 60; } },
    { label: 'First slab', hint: '30 kWh from the start of the month', apply: () => { this.consumptionKwh = 30; this.monthToDateKwh = 0; } },
    { label: 'Crosses 100 kWh', hint: '12 kWh after 90 kWh: part in each slab', apply: () => { this.consumptionKwh = 12; this.monthToDateKwh = 90; } },
    { label: 'Top slab', hint: '10 kWh after 250 kWh', apply: () => { this.consumptionKwh = 10; this.monthToDateKwh = 250; } },
  ];

  constructor(
    private readonly tariffService: TariffService,
    private readonly calculationService: CalculationService,
  ) {}

  ngOnInit(): void {
    this.tariffService.list('Active').subscribe({
      next: (tariffs) => {
        this.tariffs.set(tariffs);
        this.loadingTariffs.set(false);
        const first = tariffs.find((t) => t.scheduleCode === 'DLT') ?? tariffs[0];
        if (first) {
          this.selectedTariffId = first.id;
          this.selected.set(first);
        }
      },
      error: () => {
        this.tariffLoadError.set('Could not load tariffs from the API.');
        this.loadingTariffs.set(false);
      },
    });
  }

  protected onTariffChange(): void {
    const t = this.tariffs().find((x) => x.id === this.selectedTariffId) ?? null;
    this.selected.set(t);
    this.loadInHp = false;
    this.result.set(null);
  }

  protected get isTimeOfDay(): boolean {
    const t = this.selected();
    return !!t && t.slabCount === 0 && t.touPeriodCount > 0;
  }

  protected get loadUnit(): string {
    const t = this.selected();
    if (!t) return 'kW';
    if (t.fixedChargeBasis === FixedChargeBasis.PerKva) return 'kVA';
    if (t.fixedChargeBasis === FixedChargeBasis.PerKwOrHp) return this.loadInHp ? 'HP' : 'kW';
    return t.voltageLevel === VoltageLevel.LT ? 'kW' : 'kVA';
  }

  protected get isAgriculture(): boolean {
    return this.selected()?.fixedChargeBasis === FixedChargeBasis.PerKwOrHp;
  }

  protected get energyUnitLabel(): string {
    return this.selected()?.energyUnit === EnergyUnit.Kvah ? 'kVAh' : 'kWh';
  }

  protected get isHighVoltage(): boolean {
    return (this.selected()?.voltageLevel ?? VoltageLevel.LT) !== VoltageLevel.LT;
  }

  protected bandLabels(): string[] {
    return this.result()?.tariff.bands ?? ['Normal', 'Peak', 'Off-Peak'];
  }

  protected pctOfTotal(value: number): number {
    const total = this.segments().reduce((sum, s) => sum + s.value, 0);
    return total === 0 ? 0 : (value / total) * 100;
  }

  runSimulation(): void {
    if (!this.selectedTariffId) {
      this.simulationError.set('Select a tariff first.');
      return;
    }
    if (![this.consumptionKwh, this.monthToDateKwh, this.load].every((v) => v >= 0)) {
      this.simulationError.set('Consumption and load must be zero or positive.');
      return;
    }

    this.simulating.set(true);
    this.simulationError.set(null);

    const bands = Object.fromEntries(Object.entries(this.bandKvah).filter(([, v]) => v > 0));
    this.calculationService
      .simulate({
        tariffId: this.selectedTariffId,
        consumptionKwh: this.consumptionKwh,
        monthToDateKwh: this.monthToDateKwh,
        connectedLoadOrContractDemand: this.load,
        loadInHp: this.isAgriculture && this.loadInHp,
        meteredOnLtSide: this.meteredOnLtSide,
        tmcMonthly: this.tmcMonthly,
        cpmcMonthly: this.cpmcMonthly,
        priorMonthEnergyCharge: this.priorMonthEnergyCharge,
        fppasRatePercent: this.fppasRatePercent,
        touKvahByBand: Object.keys(bands).length > 0 ? bands : undefined,
      })
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.simulating.set(false);
        },
        error: (err) => {
          this.simulating.set(false);
          this.simulationError.set(err?.error?.error ?? 'Could not run this simulation.');
        },
      });
  }

  applyScenario(s: Scenario): void {
    s.apply();
    this.runSimulation();
  }

  reset(): void {
    this.consumptionKwh = 30;
    this.monthToDateKwh = 0;
    this.load = 2;
    this.loadInHp = false;
    this.meteredOnLtSide = false;
    this.tmcMonthly = this.cpmcMonthly = this.priorMonthEnergyCharge = this.fppasRatePercent = 0;
    for (const k of Object.keys(this.bandKvah)) delete this.bandKvah[k];
    this.result.set(null);
    this.simulationError.set(null);
  }
}
