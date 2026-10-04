import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { RequestDetails } from '../../core/models/request.models';
import { ConflictDialogComponent, ConflictDialogData } from './conflict-dialog.component';

const current = (allowed: RequestDetails['allowedNextStatuses']): RequestDetails => ({
  id: 7,
  title: 'חידוש היתר',
  organizationName: 'ארגון',
  status: 'Waiting',
  priority: 'High',
  assignedTo: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
  rowVersion: 'AAAAAAAAAAM=',
  allowedNextStatuses: allowed,
});

function render(data: ConflictDialogData): HTMLElement {
  TestBed.configureTestingModule({
    imports: [ConflictDialogComponent],
    providers: [{ provide: MAT_DIALOG_DATA, useValue: data }],
  });
  const fixture = TestBed.createComponent(ConflictDialogComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const buttons = (el: HTMLElement) => [...el.querySelectorAll('button')].map((b) => b.textContent?.trim());

describe('ConflictDialogComponent', () => {
  it('offers to apply the change again when the workflow still allows it', () => {
    const el = render({
      attempted: 'Completed',
      current: current(['InProgress', 'Completed']),
      lastChange: { id: 1, previousStatus: 'InProgress', newStatus: 'Waiting', changedAt: '2026-01-02T10:00:00Z', changedBy: 'מיכל פרץ' },
    });

    expect(el.textContent).toContain('מיכל פרץ');
    expect(buttons(el)).toEqual(['הצגת המצב העדכני', 'להעביר בכל זאת ל"הושלמה"']);
  });

  it('offers only to show the current state when the change is no longer allowed', () => {
    const el = render({ attempted: 'Waiting', current: current(['InProgress', 'Completed']), lastChange: null });

    expect(buttons(el)).toEqual(['הצגת המצב העדכני']);
    expect(el.textContent).toContain('לא ניתן להעביר את הפנייה');
  });
});
