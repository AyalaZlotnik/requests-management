import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { REQUEST_STATUSES, RequestStatus } from '../../core/models/request.models';
import { StatusLabelPipe } from './status-label.pipe';

/** Shown while rows are selected: pick a status and apply it to all of them (up to 100). */
@Component({
  selector: 'app-bulk-status-bar',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatSelectModule, StatusLabelPipe],
  template: `
    <div class="bulk-bar" role="region" aria-label="עדכון מרוכז">
      <strong>{{ count() }} פניות נבחרו</strong>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>סטטוס חדש</mat-label>
        <mat-select [(ngModel)]="status">
          @for (s of statuses; track s) {
            <mat-option [value]="s">{{ s | statusLabel }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <button mat-flat-button type="button" [disabled]="busy() || count() > max" (click)="apply.emit(status)">
        {{ busy() ? 'מעדכן…' : 'עדכון כל הנבחרות' }}
      </button>
      <button mat-button type="button" (click)="clear.emit()">ביטול הבחירה</button>
      @if (count() > max) {
        <span class="error-text">ניתן לעדכן עד {{ max }} פניות בבת אחת.</span>
      }
    </div>
  `,
})
export class BulkStatusBarComponent {
  readonly count = input.required<number>();
  readonly busy = input(false);
  readonly apply = output<RequestStatus>();
  readonly clear = output<void>();

  protected readonly statuses = REQUEST_STATUSES;
  protected readonly max = 100;
  protected status: RequestStatus = 'InProgress';
}
