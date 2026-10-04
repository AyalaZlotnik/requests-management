import { Component, inject } from '@angular/core';
import { CurrentUser } from './core/current-user.service';
import { RequestsPageComponent } from './features/requests/requests-page.component';

@Component({
  selector: 'app-root',
  imports: [RequestsPageComponent],
  template: `
    <header class="top">
      <h1>Requests Management</h1>
      <label class="user">
        Acting as
        <input [value]="user.name()" (change)="setUser($any($event.target).value)" maxlength="100" aria-label="Current user name" />
      </label>
    </header>
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
