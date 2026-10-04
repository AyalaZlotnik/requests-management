import { Pipe, PipeTransform } from '@angular/core';
import { RequestStatus } from '../../core/models/request.models';

const LABELS: Record<RequestStatus, string> = {
  New: 'New',
  InProgress: 'In progress',
  Waiting: 'Waiting',
  Completed: 'Completed',
};

@Pipe({ name: 'statusLabel' })
export class StatusLabelPipe implements PipeTransform {
  transform(status: RequestStatus): string {
    return LABELS[status] ?? status;
  }
}
