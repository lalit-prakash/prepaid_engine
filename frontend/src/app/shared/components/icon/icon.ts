import { Component, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

/** Small line-icon set (stroke-based, currentColor) used everywhere the app
 * used to fall back on emoji glyphs — emoji render inconsistently across
 * platforms and read as decoration rather than a real product icon set. */
const ICONS: Record<string, string> = {
  home: '<path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V21h14V9.5"/><path d="M9.5 21v-6h5v6"/>',
  users: '<circle cx="9" cy="8" r="3.2"/><path d="M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6"/><circle cx="17" cy="9" r="2.6"/><path d="M15.5 14.2c2.6.4 4.5 2.4 5 5.8"/>',
  bolt: '<path d="M13 2 4.5 14h6l-1.5 8L19.5 10h-6z"/>',
  plug: '<path d="M9 3v5M15 3v5M6.5 8h11l-1 4a6 6 0 0 1-9 0z"/><path d="M12 16v5"/>',
  refresh: '<path d="M20 11a8 8 0 0 0-14.9-3.5M4 4v5h5"/><path d="M4 13a8 8 0 0 0 14.9 3.5M20 20v-5h-5"/>',
  wrench: '<path d="M14.7 6.3a4 4 0 0 0-5.4 5.1L3 18l3 3 6.6-6.3a4 4 0 0 0 5.1-5.4l-2.8 2.8-2-2z"/>',
  toolbox: '<rect x="3" y="9" width="18" height="10" rx="1.5"/><path d="M8 9V6.5A2.5 2.5 0 0 1 10.5 4h3A2.5 2.5 0 0 1 16 6.5V9"/><path d="M3 13.5h18"/>',
  repeat: '<path d="M17 2 21 6l-4 4"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><path d="M7 22 3 18l4-4"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/>',
  alert: '<path d="M12 3 2 20h20L12 3z"/><path d="M12 10v4"/><circle cx="12" cy="17" r="0.6" fill="currentColor" stroke="none"/>',
  trend: '<path d="M3 17 9 11l4 4 8-8"/><path d="M15 7h6v6"/>',
  receipt: '<path d="M6 2h12v20l-3-2-3 2-3-2-3 2z"/><path d="M9 7h6M9 11h6M9 15h4"/>',
  chart: '<path d="M4 20V10M11 20V4M18 20v-7"/>',
  document: '<path d="M6 2h9l4 4v16H6z"/><path d="M14 2v5h5"/><path d="M9 12h6M9 16h6"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/>',
  pause: '<rect x="6" y="4" width="4" height="16" rx="1"/><rect x="14" y="4" width="4" height="16" rx="1"/>',
  calculator: '<rect x="4" y="2" width="16" height="20" rx="2"/><path d="M8 6h8M8 11h1M12 11h1M16 11h1M8 15h1M12 15h1M16 15h1M8 19h1M12 19h1M16 19h1"/>',
  gear: '<circle cx="12" cy="12" r="3.2"/><path d="M19 12a7 7 0 0 0-.15-1.4l2-1.6-2-3.4-2.4.8a7 7 0 0 0-2.4-1.4L13.6 2h-3.2l-.45 2.9a7 7 0 0 0-2.4 1.4l-2.4-.8-2 3.4 2 1.6a7 7 0 0 0 0 2.8l-2 1.6 2 3.4 2.4-.8a7 7 0 0 0 2.4 1.4l.45 3h3.2l.45-3a7 7 0 0 0 2.4-1.4l2.4.8 2-3.4-2-1.6c.1-.4.15-.9.15-1.4z"/>',
  lock: '<rect x="4" y="10" width="16" height="10" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
  link: '<path d="M9 15 15 9"/><path d="M10.5 6.5 13 4a4 4 0 1 1 5.7 5.7l-2.7 2.7"/><path d="M13.5 17.5 11 20a4 4 0 1 1-5.7-5.7l2.7-2.7"/>',
  flask: '<path d="M9 2h6M10 2v6.5L4.5 19a2 2 0 0 0 1.8 3h11.4a2 2 0 0 0 1.8-3L14 8.5V2"/><path d="M7.5 15h9"/>',
  scroll: '<path d="M6 3h12v15a3 3 0 0 1-3 3H8a3 3 0 0 1-2-5V3z"/><path d="M6 3a3 3 0 0 0-3 3v0a3 3 0 0 0 3 3"/><path d="M9 8h6M9 11h6"/>',
  bell: '<path d="M6 10a6 6 0 0 1 12 0c0 5 2 6 2 6H4s2-1 2-6z"/><path d="M10 19a2 2 0 0 0 4 0"/>',
  robot: '<rect x="4" y="8" width="16" height="12" rx="2"/><path d="M12 8V4"/><circle cx="12" cy="3" r="1"/><circle cx="9" cy="14" r="1.2"/><circle cx="15" cy="14" r="1.2"/><path d="M9 18h6"/>',
  wallet: '<path d="M3 7a2 2 0 0 1 2-2h13a1 1 0 0 1 1 1v2"/><rect x="3" y="7" width="18" height="13" rx="2"/><circle cx="16" cy="13.5" r="1.4"/>',
  signal: '<path d="M4 20h2v-4H4z"/><path d="M9 20h2v-8H9z"/><path d="M14 20h2v-12h-2z"/><path d="M19 20h2v-16h-2z"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/>',
  help: '<circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.9.4-1.5 1-1.5 2.2"/><circle cx="12" cy="17" r="0.6" fill="currentColor" stroke="none"/>',
  building: '<rect x="4" y="3" width="16" height="18" rx="1"/><path d="M8 7h1M8 11h1M8 15h1M15 7h1M15 11h1M15 15h1"/><path d="M10 21v-4h4v4"/>',
  chevronDown: '<path d="m6 9 6 6 6-6"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  shield: '<path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6z"/><path d="m9 12 2 2 4-4"/>',
  mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m4 7 8 6 8-6"/>',
  eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/>',
  eyeOff: '<path d="M3 3l18 18"/><path d="M10.6 5.2A10.6 10.6 0 0 1 12 5c6.5 0 10 7 10 7a17.9 17.9 0 0 1-3.2 4.1M6.5 6.6C4 8.3 2 12 2 12s3.5 7 10 7c1.4 0 2.7-.3 3.8-.8"/><path d="M9.5 10a3 3 0 0 0 4.2 4.2"/>',
};

@Component({
  selector: 'pe-icon',
  template: `<span class="pe-icon" [innerHTML]="svg()"></span>`,
  styles: [`
    .pe-icon { display: inline-flex; align-items: center; justify-content: center; line-height: 0; }
    .pe-icon svg { width: 1em; height: 1em; }
  `],
})
export class Icon {
  readonly name = input.required<string>();
  readonly size = input<number>(18);

  constructor(private readonly sanitizer: DomSanitizer) {}

  protected svg(): SafeHtml {
    const inner = ICONS[this.name()] ?? '';
    const markup = `<svg viewBox="0 0 24 24" width="${this.size()}" height="${this.size()}" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">${inner}</svg>`;
    return this.sanitizer.bypassSecurityTrustHtml(markup);
  }
}
