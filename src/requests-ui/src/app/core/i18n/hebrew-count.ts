/**
 * A number with a noun, in correct Hebrew: one → the singular form with "אחת/אחד" after it
 * ("פנייה אחת"), otherwise the number before the plural ("3 פניות", "1,250 פניות").
 */
export interface CountForms {
  /** Full singular phrase, e.g. "פנייה אחת". */
  one: string;
  /** Plural noun, e.g. "פניות". */
  many: string;
}

export const REQUESTS: CountForms = { one: 'פנייה אחת', many: 'פניות' };

export function hebrewCount(count: number, forms: CountForms): string {
  return count === 1 ? forms.one : `${count.toLocaleString('he-IL')} ${forms.many}`;
}

/** Picks the singular or plural form of a verb/adjective that agrees with the count. */
export function agree(count: number, singular: string, plural: string): string {
  return count === 1 ? singular : plural;
}
