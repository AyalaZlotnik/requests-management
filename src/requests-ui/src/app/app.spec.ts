import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { App } from './app';
import { CurrentUser } from './core/current-user.service';

describe('App header', () => {
  let dialogResult: string | undefined;

  function render(name: string) {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(dialogResult) }) } },
      ],
    });
    TestBed.inject(CurrentUser).name.set(name);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the user as initials and name', () => {
    const el = render('דנה לוי').nativeElement as HTMLElement;

    expect(el.querySelector('.avatar')?.textContent?.trim()).toBe('דל');
    expect(el.querySelector('.user-name')?.textContent?.trim()).toBe('דנה לוי');
  });

  it('changing the name in the dialog updates the user; cancelling keeps it', () => {
    const fixture = render('דנה לוי');
    const app = fixture.componentInstance as unknown as { changeName(): void };
    const user = TestBed.inject(CurrentUser);

    dialogResult = undefined;
    app.changeName();
    expect(user.name()).toBe('דנה לוי');

    dialogResult = 'יוסי כהן';
    app.changeName();
    fixture.detectChanges();
    expect(user.name()).toBe('יוסי כהן');
    expect((fixture.nativeElement as HTMLElement).querySelector('.avatar')?.textContent?.trim()).toBe('יכ');
  });
});
