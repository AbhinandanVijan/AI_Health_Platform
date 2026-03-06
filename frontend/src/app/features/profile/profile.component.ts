import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatButtonModule,
    MatSnackBarModule,
  ],
  template: `
    <mat-card class="profile-card">
      <mat-card-header>
        <mat-card-title>My Profile</mat-card-title>
        <mat-card-subtitle>
          Demographic and clinical context helps the AI generate more personalised, 
          clinically relevant health insights. All fields are optional.
        </mat-card-subtitle>
      </mat-card-header>

      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="save()">
          <div class="form-row">
            <mat-form-field appearance="outline" class="field-half">
              <mat-label>Date of Birth</mat-label>
              <input matInput type="date" formControlName="dateOfBirth" />
            </mat-form-field>

            <mat-form-field appearance="outline" class="field-half">
              <mat-label>Biological Sex</mat-label>
              <mat-select formControlName="biologicalSex">
                <mat-option value="">Prefer not to say</mat-option>
                <mat-option value="Male">Male</mat-option>
                <mat-option value="Female">Female</mat-option>
                <mat-option value="Other">Other</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <div class="form-row">
            <mat-form-field appearance="outline" class="field-half">
              <mat-label>BMI</mat-label>
              <input matInput type="number" step="0.1" min="10" max="80" formControlName="bmi" placeholder="e.g. 24.5" />
            </mat-form-field>

            <mat-form-field appearance="outline" class="field-half">
              <mat-label>Physical Activity Level</mat-label>
              <mat-select formControlName="activityLevel">
                <mat-option value="">Not specified</mat-option>
                <mat-option value="Sedentary">Sedentary</mat-option>
                <mat-option value="Moderate">Moderate</mat-option>
                <mat-option value="Active">Active</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <div class="toggles-row">
            <div class="toggle-item">
              <mat-slide-toggle formControlName="isSmoker" color="primary">Smoker</mat-slide-toggle>
            </div>
            <div class="toggle-item">
              <mat-slide-toggle formControlName="isDiabetic" color="primary">Diabetic</mat-slide-toggle>
            </div>
            <div class="toggle-item">
              <mat-slide-toggle formControlName="isHypertensive" color="primary">Hypertensive</mat-slide-toggle>
            </div>
          </div>

          <p class="profile-note">
            <strong>Note:</strong> This information is used solely to personalise AI-generated health insights. 
            It is not shared with third parties and does not replace a clinical assessment.
          </p>

          <div class="actions">
            <button mat-flat-button color="primary" type="submit" [disabled]="saving()">
              {{ saving() ? 'Saving...' : 'Save Profile' }}
            </button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .profile-card { max-width: 680px; margin: 0 auto; }
    mat-card-subtitle { margin-top: 4px; font-size: 13px; }
    .form-row { display: flex; gap: 16px; margin-top: 20px; }
    .field-half { flex: 1; }
    .toggles-row { display: flex; gap: 32px; margin-top: 8px; flex-wrap: wrap; }
    .toggle-item { display: flex; align-items: center; padding: 8px 0; }
    .profile-note { font-size: 12px; color: rgba(0,0,0,0.55); margin-top: 20px; }
    .actions { margin-top: 24px; }
  `],
})
export class ProfileComponent implements OnInit {
  form: FormGroup;
  saving = signal(false);

  constructor(
    private fb: FormBuilder,
    private api: ApiService,
    private snackBar: MatSnackBar,
  ) {
    this.form = this.fb.group({
      dateOfBirth: [null],
      biologicalSex: [null],
      bmi: [null],
      activityLevel: [null],
      isSmoker: [false],
      isDiabetic: [false],
      isHypertensive: [false],
    });
  }

  ngOnInit(): void {
    this.api.getProfile().subscribe({
      next: (profile) => {
        this.form.patchValue({
          dateOfBirth: profile.dateOfBirth ? profile.dateOfBirth.substring(0, 10) : null,
          biologicalSex: profile.biologicalSex ?? '',
          bmi: profile.bmi ?? null,
          activityLevel: profile.activityLevel ?? '',
          isSmoker: profile.isSmoker ?? false,
          isDiabetic: profile.isDiabetic ?? false,
          isHypertensive: profile.isHypertensive ?? false,
        });
      },
    });
  }

  save(): void {
    if (this.saving()) return;
    this.saving.set(true);

    const raw = this.form.value;
    const dto = {
      dateOfBirth: raw.dateOfBirth ? new Date(raw.dateOfBirth).toISOString() : null,
      biologicalSex: raw.biologicalSex || null,
      bmi: raw.bmi ? Number(raw.bmi) : null,
      activityLevel: raw.activityLevel || null,
      isSmoker: raw.isSmoker ?? null,
      isDiabetic: raw.isDiabetic ?? null,
      isHypertensive: raw.isHypertensive ?? null,
    };

    this.api.updateProfile(dto).subscribe({
      next: () => {
        this.saving.set(false);
        this.snackBar.open('Profile saved. Your next insight generation will reflect these details.', 'OK', { duration: 4000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to save profile. Please try again.', 'Dismiss', { duration: 4000 });
      },
    });
  }
}
