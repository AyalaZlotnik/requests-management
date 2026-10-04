import { Injectable } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

/** Hebrew texts for the Material paginator. */
@Injectable()
export class HebrewPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'שורות בעמוד';
  override firstPageLabel = 'עמוד ראשון';
  override previousPageLabel = 'עמוד קודם';
  override nextPageLabel = 'עמוד הבא';
  override lastPageLabel = 'עמוד אחרון';

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0) {
      return '0 מתוך 0';
    }
    const start = page * pageSize + 1;
    const end = Math.min(start + pageSize - 1, length);
    return `${start.toLocaleString('he-IL')}–${end.toLocaleString('he-IL')} מתוך ${length.toLocaleString('he-IL')}`;
  };
}
