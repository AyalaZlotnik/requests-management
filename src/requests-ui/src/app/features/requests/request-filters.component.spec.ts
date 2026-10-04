import { TestBed } from '@angular/core/testing';
import { DateAdapter, provideNativeDateAdapter } from '@angular/material/core';
import { HebrewDateAdapter } from '../../core/i18n/hebrew-date-adapter';
import { RequestFilters } from '../../core/models/request.models';
import { EMPTY_FILTERS } from './query-params';
import { RequestFiltersComponent } from './request-filters.component';

describe('RequestFiltersComponent', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function render() {
    TestBed.configureTestingModule({
      imports: [RequestFiltersComponent],
      providers: [provideNativeDateAdapter(), { provide: DateAdapter, useClass: HebrewDateAdapter }],
    });
    const fixture = TestBed.createComponent(RequestFiltersComponent);
    fixture.componentRef.setInput('value', EMPTY_FILTERS);
    const emitted: RequestFilters[] = [];
    fixture.componentInstance.filtersChange.subscribe((f) => emitted.push(f));
    fixture.detectChanges();
    const search = fixture.nativeElement.querySelector('.search-field input') as HTMLInputElement;
    return { fixture, emitted, search };
  }

  function type(input: HTMLInputElement, text: string) {
    for (let i = 1; i <= text.length; i++) {
      input.value = text.slice(0, i);
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(100);
    }
  }

  it('waits until the user stops typing and then emits once', () => {
    const { emitted, search } = render();

    type(search, 'היתר');
    expect(emitted).toHaveLength(0);

    vi.advanceTimersByTime(350);
    expect(emitted).toHaveLength(1);
    expect(emitted[0].search).toBe('היתר');
  });

  it('applies a value coming from the URL without emitting it back', () => {
    const { fixture, emitted, search } = render();

    fixture.componentRef.setInput('value', { ...EMPTY_FILTERS, search: 'דחוף', status: ['New'] });
    fixture.detectChanges();
    vi.advanceTimersByTime(1000);

    expect(search.value).toBe('דחוף');
    expect(emitted).toHaveLength(0);
  });

  function openAdvanced(fixture: ReturnType<typeof render>['fixture']) {
    const button = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) =>
      b.textContent?.includes('סינון מתקדם'),
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
  }

  function typeDate(input: HTMLInputElement, text: string) {
    input.value = text;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
  }

  it('reads typed dates day-first and emits them as yyyy-MM-dd', () => {
    const { fixture, emitted } = render();
    openAdvanced(fixture);
    const [from, to] = fixture.nativeElement.querySelectorAll('.date-field input') as NodeListOf<HTMLInputElement>;

    typeDate(from, '01/03/2026');
    typeDate(to, '31.03.2026');
    vi.advanceTimersByTime(1000);

    expect(emitted.at(-1)?.createdFrom).toBe('2026-03-01');
    expect(emitted.at(-1)?.createdTo).toBe('2026-03-31');
  });

  it('does not emit an inverted date range and explains why next to the field', () => {
    const { fixture, emitted } = render();
    openAdvanced(fixture);
    const [from, to] = fixture.nativeElement.querySelectorAll('.date-field input') as NodeListOf<HTMLInputElement>;

    typeDate(from, '01/05/2026');
    typeDate(to, '01/04/2026');
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();

    expect(emitted).toHaveLength(0);
    expect(fixture.nativeElement.querySelector('mat-error')?.textContent).toContain('תאריך ההתחלה חייב להיות לפני תאריך הסיום');
  });

  it('lists applied filters as chips and removing one clears only that filter', () => {
    const { fixture, emitted } = render();
    fixture.componentRef.setInput('value', { ...EMPTY_FILTERS, search: 'דחוף', assignedTo: 'לוי' });
    fixture.detectChanges();

    const chips = [...fixture.nativeElement.querySelectorAll('.active-filters mat-chip')] as HTMLElement[];
    expect(chips.map((c) => c.textContent?.trim())).toEqual(['חיפוש: "דחוף"', 'מטפל/ת: לוי']);

    (chips[1].querySelector('button') as HTMLButtonElement).click();
    vi.advanceTimersByTime(1000);

    expect(emitted).toHaveLength(1);
    expect(emitted[0]).toEqual({ ...EMPTY_FILTERS, search: 'דחוף' });
  });
});
