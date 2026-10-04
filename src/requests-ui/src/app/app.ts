import { Component, inject } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatToolbarModule } from '@angular/material/toolbar';
import { CurrentUser } from './core/current-user.service';
import { RequestsPageComponent } from './features/requests/requests-page.component';

@Component({
  selector: 'app-root',
  imports: [MatToolbarModule, MatFormFieldModule, MatInputModule, RequestsPageComponent],
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
      <app-requests-page />
    </main>
  `,
})
export class App {
  protected readonly user = inject(CurrentUser);

  protected setUser(name: string): void {
    if (name.trim()) this.user.name.set(name.trim());
  }
}
