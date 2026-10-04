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
  template: `
    <h2 mat-dialog-title>מישהו אחר עדכן את הפנייה בינתיים</h2>
    <mat-dialog-content>
      <p class="subject">{{ data.current.title }}</p>
      <dl class="facts">
        <dt>מצב נוכחי</dt>
        <dd><span class="badge" [attr.data-status]="data.current.status">{{ data.current.status | statusLabel }}</span></dd>
        @if (data.lastChange; as change) {
          <dt>עודכן על ידי</dt>
          <dd>{{ change.changedBy }}, {{ change.changedAt | date: 'dd/MM/yyyy HH:mm' }}</dd>
        }
        <dt>השינוי שלך</dt>
        <dd>{{ data.attempted | statusLabel }} – <strong>לא נשמר</strong></dd>
      </dl>
      @if (!canRetry) {
        <p class="muted">
          לא ניתן להעביר את הפנייה ל"{{ data.attempted | statusLabel }}" מהמצב הנוכחי שלה.
        </p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="'reload'">הצגת המצב העדכני</button>
      @if (canRetry) {
        <button mat-flat-button type="button" [mat-dialog-close]="'retry'">
          להעביר בכל זאת ל"{{ data.attempted | statusLabel }}"
        </button>
      }
    </mat-dialog-actions>
  `,
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
