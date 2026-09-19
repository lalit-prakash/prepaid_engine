import { CanDeactivateFn } from '@angular/router';

export interface HasUnsavedChanges {
  canLeave(): boolean;
}

/** Asks before leaving a form with edits that were never saved or submitted. */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) =>
  component.canLeave() || window.confirm('You have unsaved changes. Leave this page and discard them?');
