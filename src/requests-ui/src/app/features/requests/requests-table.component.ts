import { DatePipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { RequestListItem, SortDirection, SortField } from '../../core/models/request.models';
import { StatusLabelPipe } from './status-label.pipe';

interface Column {
  field: SortField | null;
  label: string;
}

/** Presentational table: renders one page and emits user intents. Sorting is done by the server. */
@Component({
  selector: 'app-requests-table',
  imports: [DatePipe, StatusLabelPipe],
  template: `
    <div class="table-wrap" [class.busy]="loading()">
      <table>
        <thead>
          <tr>
            <th class="check">
              <input type="checkbox" aria-label="Select all on page" [checked]="allSelected()" (change)="toggleAll.emit()" />
            </th>
            <th>#</th>
            @for (column of columns; track column.label) {
              <th>
                @if (column.field; as field) {
                  <button type="button" class="sort" (click)="sort.emit(field)" [attr.aria-sort]="ariaSort(field)">
                    {{ column.label }}
                    @if (sortBy() === field) {
                      <span aria-hidden="true">{{ sortDirection() === 'asc' ? '▲' : '▼' }}</span>
                    }
                  </button>
                } @else {
                  {{ column.label }}
                }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @for (item of items(); track item.id) {
            <tr [class.active]="item.id === activeId()" (click)="open.emit(item.id)">
              <td class="check" (click)="$event.stopPropagation()">
                <input type="checkbox" [attr.aria-label]="'Select request ' + item.id" [checked]="selectedIds().has(item.id)" (change)="toggleSelect.emit(item)" />
              </td>
              <td class="muted">{{ item.id }}</td>
              <td>{{ item.title }}</td>
              <td>{{ item.organizationName }}</td>
              <td><span class="badge" [attr.data-status]="item.status">{{ item.status | statusLabel }}</span></td>
              <td><span class="priority" [attr.data-priority]="item.priority">{{ item.priority }}</span></td>
              <td>{{ item.assignedTo ?? '—' }}</td>
              <td class="nowrap">{{ item.createdAt | date: 'dd/MM/yyyy HH:mm' }}</td>
              <td class="nowrap">{{ item.updatedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class RequestsTableComponent {
  readonly items = input.required<RequestListItem[]>();
  readonly sortBy = input.required<SortField>();
  readonly sortDirection = input.required<SortDirection>();
  readonly selectedIds = input.required<ReadonlySet<number>>();
  readonly activeId = input<number | null>(null);
  readonly loading = input(false);

  readonly sort = output<SortField>();
  readonly open = output<number>();
  readonly toggleSelect = output<RequestListItem>();
  readonly toggleAll = output<void>();

  protected readonly columns: Column[] = [
    { field: 'title', label: 'Title' },
    { field: 'organizationName', label: 'Organization' },
    { field: 'status', label: 'Status' },
    { field: 'priority', label: 'Priority' },
    { field: null, label: 'Assigned to' },
    { field: 'createdAt', label: 'Created' },
    { field: 'updatedAt', label: 'Updated' },
  ];

  protected readonly allSelected = computed(
    () => this.items().length > 0 && this.items().every((i) => this.selectedIds().has(i.id)),
  );

  protected ariaSort(field: SortField): string | null {
    if (this.sortBy() !== field) return null;
    return this.sortDirection() === 'asc' ? 'ascending' : 'descending';
  }
}
