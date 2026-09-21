import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingItem, SettingsResponse, SettingsService } from '../../../../core/services/settings.service';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * System Settings. Shows every setting the engine lets an administrator change (grouped), what it is now, its default and who last changed
 * it, and a few read-only facts about how the system is set up. Changes are saved together, checked by the API, audited, and apply
 * immediately with no restart. A blank value (where allowed) or "Reset" puts a setting back to its default. Admin and IT only.
 */
@Component({
  selector: 'pe-system-settings',
  imports: [FormsModule, DatePipe, StatusBadge],
  templateUrl: './system-settings.html',
  styleUrl: './system-settings.scss',
})
export class SystemSettings implements OnInit {
  protected readonly data = signal<SettingsResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** What is typed in each box, by key. */
  protected values: Record<string, string> = {};
  /** What each box held when it was loaded, so only changed settings are sent. */
  private original: Record<string, string> = {};

  protected readonly saving = signal(false);
  protected readonly problems = signal<string[]>([]);
  protected readonly notice = signal<string | null>(null);

  constructor(private readonly settingsService: SettingsService) {}

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.settingsService.get().subscribe({
      next: (d) => {
        this.data.set(d);
        this.values = {};
        for (const g of d.groups) for (const s of g.settings) this.values[s.key] = s.value ?? '';
        this.original = { ...this.values };
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.status === 403 ? 'Only Admin and IT users can change system settings.' : 'Could not load the settings from the API.');
        this.loading.set(false);
      },
    });
  }

  protected isChanged(key: string): boolean {
    return (this.values[key] ?? '').trim() !== (this.original[key] ?? '').trim();
  }

  protected get changedCount(): number {
    return Object.keys(this.values).filter((k) => this.isChanged(k)).length;
  }

  protected save(): void {
    const changes: Record<string, string> = {};
    for (const key of Object.keys(this.values)) if (this.isChanged(key)) changes[key] = this.values[key].trim();
    if (Object.keys(changes).length === 0) return;
    this.submit(changes);
  }

  /** Puts one setting back to its default straight away. */
  protected reset(s: SettingItem): void {
    this.submit({ [s.key]: '' });
  }

  private submit(values: Record<string, string>): void {
    this.saving.set(true);
    this.problems.set([]);
    this.notice.set(null);
    this.settingsService.save(values).subscribe({
      next: (r) => {
        this.saving.set(false);
        this.notice.set(r.message);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.problems.set(err?.error?.problems ?? [err?.error?.error ?? 'Could not save the settings.']);
      },
    });
  }

  protected discard(): void {
    this.values = { ...this.original };
    this.problems.set([]);
  }

  protected range(s: SettingItem): string {
    return `${s.min} to ${s.max}${s.unit === '%' ? '%' : ' ' + s.unit}`;
  }
}
