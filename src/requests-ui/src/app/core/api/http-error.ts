import { HttpErrorResponse } from '@angular/common/http';
import { RequestDetails, StatusHistoryEntry } from '../models/request.models';

/** Body of a 409 from PATCH /status: the request as stored now and its latest status change. */
export interface ConflictDetails {
  currentState: RequestDetails | null;
  lastChange: StatusHistoryEntry | null;
}

/** User-facing (Hebrew) message for a failed call. Server texts are for developers and stay in the logs. */
export function errorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'אירעה שגיאה לא צפויה.';
  }

  switch (error.status) {
    case 0:
      return 'השרת אינו זמין. יש לוודא שה-API פועל.';
    case 400:
      return 'הבקשה אינה תקינה.';
    case 404:
      return 'הפנייה לא נמצאה.';
    case 409:
      return 'הפנייה עודכנה בינתיים על ידי משתמש אחר.';
    case 422:
      return 'מעבר הסטטוס הזה אינו מותר.';
    case 428:
      return 'חסרה גרסת הפנייה בבקשת העדכון.';
    default:
      return `השרת החזיר שגיאה (${error.status}).`;
  }
}

export function conflictDetails(error: unknown): ConflictDetails | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const body = error.error as Partial<ConflictDetails> | null;
  return { currentState: body?.currentState ?? null, lastChange: body?.lastChange ?? null };
}
