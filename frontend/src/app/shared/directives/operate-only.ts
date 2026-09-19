import { Directive, ElementRef, effect, inject } from '@angular/core';
import { AuthService } from '../../core/services/auth.service';

/**
 * Hides an element for roles that cannot perform operational actions (ReadOnly, Utility). This only
 * removes buttons a user could not use anyway; the API enforces the same rule with its "Operations"
 * policy and answers 403 regardless of what the browser shows.
 */
@Directive({ selector: '[peOperate]' })
export class OperateOnly {
  constructor() {
    const el = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const auth = inject(AuthService);
    effect(() => {
      el.style.display = auth.canOperate() ? '' : 'none';
    });
  }
}
