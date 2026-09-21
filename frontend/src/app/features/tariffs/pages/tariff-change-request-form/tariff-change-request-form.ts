import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConsumerCategory } from '../../../../core/models/bill.model';
import {
  CreateTariffChangeRequestBody,
  TariffChangeRequestStatus,
  TariffSlabInput,
  TouPeriodInput,
} from '../../../../core/models/tariff-change-request.model';
import { TariffChangeRequestService } from '../../../../core/services/tariff-change-request.service';
import { TariffService } from '../../../../core/services/tariff.service';
import { categoryLabel, ALL_CATEGORIES } from '../../../../shared/utils/category-label';
import { HasUnsavedChanges } from '../../../../core/guards/unsaved-changes.guard';

/**
 * IT's create/edit workspace for a proposed tariff change (Basic Info / Applicability / Energy
 * Slabs / ToD / Review). Never activates anything directly — Save Draft and Submit for Approval
 * are the only two actions this screen can take; approval/scheduling happens on the Utility
 * approval workspace (tariff-change-request-detail). Handles three entry points:
 *   - /tariffs/change-requests/new                       — brand-new tariff, no supersession
 *   - /tariffs/change-requests/new?supersedes=<tariffId>  — revision of an existing active tariff
 *   - /tariffs/change-requests/:id/edit                   — resume a Draft or revise a Rejected one
 */
@Component({
  selector: 'pe-tariff-change-request-form',
  imports: [FormsModule, RouterLink],
  templateUrl: './tariff-change-request-form.html',
  styleUrl: './tariff-change-request-form.scss',
})
export class TariffChangeRequestForm implements OnInit, HasUnsavedChanges {
  private dirty = false;

  markDirty(): void {
    this.dirty = true;
  }

  canLeave(): boolean {
    return !this.dirty;
  }

  /** Add/Remove slab and ToD buttons change the proposal without firing an input event. */
  onFormClick(event: Event): void {
    const button = (event.target as HTMLElement).closest('button');
    if (button && /Add|Remove/.test(button.textContent ?? '')) this.markDirty();
  }

  protected readonly categories = ALL_CATEGORIES;
  protected readonly categoryLabel = categoryLabel;

  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly validationErrors = signal<string[]>([]);

  protected requestId: string | null = null;
  protected supersedesTariffId: string | null = null;
  protected currentTariffName: string | null = null;

