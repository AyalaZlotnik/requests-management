import { DecimalPipe } from '@angular/common';
import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-pagination',
  imports: [DecimalPipe],
  template: `
    <nav class="pagination" aria-label="Pagination">
      <span class="muted">{{ totalCount() | number }} requests</span>
      <span class="spacer"></span>
      <label>
        Rows
        <select [value]="pageSize()" (change)="pageSizeChange.emit(+$any($event.target).value)">
          @for (size of sizes; track size) {
            <option [value]="size" [selected]="size === pageSize()">{{ size }}</option>
          }
        </select>
      </label>
      <button type="button" (click)="pageChange.emit(1)" [disabled]="page() <= 1" aria-label="First page">«</button>
      <button type="button" (click)="pageChange.emit(page() - 1)" [disabled]="page() <= 1" aria-label="Previous page">‹</button>
      <span>Page {{ page() | number }} of {{ totalPages() | number }}</span>
      <button type="button" (click)="pageChange.emit(page() + 1)" [disabled]="page() >= totalPages()" aria-label="Next page">›</button>
      <button type="button" (click)="pageChange.emit(totalPages())" [disabled]="page() >= totalPages()" aria-label="Last page">»</button>
    </nav>
  `,
})
export class PaginationComponent {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly totalCount = input.required<number>();

  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  // Must not exceed the API maximum (100).
  protected readonly sizes = [10, 20, 50, 100];
}
