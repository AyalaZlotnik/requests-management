import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterOutlet } from '@angular/router';
import { ConnectionStatus } from './core/api/connection-status.service';
import { CurrentUser } from './core/current-user.service';
import { IconComponent } from './core/ui/icon.component';
import { UserNameDialogComponent } from './core/ui/user-name-dialog.component';

@Component({
  selector: 'app-root',
  imports: [MatToolbarModule, MatButtonModule, MatMenuModule, RouterOutlet, IconComponent],
  template: `
    <mat-toolbar class="app-bar">
      <app-icon name="inbox" [size]="28" class="app-logo" />
      <div class="app-title">
        <h1>ניהול פניות</h1>
        <span class="app-subtitle">פניות מארגונים ומעסיקים</span>
      </div>
      <span class="spacer"></span>

      <button type="button" class="user-button" [matMenuTriggerFor]="userMenu" [attr.aria-label]="'משתמש: ' + user.name()">
        <span class="avatar" aria-hidden="true">{{ initials() }}</span>
        <span class="user-name">{{ user.name() }}</span>
        <app-icon name="dropDown" [size]="20" />
      </button>
      <mat-menu #userMenu="matMenu" xPosition="before">
        <div class="menu-header">
          <span class="muted small">שם שנרשם בהיסטוריה</span>
          <strong>{{ user.name() }}</strong>
        </div>
        <button mat-menu-item type="button" (click)="changeName()">
          <app-icon name="edit" [size]="18" />
          <span>שינוי שם…</span>
        </button>
      </mat-menu>
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
  protected readonly user = inject(CurrentUser);
  protected readonly connection = inject(ConnectionStatus);
  private readonly dialog = inject(MatDialog);

  /** First letter of the first two words: "דנה לוי" → "דל". */
  protected readonly initials = computed(() =>
    this.user
      .name()
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((word) => word[0])
      .join(''),
  );

  protected changeName(): void {
    this.dialog
      .open<UserNameDialogComponent, string, string>(UserNameDialogComponent, { data: this.user.name(), width: '400px', direction: 'rtl' })
      .afterClosed()
      .subscribe((name) => {
        if (name) this.user.name.set(name);
      });
  }
}
