import { Component, computed, effect, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { debounceTime, filter, map } from 'rxjs';
import { PRIORITY_LABELS, STATUS_LABELS } from '../../core/i18n/labels';
import {
  REQUEST_PRIORITIES,
  REQUEST_STATUSES,
  RequestFilters,
  RequestPriority,
  RequestStatus,
} from '../../core/models/request.models';
import { IconComponent } from '../../core/ui/icon.component';
import { EMPTY_FILTERS, sameValue } from './query-params';
import { PriorityLabelPipe, StatusLabelPipe } from './status-label.pipe';

/** One applied filter, shown as a removable chip. */
interface ActiveFilter {
  key: keyof RequestFilters | 'dates';
  label: string;
}

type FormValue = Omit<RequestFilters, 'createdFrom' | 'createdTo'> & { createdFrom: Date | null; createdTo: Date | null };

/**
 * Filters: a prominent search box and the status/priority chips are always visible; organization,
 * handler and the date range sit under "advanced". Applied filters are listed as removable chips.
 * Emits the complete filter set – debounced, only when it changed and is valid. The current value comes
 * back from the URL (Back, shared links) and is applied without re-emitting.
 */
@Component({
  selector: 'app-request-filters',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatChipsModule,
    MatButtonModule,
    MatDatepickerModule,
    IconComponent,
    StatusLabelPipe,
    PriorityLabelPipe,
  ],
  templateUrl: './request-filters.component.html',
})
export class RequestFiltersComponent {
  /** Filters currently applied (from the URL: initial load, Back/Forward, shared links). */
  readonly value = input.required<RequestFilters>();
  readonly filtersChange = output<RequestFilters>();

  protected readonly statuses = REQUEST_STATUSES;
  protected readonly priorities = REQUEST_PRIORITIES;
  protected readonly advancedOpen = signal(false);

  /** Last filters known to the URL – emitted by us or received from outside. */
  private lastKnown: RequestFilters = EMPTY_FILTERS;

  protected readonly form = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    status: new FormControl<RequestStatus[]>([], { nonNullable: true }),
    priority: new FormControl<RequestPriority[]>([], { nonNullable: true }),
    organizationName: new FormControl('', { nonNullable: true }),
    assignedTo: new FormControl('', { nonNullable: true }),
    createdFrom: new FormControl<Date | null>(null),
    createdTo: new FormControl<Date | null>(null),
  });

  protected readonly advancedCount = computed(() => {
    const v = this.value();
    return [v.organizationName, v.assignedTo, v.createdFrom || v.createdTo].filter(Boolean).length;
  });

  protected readonly activeFilters = computed<ActiveFilter[]>(() => {
    const v = this.value();
    const items: ActiveFilter[] = [];
    if (v.search) items.push({ key: 'search', label: `חיפוש: "${v.search}"` });
    if (v.status.length) items.push({ key: 'status', label: `סטטוס: ${v.status.map((s) => STATUS_LABELS[s]).join(', ')}` });
    if (v.priority.length) items.push({ key: 'priority', label: `עדיפות: ${v.priority.map((p) => PRIORITY_LABELS[p]).join(', ')}` });
    if (v.organizationName) items.push({ key: 'organizationName', label: `ארגון מתחיל ב: ${v.organizationName}` });
    if (v.assignedTo) items.push({ key: 'assignedTo', label: `מטפל/ת: ${v.assignedTo}` });
    if (v.createdFrom || v.createdTo) {
      items.push({ key: 'dates', label: `נוצרה: ${displayDate(v.createdFrom) || '…'} – ${displayDate(v.createdTo) || '…'}` });
    }
    return items;
  });

  constructor() {
    // URL → form. Changes we emitted ourselves come back here too; those are skipped, otherwise
    // the form would be reset to an older value while the user is still typing.
    effect(() => {
      const value = this.value();
      if (!sameValue(value, this.lastKnown)) {
        this.lastKnown = value;
        this.form.setValue(toFormValue(value), { emitEvent: false });
      }
    });

    // Form → URL. Wait until the user stops typing before hitting the server.
    this.form.valueChanges
      .pipe(
        debounceTime(350),
        map(() => toFilters(this.form.getRawValue())),
        filter((filters) => !sameValue(filters, this.lastKnown) && this.form.valid && !this.rangeInvalid()),
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

  protected remove(key: ActiveFilter['key']): void {
    if (key === 'dates') {
      this.form.patchValue({ createdFrom: null, createdTo: null });
    } else {
      this.form.patchValue({ [key]: EMPTY_FILTERS[key] });
    }
  }

  protected clearAll(): void {
    this.form.setValue(toFormValue(EMPTY_FILTERS));
  }
}

function toFormValue(filters: RequestFilters): FormValue {
  return { ...filters, createdFrom: fromIsoDate(filters.createdFrom), createdTo: fromIsoDate(filters.createdTo) };
}

function toFilters(value: FormValue): RequestFilters {
  return { ...value, createdFrom: toIsoDate(value.createdFrom), createdTo: toIsoDate(value.createdTo) };
}

/** Local calendar day ↔ yyyy-MM-dd (the format kept in the URL). */
function toIsoDate(date: Date | null): string {
  if (!date) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function fromIsoDate(value: string): Date | null {
  if (!value) return null;
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

function displayDate(value: string): string {
  return value ? value.split('-').reverse().join('/') : '';
}
