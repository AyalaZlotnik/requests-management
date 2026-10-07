import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { RequestDetails, RequestStatus, StatusHistoryEntry } from '../../core/models/request.models';
import { StatusLabelPipe } from './status-label.pipe';

export interface ConflictDialogData {
  /** The status the user tried to set. */
  attempted: RequestStatus;
  /** The request as stored now (from the 409 body). */
  current: RequestDetails;
  /** Who made the change that won, and when. */
  lastChange: StatusHistoryEntry | null;
}

/** "reload" – show the current state; "retry" – apply my status on top of the current version. */
export type ConflictChoice = 'reload' | 'retry';

/**
 * Shown when the server rejects an update with 409. It explains what happened and lets the user decide –
 * the UI never picks a winner by itself.
 */
@Component({
  selector: 'app-conflict-dialog',
  imports: [MatDialogModule, MatButtonModule, DatePipe, StatusLabelPipe],
  templateUrl: './conflict-dialog.component.html',
  styles: `
    .subject { font-weight: 600; margin-top: 0; }
    .facts { display: grid; grid-template-columns: auto 1fr; gap: 6px 12px; margin: 0 0 8px; }
    .facts dt { color: var(--muted); }
    .facts dd { margin: 0; }
  `,
})
export class ConflictDialogComponent {
  protected readonly data = inject<ConflictDialogData>(MAT_DIALOG_DATA);

  /** Retrying makes sense only if the workflow still allows the user's status from the current one. */
  protected readonly canRetry = this.data.current.allowedNextStatuses.includes(this.data.attempted);
}