  protected proposedName = '';
  protected proposedCategory: ConsumerCategory = ConsumerCategory.Domestic;
  protected fixedCharge = 0;
  protected rebatePercent = 0;
  protected emergencyCredit = 0;
  protected minVendSingle: number | null = null;
  protected maxVendSingle: number | null = null;
  protected minVendThree: number | null = null;
  protected maxVendThree: number | null = null;
  protected slabs: TariffSlabInput[] = [{ fromKwh: 0, upToKwh: null, ratePerKwh: 0 }];
  protected touPeriods: TouPeriodInput[] = [];
  protected changeReason = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly changeRequestService: TariffChangeRequestService,
    private readonly tariffService: TariffService,
  ) {}

  ngOnInit(): void {
    this.requestId = this.route.snapshot.paramMap.get('id');
    this.supersedesTariffId = this.route.snapshot.queryParamMap.get('supersedes');

    if (this.requestId) {
      this.loading.set(true);
      this.changeRequestService.getById(this.requestId).subscribe({
        next: (detail) => {
          this.supersedesTariffId = detail.supersedesTariffId;
          this.currentTariffName = detail.current?.name ?? null;
          this.proposedName = detail.proposed.proposedName;
          this.proposedCategory = detail.proposed.proposedCategory;
          this.fixedCharge = detail.proposed.proposedFixedChargePerUnitPerMonth;
          this.rebatePercent = detail.proposed.proposedPrepaidEnergyRebatePercent;
          this.emergencyCredit = detail.proposed.proposedEmergencyCreditLimit;
          this.minVendSingle = detail.proposed.proposedMinVendAmountSinglePhase;
          this.maxVendSingle = detail.proposed.proposedMaxVendAmountSinglePhase;
          this.minVendThree = detail.proposed.proposedMinVendAmountThreePhase;
          this.maxVendThree = detail.proposed.proposedMaxVendAmountThreePhase;
          this.slabs = detail.proposed.slabs.map((s) => ({ fromKwh: s.fromKwh, upToKwh: s.upToKwh, ratePerKwh: s.ratePerKwh }));
          this.touPeriods = detail.proposed.touPeriods.map((t) => ({
            label: t.label,
            startTime: t.startTime.slice(0, 5),
            endTime: t.endTime.slice(0, 5),
            ratePerKvah: t.ratePerKvah,
          }));
          this.changeReason = '';
          this.loading.set(false);
        },
        error: () => {
          this.loadError.set('Could not load this draft from the API.');
          this.loading.set(false);
        },
      });
    } else if (this.supersedesTariffId) {
      this.loading.set(true);
      this.tariffService.getById(this.supersedesTariffId).subscribe({
        next: (tariff) => {
          this.currentTariffName = tariff.name;
          this.proposedName = tariff.name;
          this.proposedCategory = tariff.category;
          this.fixedCharge = tariff.fixedChargePerUnitPerMonth;
          this.rebatePercent = tariff.prepaidEnergyRebatePercent;
          this.emergencyCredit = tariff.emergencyCreditLimit;
          this.minVendSingle = tariff.minVendAmountSinglePhase;
          this.maxVendSingle = tariff.maxVendAmountSinglePhase;
          this.minVendThree = tariff.minVendAmountThreePhase;
          this.maxVendThree = tariff.maxVendAmountThreePhase;
          this.slabs = tariff.slabs.map((s) => ({ fromKwh: s.fromKwh, upToKwh: s.upToKwh, ratePerKwh: s.ratePerKwh }));
          this.touPeriods = tariff.touPeriods.map((t) => ({
            label: t.label,
            startTime: t.startTime.slice(0, 5),
            endTime: t.endTime.slice(0, 5),
            ratePerKvah: t.ratePerKvah,
          }));
          this.loading.set(false);
        },
        error: () => {
          this.loadError.set('Could not load the tariff being revised.');
          this.loading.set(false);
        },
      });
    }
  }

  addSlab(): void {
    this.slabs = [...this.slabs, { fromKwh: 0, upToKwh: null, ratePerKwh: 0 }];
  }

  removeSlab(index: number): void {
    this.slabs = this.slabs.filter((_, i) => i !== index);
  }

  addTouPeriod(): void {
    this.touPeriods = [...this.touPeriods, { label: '', startTime: '00:00', endTime: '00:00', ratePerKvah: 0 }];
  }

  removeTouPeriod(index: number): void {
    this.touPeriods = this.touPeriods.filter((_, i) => i !== index);
  }

  private buildBody(): CreateTariffChangeRequestBody {
    return {
      supersedesTariffId: this.supersedesTariffId,
      proposedName: this.proposedName,
      proposedCategory: this.proposedCategory,
      proposedSlabs: this.slabs,
      proposedFixedChargePerUnitPerMonth: this.fixedCharge,
      proposedPrepaidEnergyRebatePercent: this.rebatePercent,
      proposedEmergencyCreditLimit: this.emergencyCredit,
      proposedMinVendAmountSinglePhase: this.minVendSingle,
      proposedMaxVendAmountSinglePhase: this.maxVendSingle,
      proposedMinVendAmountThreePhase: this.minVendThree,
      proposedMaxVendAmountThreePhase: this.maxVendThree,
      proposedTouPeriods: this.touPeriods,
    };
  }

  saveDraft(): void {
    this.formError.set(null);
    this.validationErrors.set([]);
    this.saving.set(true);
    const body = this.buildBody();

    const onSuccess = (result: { id: string }) => {
      this.saving.set(false);
      this.dirty = false;
      this.router.navigate(['/tariffs/change-requests', result.id, 'edit']);
    };
    const onError = (err: unknown) => {
      this.saving.set(false);
      this.formError.set(this.extractError(err, 'Could not save this draft.'));
    };

    if (this.requestId) {
      const { supersedesTariffId: _omit, ...updateBody } = body;
      this.changeRequestService.saveDraft(this.requestId, updateBody).subscribe({ next: () => onSuccess({ id: this.requestId! }), error: onError });
    } else {
      this.changeRequestService.create(body).subscribe({ next: onSuccess, error: onError });
    }
  }

  submitForApproval(): void {
    if (!this.changeReason.trim()) {
      this.formError.set('A change reason is required to submit for approval.');
      return;
    }
    this.formError.set(null);
    this.validationErrors.set([]);
    this.saving.set(true);

    const body = this.buildBody();
    const proceedToSubmit = (id: string) => {
      this.changeRequestService.submit(id, this.changeReason).subscribe({
        next: () => {
          this.saving.set(false);
          this.dirty = false;
          this.router.navigate(['/tariffs/change-requests', id]);
        },
        error: (err) => {
          this.saving.set(false);
          const errors = err?.error?.errors as string[] | undefined;
          if (errors?.length) {
            this.validationErrors.set(errors);
          } else {
            this.formError.set(this.extractError(err, 'Could not submit for approval.'));
          }
        },
      });
    };

    if (this.requestId) {
      const { supersedesTariffId: _omit, ...updateBody } = body;
      this.changeRequestService.saveDraft(this.requestId, updateBody).subscribe({
        next: () => proceedToSubmit(this.requestId!),
        error: (err) => {
          this.saving.set(false);
          this.formError.set(this.extractError(err, 'Could not save changes before submitting.'));
        },
      });
    } else {
      this.changeRequestService.create(body).subscribe({
        next: (result) => proceedToSubmit(result.id),
        error: (err) => {
          this.saving.set(false);
          this.formError.set(this.extractError(err, 'Could not create this change request.'));
        },
      });
    }
  }

  private extractError(err: unknown, fallback: string): string {
    const anyErr = err as { error?: { error?: string } };
    return anyErr?.error?.error ?? fallback;
  }
}
