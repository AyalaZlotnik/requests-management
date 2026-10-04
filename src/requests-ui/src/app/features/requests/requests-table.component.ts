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
  template: `
    <div class="table-wrap">
      <table
        mat-table
        [dataSource]="items()"
        matSort
        matSortDisableClear
        [matSortActive]="sortBy()"
        [matSortDirection]="sortDirection()"
        (matSortChange)="onSort($event)"
      >
        <ng-container matColumnDef="select">
          <th mat-header-cell *matHeaderCellDef>
            <mat-checkbox
              aria-label="בחירת כל הפניות בעמוד"
              [checked]="allSelected()"
              [indeterminate]="someSelected()"
              (change)="toggleAll.emit()"
            />
          </th>
          <td mat-cell *matCellDef="let row" (click)="$event.stopPropagation()">
            <mat-checkbox
              [attr.aria-label]="'בחירת פנייה ' + row.id"
              [checked]="selectedIds().has(row.id)"
              (change)="toggleSelect.emit(row)"
            />
          </td>
        </ng-container>

        <ng-container matColumnDef="id">
          <th mat-header-cell *matHeaderCellDef>#</th>
          <td mat-cell *matCellDef="let row" class="muted">{{ row.id }}</td>
        </ng-container>

        <ng-container matColumnDef="title">
          <th mat-header-cell *matHeaderCellDef mat-sort-header>כותרת</th>
          <td mat-cell *matCellDef="let row" class="clip" [title]="row.title">{{ row.title }}</td>
        </ng-container>

        <ng-container matColumnDef="organizationName">
          <th mat-header-cell *matHeaderCellDef mat-sort-header>ארגון</th>
          <td mat-cell *matCellDef="let row" class="clip" [title]="row.organizationName">{{ row.organizationName }}</td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>סטטוס</th>
          <td mat-cell *matCellDef="let row">
            <span class="badge" [attr.data-status]="row.status">{{ row.status | statusLabel }}</span>
          </td>
        </ng-container>

        <ng-container matColumnDef="priority">
          <th mat-header-cell *matHeaderCellDef>עדיפות</th>
          <td mat-cell *matCellDef="let row">
            <span class="priority" [attr.data-priority]="row.priority">{{ row.priority | priorityLabel }}</span>
          </td>
        </ng-container>

        <ng-container matColumnDef="assignedTo">
          <th mat-header-cell *matHeaderCellDef>מטפל/ת</th>
          <td mat-cell *matCellDef="let row" class="nowrap">{{ row.assignedTo ?? '—' }}</td>
        </ng-container>

        <ng-container matColumnDef="createdAt">
          <th mat-header-cell *matHeaderCellDef mat-sort-header>נוצרה</th>
          <td mat-cell *matCellDef="let row" class="nowrap">{{ row.createdAt | date: 'dd/MM/yy HH:mm' }}</td>
        </ng-container>

        <ng-container matColumnDef="updatedAt">
          <th mat-header-cell *matHeaderCellDef mat-sort-header>עודכנה</th>
          <td mat-cell *matCellDef="let row" class="nowrap">{{ row.updatedAt | date: 'dd/MM/yy HH:mm' }}</td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns; sticky: true"></tr>
        <tr
          mat-row
          *matRowDef="let row; columns: columns"
          [class.active]="row.id === activeId()"
          (click)="open.emit(row.id)"
          (keydown.enter)="open.emit(row.id)"
          tabindex="0"
        ></tr>
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
