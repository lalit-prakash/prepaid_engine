import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BillService } from '../../../../core/services/bill.service';
import { BillStatus } from '../../../../core/models/consumer.model';
import { BillDetail as BillDetailModel } from '../../../../core/models/bill.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * The detailed calculation view for a single bill, real end to end
 * (GET /api/v1/bills/{id}). Shows the full charge breakdown as a trace —
 * inputs -> tariff -> rate -> formula -> result -> adjustments -> final —
 * rather than only a total, so every production bill stays explainable.
 */
@Component({
  selector: 'pe-bill-detail',
  imports: [StatusBadge, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './bill-detail.html',
  styleUrl: './bill-detail.scss',
})
export class BillDetail implements OnInit {
  protected readonly bill = signal<BillDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly BillStatus = BillStatus;

  private billId = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly billService: BillService,
  ) {}

  ngOnInit(): void {
    this.billId = this.route.snapshot.paramMap.get('id') ?? '';
    this.billService.getById(this.billId).subscribe({
      next: (bill) => {
        this.bill.set(bill);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(
          err?.status === 404 ? 'This bill could not be found.' : 'Could not load this bill from the API.',
        );
        this.loading.set(false);
      },
    });
  }
}
