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
import { IconComponent } from '../../core/ui/icon.component';
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
    IconComponent,
    DatePipe,
    StatusLabelPipe,
    PriorityLabelPipe,
  ],
  templateUrl: `./request-details.component.html`,
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
