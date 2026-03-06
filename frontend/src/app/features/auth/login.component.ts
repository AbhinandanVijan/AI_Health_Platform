import { Component, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NgIf } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSelectModule } from '@angular/material/select';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    NgIf,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatTabsModule,
    MatSelectModule,
  ],
  template: `
    <div class="auth-page">
      <mat-card class="auth-card">
        <h2 class="auth-title">AI Health Platform</h2>
        <p>Login or register to manage your lab reports and insights.</p>

        <mat-tab-group>
          <mat-tab label="Login">
            <form [formGroup]="loginForm" (ngSubmit)="login()" class="auth-form">
              <mat-form-field appearance="outline" class="auth-field" subscriptSizing="dynamic">
                <mat-label>Email</mat-label>
                <input matInput formControlName="email" type="email" />
              </mat-form-field>

              <mat-form-field appearance="outline" class="auth-field" subscriptSizing="dynamic">
                <mat-label>Password</mat-label>
                <input matInput formControlName="password" type="password" />
                <mat-hint>At least 8 characters and 1 digit.</mat-hint>
              </mat-form-field>

              <button mat-flat-button color="primary" type="submit" class="auth-submit" [disabled]="loading()">
                {{ loading() ? 'Signing in...' : 'Sign in' }}
              </button>
            </form>
          </mat-tab>

          <mat-tab label="Register">
            <form [formGroup]="registerForm" (ngSubmit)="register()" class="auth-form">
              <mat-form-field appearance="outline" class="auth-field" subscriptSizing="dynamic">
                <mat-label>Email</mat-label>
                <input matInput formControlName="email" type="email" />
              </mat-form-field>

              <mat-form-field appearance="outline" class="auth-field" subscriptSizing="dynamic">
                <mat-label>Password</mat-label>
                <input matInput formControlName="password" type="password" />
                <mat-hint>At least 8 characters and 1 digit.</mat-hint>
              </mat-form-field>

              <mat-form-field appearance="outline" class="auth-field" subscriptSizing="dynamic">
                <mat-label>User type</mat-label>
                <mat-select formControlName="role">
                  <mat-option value="User">User (standard)</mat-option>
                  <mat-option value="Clinician">Clinician (can approve recommendations)</mat-option>
                  <mat-option value="Admin">Admin (administrative access)</mat-option>
                </mat-select>
                <mat-hint>Select the account type to register.</mat-hint>
              </mat-form-field>

              <button mat-flat-button color="primary" type="submit" class="auth-submit" [disabled]="loading()">
                {{ loading() ? 'Creating account...' : 'Create account' }}
              </button>
            </form>
          </mat-tab>
        </mat-tab-group>

        <p class="error" *ngIf="error()">{{ error() }}</p>
      </mat-card>
    </div>
  `,
  styles: [
    `
      .auth-page { min-height: 100vh; display: grid; place-items: center; padding: 20px; }
      .auth-card { width: 100%; max-width: 430px; }
      .auth-title { text-align: center; margin-bottom: 8px; }
      .auth-form { display: flex; flex-direction: column; align-items: center; gap: 10px; margin-top: 10px; }
      .auth-field { width: 100%; max-width: 320px; }
      .auth-submit { width: 100%; max-width: 320px; min-height: 40px; }
      .error { color: #b00020; margin-top: 12px; }
    `,
  ],
})
export class LoginComponent {
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  protected readonly loginForm;
  protected readonly registerForm;

  constructor(
    private readonly fb: FormBuilder,
    private readonly auth: AuthService,
    private readonly router: Router
  ) {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]],
    });

    this.registerForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(8), Validators.pattern(/.*\d.*/)]],
      role: ['User', [Validators.required]],
    });
  }

  login(): void {
    if (this.loginForm.invalid) return;
    this.error.set('');
    this.loading.set(true);

    this.auth.login(this.loginForm.getRawValue() as { email: string; password: string }).subscribe({
      next: () => {
        this.auth.loadMe().subscribe({
          next: (me) => {
            this.loading.set(false);
            const targetRoute = me.roles?.includes('Clinician') ? '/clinician-review' : '/dashboard';
            this.router.navigate([targetRoute]);
          },
          error: () => {
            this.loading.set(false);
            this.error.set('Unable to load user profile after login.');
          },
        });
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Invalid email or password.');
      },
    });
  }

  register(): void {
    if (this.registerForm.invalid) return;
    this.error.set('');
    this.loading.set(true);

    this.auth.register(this.registerForm.getRawValue() as { email: string; password: string; role: string }).subscribe({
      next: () => {
        this.loading.set(false);
        this.error.set('Registration successful. Please sign in from the Login tab.');
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        const errors = Array.isArray(err?.error)
          ? err.error
              .map((item: { description?: string }) => item?.description)
              .filter((x: unknown): x is string => typeof x === 'string' && x.length > 0)
          : [];

        this.error.set(
          errors.length > 0
            ? errors.join(' ')
            : 'Registration failed. Email may already exist or password is weak.'
        );
      },
    });
  }
}
