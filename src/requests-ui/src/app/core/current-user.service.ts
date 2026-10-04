import { Injectable, effect, signal } from '@angular/core';

const STORAGE_KEY = 'requests-ui.user';

/**
 * The name sent as "changedBy". There is no authentication in this exercise,
 * so the user types a name in the header; in production this would come from the identity token.
 */
@Injectable({ providedIn: 'root' })
export class CurrentUser {
  readonly name = signal(readStored() ?? 'משתמש לדוגמה');
  /** False while the name field is empty or too long – updates are disabled until it is fixed. */
  readonly nameValid = signal(true);

  constructor() {
    effect(() => {
      try {
        localStorage.setItem(STORAGE_KEY, this.name());
      } catch {
        // Storage not available – the name just isn't remembered.
      }
    });
  }
}

function readStored(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
