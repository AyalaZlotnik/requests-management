import { Component, effect, input, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { debounceTime, filter, map } from 'rxjs';
import {
  REQUEST_PRIORITIES,
  REQUEST_STATUSES,
  RequestFilters,
  RequestPriority,
  RequestStatus,
} from '../../core/models/request.models';
import { EMPTY_FILTERS, sameValue } from './query-params';
import { PriorityLabelPipe, StatusLabelPipe } from './status-label.pipe';

/**
 * Filter form. Emits the complete filter set – debounced, only when it changed and is valid.
 * The current value comes back from the URL (Back, shared links) and is applied without re-emitting.
 */
@Component({
  selector: 'app-request-filters',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatChipsModule,
    MatButtonModule,
    StatusLabelPipe,
    PriorityLabelPipe,
  ],
  template: `
    <form class="filters" [formGroup]="form" (submit)="$event.preventDefault()">
      <mat-form-field class="grow" subscriptSizing="dynamic">
        <mat-label>חיפוש בכותרת או בשם הארגון</mat-label>
        <input matInput type="search" formControlName="search" maxlength="100" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>שם הארגון מתחיל ב…</mat-label>
        <input matInput formControlName="organizationName" maxlength="200" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>מטפל/ת</mat-label>
        <input matInput formControlName="assignedTo" maxlength="100" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>נוצרה מתאריך</mat-label>
        <input matInput type="date" formControlName="createdFrom" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>עד תאריך</mat-label>
        <input matInput type="date" formControlName="createdTo" />
      </mat-form-field>

      <div class="chip-group">
        <span class="chip-label">סטטוס</span>
        <mat-chip-listbox multiple formControlName="status" aria-label="סינון לפי סטטוס">
          @for (status of statuses; track status) {
            <mat-chip-option [value]="status">{{ status | statusLabel }}</mat-chip-option>
          }
        </mat-chip-listbox>
      </div>
      <div class="chip-group">
        <span class="chip-label">עדיפות</span>
        <mat-chip-listbox multiple formControlName="priority" aria-label="סינון לפי עדיפות">
          @for (priority of priorities; track priority) {
            <mat-chip-option [value]="priority">{{ priority | priorityLabel }}</mat-chip-option>
          }
        </mat-chip-listbox>
      </div>

      <button mat-button type="button" (click)="form.setValue(empty)">
ניקוי סינון
      </button>
      @if (rangeInvalid()) {
        <p class="error-text" role="alert">תאריך ההתחלה חייב להיות לפני תאריך הסיום.</p>
      }
    </form>
  `,
})
export class RequestFiltersComponent {
  /** Current filters from the URL (initial load, Back/Forward, shared links). */
  readonly value = input.required<RequestFilters>();
  readonly filtersChange = output<RequestFilters>();

  protected readonly statuses = REQUEST_STATUSES;
  protected readonly priorities = REQUEST_PRIORITIES;
  protected readonly empty = EMPTY_FILTERS;

  /** Last filters known to the URL – emitted by us or received from outside. */
  private lastKnown: RequestFilters = EMPTY_FILTERS;

  protected readonly form = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    status: new FormControl<RequestStatus[]>([], { nonNullable: true }),
    priority: new FormControl<RequestPriority[]>([], { nonNullable: true }),
    organizationName: new FormControl('', { nonNullable: true }),
    assignedTo: new FormControl('', { nonNullable: true }),
    createdFrom: new FormControl('', { nonNullable: true }),
    createdTo: new FormControl('', { nonNullable: true }),
  });

  constructor() {
    // URL → form. Changes we emitted ourselves come back here too; those are skipped, otherwise
    // the form would be reset to an older value while the user is still typing.
    effect(() => {
      const value = this.value();
      if (!sameValue(value, this.lastKnown)) {
        this.lastKnown = value;
        this.form.setValue(value, { emitEvent: false });
      }
    });

    // Form → URL. Wait until the user stops typing before hitting the server.
    this.form.valueChanges
      .pipe(
        debounceTime(350),
        map(() => this.form.getRawValue()),
        filter((filters) => !sameValue(filters, this.lastKnown) && !this.rangeInvalid()),
        takeUntilDestroyed(),
      )
      .subscribe((filters) => {
        this.lastKnown = filters;
        this.filtersChange.emit(filters);
      });
  }

  protected rangeInvalid(): boolean {
    const { createdFrom, createdTo } = this.form.getRawValue();
    return !!createdFrom && !!createdTo && createdFrom > createdTo;
  }
}
