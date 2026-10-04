import { Pipe, PipeTransform } from '@angular/core';
import { PRIORITY_LABELS, STATUS_LABELS } from '../../core/i18n/labels';
import { RequestPriority, RequestStatus } from '../../core/models/request.models';

@Pipe({ name: 'statusLabel' })
export class StatusLabelPipe implements PipeTransform {
  transform(status: RequestStatus): string {
    return STATUS_LABELS[status] ?? status;
  }
}

@Pipe({ name: 'priorityLabel' })
export class PriorityLabelPipe implements PipeTransform {
  transform(priority: RequestPriority): string {
    return PRIORITY_LABELS[priority] ?? priority;
  }
}
