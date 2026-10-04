import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, catchError, combineLatest, filter, forkJoin, map, of, startWith, switchMap, tap } from 'rxjs';
import { RequestsApi, VersionedRequest, etagOf } from '../../core/api/requests-api.service';
import { conflictDetails, errorMessage } from '../../core/api/http-error';
import { CurrentUser } from '../../core/current-user.service';
import { STATUS_LABELS } from '../../core/i18n/labels';
import { RequestDetails, RequestStatus, StatusHistoryEntry } from '../../core/models/request.models';
import { ConflictChoice, ConflictDialogComponent, ConflictDialogData } from './conflict-dialog.component';
import { PriorityLabelPipe, StatusLabelPipe } from './status-label.pipe';

type Loaded = { details: VersionedRequest; history: StatusHistoryEntry[] };

/**
 * Details side panel: shows a request, changes its status (conditional on the version that was read)
 * and lists the status history. Concurrency is enforced by the server; on 409 this panel explains what
 * happened and lets the user decide.
 */
@Component({
  selector: 'app-request-details',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatProgressBarModule,
    DatePipe,
    StatusLabelPipe,
    PriorityLabelPipe,
  ],
  template: `
    <aside class="details" aria-label="פרטי פנייה">
      <header>
        <h2>פנייה #{{ requestId() }}</h2>
        <button mat-button type="button" (click)="closed.emit()" aria-label="סגירת פרטי הפנייה">סגירה</button>
      </header>

      @if (loadError()) {
        <p class="error-text" role="alert">{{ loadError() }}</p>
      } @else if (data(); as d) {
        <dl>
          <dt>כותרת</dt><dd>{{ d.details.request.title }}</dd>
          <dt>ארגון</dt><dd>{{ d.details.request.organizationName }}</dd>
          <dt>סטטוס</dt>
          <dd><span class="badge" [attr.data-status]="d.details.request.status">{{ d.details.request.status | statusLabel }}</span></dd>
          <dt>עדיפות</dt><dd>{{ d.details.request.priority | priorityLabel }}</dd>
          <dt>מטפל/ת</dt><dd>{{ d.details.request.assignedTo ?? '—' }}</dd>
          <dt>נוצרה</dt><dd>{{ d.details.request.createdAt | date: 'dd/MM/yyyy HH:mm' }}</dd>
          <dt>עודכנה</dt><dd>{{ d.details.request.updatedAt | date: 'dd/MM/yyyy HH:mm' }}</dd>
        </dl>

        <section class="status-change">
          <h3>שינוי סטטוס</h3>
          @if (d.details.request.allowedNextStatuses.length) {
            <div class="row">
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>סטטוס חדש</mat-label>
                <mat-select [(ngModel)]="nextStatus" [disabled]="saving()">
                  @for (s of d.details.request.allowedNextStatuses; track s) {
                    <mat-option [value]="s">{{ s | statusLabel }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              <button mat-flat-button type="button" [disabled]="!nextStatus || saving()" (click)="save(d.details)">
                {{ saving() ? 'שומר…' : 'עדכון' }}
              </button>
            </div>
            @if (saving()) {
              <mat-progress-bar mode="indeterminate" />
            }
          } @else {
            <p class="muted">לא ניתן לשנות סטטוס ממצב "{{ d.details.request.status | statusLabel }}".</p>
          }
          @if (saveError()) {
            <p class="error-text" role="alert">{{ saveError() }}</p>
          }
        </section>

        <section>
          <h3>היסטוריית שינויים</h3>
          <ol class="history">
            @for (h of d.history; track h.id) {
              <li>
                <span>{{ h.previousStatus | statusLabel }} ← <strong>{{ h.newStatus | statusLabel }}</strong></span>
                <span class="muted small">{{ h.changedBy }} · {{ h.changedAt | date: 'dd/MM/yyyy HH:mm:ss' }}</span>
              </li>
            } @empty {
              <li class="muted">עדיין לא בוצעו שינויי סטטוס.</li>
            }
          </ol>
        </section>
      } @else {
        <mat-progress-bar mode="indeterminate" aria-label="טוען פנייה" />
      }
    </aside>
  `,
})
export class RequestDetailsComponent {
  private readonly api = inject(RequestsApi);
  private readonly user = inject(CurrentUser);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly refresh$ = new Subject<void>();

  readonly requestId = input.required<number>();
  readonly changed = output<RequestDetails>();
  readonly closed = output<void>();

  protected readonly data = signal<Loaded | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected nextStatus: RequestStatus | null = null;

  constructor() {
    const id$ = toObservable(this.requestId).pipe(
      tap(() => {
        this.data.set(null);
        this.saveError.set(null);
      }),
    );

    // Switching to another request cancels the pending load of the previous one.
    combineLatest([id$, this.refresh$.pipe(startWith(undefined))])
      .pipe(
        switchMap(([id]) =>
          forkJoin({ details: this.api.getById(id), history: this.api.getHistory(id) }).pipe(
            map((loaded) => ({ loaded, error: null })),
            catchError((e) => of({ loaded: null, error: errorMessage(e, { requestId: id }) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ loaded, error }) => {
        this.loadError.set(error);
        if (loaded) {
          this.data.set(loaded);
          this.nextStatus = loaded.details.request.allowedNextStatuses[0] ?? null;
        }
      });
  }

  protected save(read: VersionedRequest): void {
    if (this.nextStatus) {
      this.update(read.request.id, this.nextStatus, read.etag);
    }
  }

  private update(id: number, status: RequestStatus, etag: string): void {
    this.saving.set(true);
    this.saveError.set(null);
    this.api.updateStatus(id, status, etag, this.user.name()).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.snackBar.open(`הסטטוס עודכן ל"${STATUS_LABELS[updated.request.status]}"`, undefined, { duration: 3000 });
        this.refresh$.next();
        this.changed.emit(updated.request);
      },
      error: (e) => {
        this.saving.set(false);
        const conflict = conflictDetails(e);
        if (conflict?.currentState) {
          this.resolveConflict(status, conflict.currentState, conflict.lastChange);
        } else {
          this.saveError.set(errorMessage(e, { requestId: id, attemptedStatus: status }));
        }
      },
    });
  }

  private resolveConflict(attempted: RequestStatus, current: RequestDetails, lastChange: StatusHistoryEntry | null): void {
    // Whatever the user decides, the panel and the list must show the state that won.
    this.refresh$.next();
    this.changed.emit(current);

    this.dialog
      .open<ConflictDialogComponent, ConflictDialogData, ConflictChoice>(ConflictDialogComponent, {
        data: { attempted, current, lastChange },
        width: '440px',
        direction: 'rtl',
      })
      .afterClosed()
      .pipe(filter((choice) => choice === 'retry'))
      .subscribe(() => this.update(current.id, attempted, etagOf(current.rowVersion)));
  }
}
