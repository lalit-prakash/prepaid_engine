import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { BillStatus, ConnectionStatus, ConsumerDetail, WalletTransactionType } from '../../../../core/models/consumer.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

type RechargeOutcome =
  | { kind: 'success'; replayed: boolean; balance: number }
  | { kind: 'pending'; message: string }
  | { kind: 'declined'; message: string }
  | { kind: 'unavailable'; message: string }
  | { kind: 'invalid'; message: string };

/**
 * Consumer 360 — the primary investigation screen. Financial figures are
 * deliberately kept apart per the RMS-source-of-truth boundary: "RMS Wallet
 * Balance" is the only value ever labeled "wallet"; everything else
 * (charges, rebate, FPPAS, ...) is labeled as an engine-calculated figure.
 */
@Component({
  selector: 'pe-consumer-360',
  imports: [FormsModule, StatusBadge, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './consumer-360.html',
  styleUrl: './consumer-360.scss',
})
export class Consumer360 implements OnInit {
  protected readonly consumer = signal<ConsumerDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly ConnectionStatus = ConnectionStatus;
  protected readonly BillStatus = BillStatus;
  protected readonly WalletTransactionType = WalletTransactionType;

  protected accountNumber = '';
  protected rechargeAmount = 300;
  protected idempotencyKey = '';
  protected readonly rechargeSubmitting = signal(false);
  protected readonly rechargeOutcome = signal<RechargeOutcome | null>(null);

  constructor(
    private readonly route: ActivatedRoute,
    private readonly consumerService: ConsumerService,
  ) {}

  ngOnInit(): void {
    this.accountNumber = this.route.snapshot.paramMap.get('accountNumber') ?? '';
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.consumerService.getByAccountNumber(this.accountNumber).subscribe({
      next: (consumer) => {
        this.consumer.set(consumer);
        this.loading.set(false);
        this.idempotencyKey = this.freshKey();
      },
      error: (err) => {
        this.error.set(
          err?.status === 404
            ? `No consumer found for account "${this.accountNumber}".`
            : 'Could not load this consumer from the API.',
        );
        this.loading.set(false);
      },
    });
  }

  private freshKey(): string {
    return `ui-${Date.now()}`;
  }

  protected get latestBill() {
    const bills = this.consumer()?.bills ?? [];
    return bills.length ? bills[bills.length - 1] : null;
  }

  submitRecharge(): void {
    if (!(this.rechargeAmount > 0) || !this.idempotencyKey.trim()) {
      this.rechargeOutcome.set({ kind: 'invalid', message: 'Enter a valid amount and idempotency key.' });
      return;
    }
    this.rechargeSubmitting.set(true);
    this.rechargeOutcome.set(null);

    this.consumerService
      .recharge(this.accountNumber, { amount: this.rechargeAmount, idempotencyKey: this.idempotencyKey.trim() })
      .subscribe({
        next: (response) => {
          this.rechargeSubmitting.set(false);
          const body = response.body!;
          if (response.status === 202) {
            this.rechargeOutcome.set({ kind: 'pending', message: (body as any).message ?? 'RMS is still processing this recharge.' });
          } else {
            this.rechargeOutcome.set({ kind: 'success', replayed: !!body.replayed, balance: body.walletBalance });
            this.idempotencyKey = this.freshKey();
            this.load(); // re-fetch so the ledger/bill table reflect the real, post-recharge state
          }
        },
        error: (err) => {
          this.rechargeSubmitting.set(false);
          const status = err?.status;
          if (status === 402) {
            this.rechargeOutcome.set({ kind: 'declined', message: err?.error?.message ?? 'RMS declined this recharge.' });
          } else if (status === 503) {
            this.rechargeOutcome.set({ kind: 'unavailable', message: err?.error?.detail ?? 'RMS is unavailable — try again shortly.' });
          } else if (status === 400) {
            this.rechargeOutcome.set({ kind: 'invalid', message: err?.error?.error ?? 'Invalid request.' });
          } else {
            this.rechargeOutcome.set({ kind: 'invalid', message: `Unexpected response (${status ?? 'network error'}).` });
          }
        },
      });
  }
}
