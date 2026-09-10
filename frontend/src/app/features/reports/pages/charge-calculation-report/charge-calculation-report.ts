import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { BillService } from '../../../../core/services/bill.service';
import { ConsumerSummary } from '../../../../core/models/consumer.model';
import { BillDetail } from '../../../../core/models/bill.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { categoryLabel } from '../../../../shared/utils/category-label';
import { exportToCsv } from '../../../../shared/utils/csv-export';

/**
 * Individual Charge Calculation Report — every real bill for one consumer, each with its full
 * calculation trace. Fetches the consumer's bill IDs from GET /api/v1/consumers/{account}, then
 * the full breakdown for each via GET /api/v1/bills/{id} (BillSummary alone doesn't carry
 * consumption/reading data — only Bill Detail does).
 */
@Component({
  selector: 'pe-charge-calculation-report',
  imports: [FormsModule, StatusBadge, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './charge-calculation-report.html',
  styleUrl: './charge-calculation-report.scss',
})
export class ChargeCalculationReport implements OnInit {
  protected readonly consumers = signal<ConsumerSummary[]>([]);
  protected readonly loadingConsumers = signal(true);
  protected readonly consumerLoadError = signal<string | null>(null);

  protected selectedAccountNumber = '';
  protected readonly loadingBills = signal(false);
  protected readonly bills = signal<BillDetail[]>([]);
  protected readonly billLoadError = signal<string | null>(null);
  protected readonly hasRun = signal(false);
  protected readonly categoryLabel = categoryLabel;

  constructor(
    private readonly consumerService: ConsumerService,
    private readonly billService: BillService,
  ) {}

  ngOnInit(): void {
    this.consumerService.list().subscribe({
      next: (consumers) => {
        this.consumers.set(consumers);
        this.loadingConsumers.set(false);
        if (consumers.length > 0) {
          this.selectedAccountNumber = consumers[0].accountNumber;
        }
      },
      error: () => {
        this.consumerLoadError.set('Could not load consumers from the API.');
        this.loadingConsumers.set(false);
      },
    });
  }

  runReport(): void {
    if (!this.selectedAccountNumber) return;

    this.loadingBills.set(true);
    this.billLoadError.set(null);
    this.hasRun.set(true);
    this.bills.set([]);

    this.consumerService.getByAccountNumber(this.selectedAccountNumber).subscribe({
      next: (consumer) => {
        if (consumer.bills.length === 0) {
          this.loadingBills.set(false);
          return;
        }
        const detailRequests = consumer.bills.map((b) =>
          this.billService.getById(b.id).pipe(catchError(() => of(null))),
        );
        forkJoin(detailRequests).subscribe((details) => {
          this.bills.set(details.filter((d): d is BillDetail => d !== null));
          this.loadingBills.set(false);
        });
      },
      error: () => {
        this.billLoadError.set('Could not load this consumer from the API.');
        this.loadingBills.set(false);
      },
    });
  }

  export(): void {
    exportToCsv(
      `charge-calculation-report-${this.selectedAccountNumber}.csv`,
      [
        'Generated', 'Account', 'Consumer', 'Tariff', 'Consumption (kWh)', 'Gross Energy', 'Rebate',
        'Net Energy', 'Fixed', 'Duty', 'FPPAS', 'TMC', 'CPMC', 'Arrears', 'Total', 'Status',
      ],
      this.bills().map((b) => [
        b.generatedAt,
        b.consumer.accountNumber,
        b.consumer.name,
        b.tariff.name,
        b.reading.consumptionKwh.toFixed(2),
        b.energyChargeGross.toFixed(2),
        b.prepaidRebateAmount.toFixed(2),
        b.energyChargeNet.toFixed(2),
        b.fixedCharge.toFixed(2),
        b.electricityDutyAmount.toFixed(2),
        b.fppasAmount.toFixed(2),
        b.tmcAmount.toFixed(2),
        b.cpmcAmount.toFixed(2),
        b.arrearsAmount.toFixed(2),
        b.amount.toFixed(2),
        String(b.status),
      ]),
    );
  }
}
