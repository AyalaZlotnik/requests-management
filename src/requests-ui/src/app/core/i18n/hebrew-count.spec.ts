import { REQUESTS, agree, hebrewCount } from './hebrew-count';

describe('hebrewCount', () => {
  it('uses the singular phrase for one', () => {
    expect(hebrewCount(1, REQUESTS)).toBe('פנייה אחת');
  });

  it.each([
    [0, '0 פניות'],
    [3, '3 פניות'],
    [1250, '1,250 פניות'],
  ])('puts the number before the plural for %i', (count, expected) => {
    expect(hebrewCount(count, REQUESTS)).toBe(expected);
  });

  it('agrees the verb with the count', () => {
    expect(`${hebrewCount(1, REQUESTS)} ${agree(1, 'נבחרה', 'נבחרו')}`).toBe('פנייה אחת נבחרה');
    expect(`${hebrewCount(2, REQUESTS)} ${agree(2, 'נבחרה', 'נבחרו')}`).toBe('2 פניות נבחרו');
  });
});
