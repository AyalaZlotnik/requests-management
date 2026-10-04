import { Component, inject } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterOutlet } from '@angular/router';
import { CurrentUser } from './core/current-user.service';

@Component({
  selector: 'app-root',
  imports: [MatToolbarModule, MatFormFieldModule, MatInputModule, RouterOutlet],
  template: `
    <mat-toolbar class="top">
      <h1>ניהול פניות</h1>
      <span class="spacer"></span>
      <mat-form-field class="user" subscriptSizing="dynamic" appearance="outline">
        <mat-label>שם המשתמש</mat-label>
        <input matInput [value]="user.name()" (change)="setUser($any($event.target).value)" maxlength="100" />
      </mat-form-field>
    </mat-toolbar>
    <main>
      <router-outlet />
    </main>
  `,
})
export class App {
  protected readonly user = inject(CurrentUser);

  protected setUser(name: string): void {
    if (name.trim()) this.user.name.set(name.trim());
  }
}
