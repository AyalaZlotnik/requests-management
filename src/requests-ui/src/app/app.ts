import { Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterOutlet } from '@angular/router';
import { ConnectionStatus } from './core/api/connection-status.service';
import { CurrentUser } from './core/current-user.service';
import { IconComponent } from './core/ui/icon.component';

const MAX_NAME_LENGTH = 100;

@Component({
  selector: 'app-root',
  imports: [ReactiveFormsModule, MatToolbarModule, MatFormFieldModule, MatInputModule, MatButtonModule, RouterOutlet, IconComponent],
  template: `
    <mat-toolbar class="top">
      <h1>ניהול פניות</h1>
      <span class="spacer"></span>
      <mat-form-field class="user" subscriptSizing="dynamic" appearance="outline">
        <mat-label>שם המשתמש (נשמר בהיסטוריה)</mat-label>
        <input matInput [formControl]="userName" [maxlength]="maxNameLength" />
        @if (userName.hasError('required')) {
          <mat-error>יש להזין שם – הוא נרשם בהיסטוריית השינויים.</mat-error>
        }
      </mat-form-field>
    </mat-toolbar>

    @if (connection.offline()) {
      <div class="connection-banner" role="alert">
        <app-icon name="warning" />
        <span>אין חיבור לשרת. הנתונים שמוצגים עשויים להיות לא עדכניים, ושינויים לא יישמרו.</span>
        <button mat-flat-button type="button" (click)="connection.retry()">ניסיון חוזר</button>
      </div>
    }

    <main>
      <router-outlet />
    </main>
  `,
})
export class App {
  private readonly user = inject(CurrentUser);
  protected readonly connection = inject(ConnectionStatus);

  protected readonly maxNameLength = MAX_NAME_LENGTH;
  protected readonly userName = new FormControl(this.user.name(), {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)],
  });

  constructor() {
    this.userName.markAsTouched();
    this.userName.statusChanges.pipe(takeUntilDestroyed()).subscribe(() => this.user.nameValid.set(this.userName.valid));
    // Only a valid name is used for updates; an empty field shows its error instead of being ignored silently.
    this.userName.valueChanges.pipe(takeUntilDestroyed()).subscribe((name) => {
      if (this.userName.valid && name.trim()) this.user.name.set(name.trim());
    });
  }
}
