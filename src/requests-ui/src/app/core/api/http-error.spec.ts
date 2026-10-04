import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage, isConnectionError } from './http-error';

const response = (status: number, error: unknown = null) => new HttpErrorResponse({ status, error });

describe('errorMessage', () => {
  it('400 names the invalid fields in Hebrew, once each', () => {
    const error = response(400, {
      title: 'One or more validation errors occurred.',
      errors: { PageSize: ['too big'], CreatedFrom: ['x'], CreatedTo: ['y'], '$.status': ['bad'], 'Items[3].RowVersion': ['bad'] },
    });

    expect(errorMessage(error)).toBe('הערכים בשדות "מספר שורות בעמוד", "תאריך התחלה", "תאריך סיום", "סטטוס", "הפניות שנבחרו" אינם תקינים.');
  });

  it('422 explains which transition was refused and which are allowed', () => {
    const error = response(422, { title: 'Invalid status transition', currentStatus: 'New', allowedStatuses: ['InProgress', 'Waiting'] });

    expect(errorMessage(error, { attemptedStatus: 'Completed' })).toBe(
      'לא ניתן להעביר פנייה מ"חדשה" ל"הושלמה". מעברים מותרים: בטיפול, ממתינה.',
    );
  });

  it('404 names the request', () => {
    expect(errorMessage(response(404, { title: 'Resource not found' }), { requestId: 77 })).toBe(
      'פנייה #77 לא נמצאה – ייתכן שנמחקה או שהקישור שגוי.',
    );
  });

  it('500 from the API gives the trace id as a reference for support', () => {
    const error = response(500, { title: 'An unexpected error occurred', traceId: '00-abc-01' });

    expect(errorMessage(error)).toBe('אירעה תקלה בשרת והפעולה לא בוצעה. אם זה חוזר, מסרו לתמיכה את הקוד: 00-abc-01');
  });

  it.each([0, 502, 503, 504])('status %i means the server could not be reached', (status) => {
    expect(isConnectionError(response(status))).toBe(true);
    expect(errorMessage(response(status))).toBe('אין חיבור לשרת. יש לבדוק את החיבור ולנסות שוב.');
  });

  it('a bare 500 (no Problem Details) came from a proxy, not from the API', () => {
    expect(isConnectionError(response(500, null))).toBe(true);
    expect(isConnectionError(response(500, { title: 'An unexpected error occurred' }))).toBe(false);
  });
});
