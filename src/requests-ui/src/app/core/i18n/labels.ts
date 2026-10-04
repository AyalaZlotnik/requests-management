import { BulkItemOutcome, RequestPriority, RequestStatus } from '../models/request.models';

/** Hebrew display names. The API always uses the English enum names. */
export const STATUS_LABELS: Record<RequestStatus, string> = {
  New: 'חדשה',
  InProgress: 'בטיפול',
  Waiting: 'ממתינה',
  Completed: 'הושלמה',
};

export const PRIORITY_LABELS: Record<RequestPriority, string> = {
  Low: 'נמוכה',
  Medium: 'בינונית',
  High: 'גבוהה',
};

/** Why an item of a bulk update was not applied. */
export const BULK_OUTCOME_LABELS: Record<BulkItemOutcome, string> = {
  Updated: 'עודכנה',
  NotFound: 'הפנייה לא נמצאה',
  Conflict: 'עודכנה בינתיים על ידי משתמש אחר',
  InvalidTransition: 'מעבר הסטטוס אינו מותר מהמצב הנוכחי',
};
