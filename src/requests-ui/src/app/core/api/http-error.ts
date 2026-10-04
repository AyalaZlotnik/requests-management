import { HttpErrorResponse } from '@angular/common/http';
import { STATUS_LABELS } from '../i18n/labels';
import { RequestDetails, RequestStatus, StatusHistoryEntry } from '../models/request.models';

/** Body of a 409 from PATCH /status: the request as stored now and its latest status change. */
export interface ConflictDetails {
  currentState: RequestDetails | null;
  lastChange: StatusHistoryEntry | null;
}

/** What the screen knows about the failed action – used to make the message specific. */
export interface ErrorContext {
  requestId?: number;
  /** The status the user tried to set. */
  attemptedStatus?: RequestStatus;
}

interface ProblemBody {
  title?: string;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  currentStatus?: RequestStatus;
  allowedStatuses?: RequestStatus[];
}

/** API field names (as they appear in validation errors) → what the user sees on the screen. */
const FIELD_NAMES: Record<string, string> = {
  search: 'חיפוש',
  status: 'סטטוס',
  priority: 'עדיפות',
  organizationname: 'שם הארגון',
  assignedto: 'מטפל/ת',
  createdfrom: 'תאריך התחלה',
  createdto: 'תאריך סיום',
  page: 'מספר עמוד',
  pagesize: 'מספר שורות בעמוד',
  sortby: 'מיון',
  sortdirection: 'כיוון המיון',
  changedby: 'שם המשתמש',
  items: 'הפניות שנבחרו',
};

/**
 * User-facing (Hebrew) message for a failed call, built from what the server returned:
 * which field was invalid (400), which transitions are allowed (422), which request is missing (404),
 * and a reference code for support (500).
 */
export function errorMessage(error: unknown, context: ErrorContext = {}): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'אירעה שגיאה לא צפויה בדפדפן. יש לרענן את הדף.';
  }

  const problem = (typeof error.error === 'object' ? error.error : null) as ProblemBody | null;
  const request = context.requestId ? `פנייה #${context.requestId}` : 'הפנייה';

  if (isConnectionError(error)) {
    return 'אין חיבור לשרת. יש לבדוק את החיבור ולנסות שוב.';
  }

  switch (error.status) {
    case 400:
      return invalidFieldsMessage(problem);
    case 404:
      return `${request} לא נמצאה – ייתכן שנמחקה או שהקישור שגוי.`;
    case 409:
      return `${request} עודכנה בינתיים על ידי משתמש אחר.`;
    case 422:
      return transitionMessage(problem, context);
    case 428:
      return 'חסרה גרסת הפנייה בבקשת העדכון. יש לטעון את הפנייה מחדש ולנסות שוב.';
    default:
      if (error.status >= 500) {
        const reference = problem?.traceId ? ` אם זה חוזר, מסרו לתמיכה את הקוד: ${problem.traceId}` : '';
        return `אירעה תקלה בשרת והפעולה לא בוצעה.${reference}`;
      }
      return `הפעולה נכשלה (קוד ${error.status}).`;
  }
}

export function conflictDetails(error: unknown): ConflictDetails | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const body = error.error as Partial<ConflictDetails> | null;
  return { currentState: body?.currentState ?? null, lastChange: body?.lastChange ?? null };
}

/**
 * The API could not be reached: no response at all (0), a gateway error (502/503/504), or any 5xx without
 * a Problem Details body. The API always answers errors with Problem Details, so a bare 5xx came from a
 * proxy in front of it (the dev server proxy answers 500 with an empty body when the API is down).
 */
export function isConnectionError(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return false;
  if ([0, 502, 503, 504].includes(error.status)) return true;
  const body = error.error as { title?: unknown } | null;
  return error.status >= 500 && !(body && typeof body === 'object' && 'title' in body);
}

function invalidFieldsMessage(problem: ProblemBody | null): string {
  const fields = Object.keys(problem?.errors ?? {})
    .map(fieldName)
    .filter((name, index, all) => all.indexOf(name) === index);
  if (fields.length === 0) {
    return 'הנתונים שנשלחו אינם תקינים.';
  }
  return fields.length === 1 ? `הערך בשדה "${fields[0]}" אינו תקין.` : `הערכים בשדות ${fields.map((f) => `"${f}"`).join(', ')} אינם תקינים.`;
}

function fieldName(key: string): string {
  // Keys look like "PageSize", "$.status" or "Items[3].RowVersion".
  const normalized = key.replace(/^\$\./, '').split(/[.[]/)[0].toLowerCase();
  return FIELD_NAMES[normalized] ?? key;
}

function transitionMessage(problem: ProblemBody | null, context: ErrorContext): string {
  const from = problem?.currentStatus ? STATUS_LABELS[problem.currentStatus] : null;
  const to = context.attemptedStatus ? STATUS_LABELS[context.attemptedStatus] : null;
  const allowed = problem?.allowedStatuses?.map((s) => STATUS_LABELS[s]) ?? [];

  const what = from && to ? `לא ניתן להעביר פנייה מ"${from}" ל"${to}".` : 'מעבר הסטטוס הזה אינו מותר.';
  const options = allowed.length ? ` מעברים מותרים: ${allowed.join(', ')}.` : ' לא ניתן לשנות את הסטטוס מהמצב הנוכחי.';
  return what + options;
}
