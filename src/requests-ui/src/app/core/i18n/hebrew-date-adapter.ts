import { Injectable } from '@angular/core';
import { NativeDateAdapter } from '@angular/material/core';

const DAY_MONTH_YEAR = /^\s*(\d{1,2})[./-](\d{1,2})[./-](\d{4})\s*$/;

/**
 * The native adapter parses typed dates with Date.parse, i.e. US order (MM/dd) – "04/10/2026" would become
 * April 10th. Israeli users type day first, so dd/MM/yyyy (and dd.MM.yyyy) is parsed here explicitly.
 */
@Injectable()
export class HebrewDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value === 'string') {
      const match = DAY_MONTH_YEAR.exec(value);
      if (match) {
        const [, day, month, year] = match.map(Number);
        const date = new Date(year, month - 1, day);
        // Reject impossible dates such as 31/02/2026 instead of rolling them over.
        return date.getMonth() === month - 1 && date.getDate() === day ? date : this.invalid();
      }
    }
    return super.parse(value, parseFormat);
  }
}
