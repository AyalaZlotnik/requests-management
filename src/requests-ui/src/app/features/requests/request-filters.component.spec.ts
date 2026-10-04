import { TestBed } from '@angular/core/testing';
import { RequestFilters } from '../../core/models/request.models';
import { EMPTY_FILTERS } from './query-params';
import { RequestFiltersComponent } from './request-filters.component';

describe('RequestFiltersComponent', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function render() {
    TestBed.configureTestingModule({ imports: [RequestFiltersComponent] });
    const fixture = TestBed.createComponent(RequestFiltersComponent);
    fixture.componentRef.setInput('value', EMPTY_FILTERS);
    const emitted: RequestFilters[] = [];
    fixture.componentInstance.filtersChange.subscribe((f) => emitted.push(f));
    fixture.detectChanges();
    const search = fixture.nativeElement.querySelector('input[type=search]') as HTMLInputElement;
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

  it('does not emit an inverted date range', () => {
    const { fixture, emitted } = render();
    const [from, to] = fixture.nativeElement.querySelectorAll('input[type=date]') as NodeListOf<HTMLInputElement>;

    from.value = '2026-05-01';
    from.dispatchEvent(new Event('input'));
    to.value = '2026-04-01';
    to.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();

    expect(emitted).toHaveLength(0);
    expect(fixture.nativeElement.textContent).toContain('תאריך ההתחלה חייב להיות לפני תאריך הסיום');
  });
});
