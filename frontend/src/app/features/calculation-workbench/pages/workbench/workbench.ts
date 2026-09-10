import { DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TariffService } from '../../../../core/services/tariff.service';
import { CalculationService } from '../../../../core/services/calculation.service';
import { TariffSummary } from '../../../../core/models/tariff.model';
import { SimulateChargeResult } from '../../../../core/models/calculation.model';
import { categoryLabel } from '../../../../shared/utils/category-label';

/**
 * SIMULATION ONLY — previews a charge calculation for an arbitrary tariff/consumption/load
 * combination. Never touches a real consumer, bill, or wallet, and never computes anything
 * itself: every figure comes from POST /api/v1/calculation-workbench/simulate, which delegates
 * to the same domain methods production billing uses (see CalculationService's doc comment).
 * The frontend calculation rule (docs/frontend-scope.md) is why this isn't computed in TS.
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
  protected consumptionKwh = 150;
  protected connectedLoadOrContractDemand = 2;

  protected readonly simulating = signal(false);
  protected readonly result = signal<SimulateChargeResult | null>(null);
  protected readonly simulationError = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;

  constructor(
    private readonly tariffService: TariffService,
    private readonly calculationService: CalculationService,
  ) {}

  ngOnInit(): void {
    this.tariffService.list().subscribe({
      next: (tariffs) => {
        this.tariffs.set(tariffs);
        this.loadingTariffs.set(false);
        if (tariffs.length > 0) {
          this.selectedTariffId = tariffs[0].id;
        }
      },
      error: () => {
        this.tariffLoadError.set('Could not load tariffs from the API.');
        this.loadingTariffs.set(false);
      },
    });
  }

  runSimulation(): void {
    if (!this.selectedTariffId) {
      this.simulationError.set('Select a tariff first.');
      return;
    }
    if (!(this.consumptionKwh >= 0) || !(this.connectedLoadOrContractDemand >= 0)) {
      this.simulationError.set('Consumption and load must be zero or positive.');
      return;
    }

    this.simulating.set(true);
    this.simulationError.set(null);
    this.result.set(null);

    this.calculationService
      .simulate({
        tariffId: this.selectedTariffId,
        consumptionKwh: this.consumptionKwh,
        connectedLoadOrContractDemand: this.connectedLoadOrContractDemand,
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

  reset(): void {
    this.consumptionKwh = 150;
    this.connectedLoadOrContractDemand = 2;
    this.result.set(null);
    this.simulationError.set(null);
  }
}
