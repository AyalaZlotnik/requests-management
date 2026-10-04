import { Injectable, effect, signal } from '@angular/core';

const STORAGE_KEY = 'requests-ui.user';
export const DEFAULT_USER_NAME = 'משתמש לדוגמה';

/**
 * The name sent as "changedBy". There is no authentication in this exercise,
 * so the user types a name in the header; in production this would come from the identity token.
 */
@Injectable({ providedIn: 'root' })
export class CurrentUser {
  readonly name = signal(readStored() ?? DEFAULT_USER_NAME);

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

/** The remembered name, or null if there is none – an empty or blank value counts as none. */
function readStored(): string | null {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)?.trim();
    return stored ? stored : null;
  } catch {
    return null;
  }
}
