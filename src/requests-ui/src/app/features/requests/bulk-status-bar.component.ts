import { Component, input, output } from '@angular/core';
import { REQUEST_STATUSES, RequestStatus } from '../../core/models/request.models';
import { StatusLabelPipe } from './status-label.pipe';

@Component({
  selector: 'app-bulk-status-bar',
  imports: [StatusLabelPipe],
  template: `
    <div class="bulk-bar" role="region" aria-label="Bulk actions">
      <strong>{{ count() }} selected</strong>
      @if (count() > max) {
        <span class="error-text">Up to {{ max }} requests per bulk update.</span>
      }
      <label>
        Set status to
        <select #status>
          @for (s of statuses; track s) {
            <option [value]="s">{{ s | statusLabel }}</option>
          }
        </select>
      </label>
      <button type="button" class="primary" [disabled]="busy() || count() > max" (click)="apply.emit($any(status.value))">
        {{ busy() ? 'Updating…' : 'Apply' }}
      </button>
      <button type="button" class="link" (click)="clear.emit()">Clear selection</button>
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
}
