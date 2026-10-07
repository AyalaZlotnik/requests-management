import { DatePipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { RequestListItem, SortDirection, SortField } from '../../core/models/request.models';
import { PriorityLabelPipe, StatusLabelPipe } from './status-label.pipe';

const SORTABLE: readonly string[] = ['title', 'organizationName', 'status', 'priority', 'createdAt', 'updatedAt'];

/**
 * Presentational table: renders one page and reports user intents.
 * Sorting is done by the server – MatSort only reports which column and direction were chosen.
 */
@Component({
  selector: 'app-requests-table',
  imports: [MatTableModule, MatSortModule, MatCheckboxModule, DatePipe, StatusLabelPipe, PriorityLabelPipe],
  templateUrl: './requests-table.component.html'
 ,
})
export class RequestsTableComponent {
  readonly items = input.required<RequestListItem[]>();
  readonly sortBy = input.required<SortField>();
  readonly sortDirection = input.required<SortDirection>();
  readonly selectedIds = input.required<ReadonlySet<number>>();
  readonly activeId = input<number | null>(null);

  readonly sort = output<{ field: SortField; direction: SortDirection }>();
  readonly open = output<number>();
  readonly toggleSelect = output<RequestListItem>();
  readonly toggleAll = output<void>();

  protected readonly columns = ['select', 'id', 'title', 'organizationName', 'status', 'priority', 'assignedTo', 'createdAt', 'updatedAt'];

  protected readonly allSelected = computed(
    () => this.items().length > 0 && this.items().every((i) => this.selectedIds().has(i.id)),
  );
  protected readonly someSelected = computed(
    () => !this.allSelected() && this.items().some((i) => this.selectedIds().has(i.id)),
  );

  protected onSort(event: Sort): void {
    if (SORTABLE.includes(event.active) && event.direction) {
      this.sort.emit({ field: event.active as SortField, direction: event.direction });
    }
  }
}
