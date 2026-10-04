import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export const MAX_USER_NAME_LENGTH = 100;

/** Changes the name recorded in the history ("changedBy"). Returns the new name, or nothing if cancelled. */
@Component({
  selector: 'app-user-name-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>שינוי שם משתמש</h2>
    <form (submit)="$event.preventDefault(); save()">
      <mat-dialog-content>
        <p class="muted">השם נרשם בהיסטוריית השינויים של כל פנייה שתעדכנו.</p>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>שם מלא</mat-label>
          <input matInput [formControl]="name" [maxlength]="maxLength" cdkFocusInitial />
          @if (name.hasError('required')) {
            <mat-error>יש להזין שם.</mat-error>
          }
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>ביטול</button>
        <button mat-flat-button type="submit">שמירה</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .full-width { width: 100%; }
  `,
})
export class UserNameDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<UserNameDialogComponent, string>);

  protected readonly maxLength = MAX_USER_NAME_LENGTH;
  protected readonly name = new FormControl(inject<string>(MAT_DIALOG_DATA), {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(MAX_USER_NAME_LENGTH)],
  });

  protected save(): void {
    const value = this.name.value.trim();
    if (!value) {
      this.name.setValue('');
      this.name.markAsTouched();
      return;
    }
    this.dialogRef.close(value);
  }
}
