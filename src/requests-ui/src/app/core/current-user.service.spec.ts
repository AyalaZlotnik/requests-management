import { TestBed } from '@angular/core/testing';
import { CurrentUser, DEFAULT_USER_NAME } from './current-user.service';

describe('CurrentUser', () => {
  afterEach(() => localStorage.clear());

  it.each(['', '   '])('falls back to the default name when the stored name is blank (%j)', (stored) => {
    localStorage.setItem('requests-ui.user', stored);

    expect(TestBed.inject(CurrentUser).name()).toBe(DEFAULT_USER_NAME);
  });

  it('uses the remembered name, trimmed', () => {
    localStorage.setItem('requests-ui.user', '  דנה לוי ');

    expect(TestBed.inject(CurrentUser).name()).toBe('דנה לוי');
  });
});
