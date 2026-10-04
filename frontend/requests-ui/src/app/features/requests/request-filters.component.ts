import { Component, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged, filter, map } from 'rxjs';
import {
  REQUEST_PRIORITIES,
  REQUEST_STATUSES,
  RequestFilters,
  RequestPriority,
  RequestStatus,
} from '../../core/models/request.models';
import { EMPTY_FILTERS } from './requests-list.store';
import { StatusLabelPipe } from './status-label.pipe';

/** Filter form. Emits the complete filter set, debounced, only when it actually changed and is valid. */
@Component({
  selector: 'app-request-filters',
  imports: [ReactiveFormsModule, StatusLabelPipe],
  template: `
    <form class="filters" [formGroup]="form" (submit)="$event.preventDefault()">
      <label class="field grow">
        <span>Search (title / organization)</span>
        <input type="search" formControlName="search" placeholder="e.g. permit, Negev…" maxlength="100" />
      </label>
      <label class="field">
        <span>Organization starts with</span>
        <input formControlName="organizationName" maxlength="200" />
      </label>
      <label class="field">
        <span>Assigned to</span>
        <input formControlName="assignedTo" placeholder="agent07" maxlength="100" />
      </label>
      <label class="field">
        <span>Created from</span>
        <input type="date" formControlName="createdFrom" />
      </label>
      <label class="field">
        <span>Created to</span>
        <input type="date" formControlName="createdTo" />
      </label>

      <fieldset class="chips">
        <legend>Status</legend>
        @for (status of statuses; track status) {
          <button type="button" class="chip" [class.on]="form.controls.status.value.includes(status)" (click)="toggle(form.controls.status, status)">
            {{ status | statusLabel }}
          </button>
        }
      </fieldset>
      <fieldset class="chips">
        <legend>Priority</legend>
        @for (priority of priorities; track priority) {
          <button type="button" class="chip" [class.on]="form.controls.priority.value.includes(priority)" (click)="toggle(form.controls.priority, priority)">
            {{ priority }}
          </button>
        }
      </fieldset>

      <button type="button" class="link" (click)="form.setValue(empty)">Clear filters</button>
      @if (rangeInvalid()) {
        <p class="error-text">“Created from” must be before “Created to”.</p>
      }
    </form>
  `,
})
export class RequestFiltersComponent {
  readonly filtersChange = output<RequestFilters>();

  protected readonly statuses = REQUEST_STATUSES;
  protected readonly priorities = REQUEST_PRIORITIES;
  protected readonly empty = EMPTY_FILTERS;

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
    this.form.valueChanges
      .pipe(
        // Wait until the user stops typing before hitting the server.
        debounceTime(350),
        map(() => this.form.getRawValue()),
        distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
        filter(() => !this.rangeInvalid()),
        takeUntilDestroyed(),
      )
      .subscribe((filters) => this.filtersChange.emit(filters));
  }

  protected rangeInvalid(): boolean {
    const { createdFrom, createdTo } = this.form.getRawValue();
    return !!createdFrom && !!createdTo && createdFrom > createdTo;
  }

  protected toggle<T>(control: FormControl<T[]>, value: T): void {
    const current = control.value;
    control.setValue(current.includes(value) ? current.filter((v) => v !== value) : [...current, value]);
  }
}
