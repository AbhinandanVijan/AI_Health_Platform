import { Component, computed } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgIf } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NgIf,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatSidenavModule,
    MatListModule,
    MatDividerModule,
  ],
  template: `
    <mat-sidenav-container class="shell-container">
      <mat-sidenav mode="side" opened class="shell-sidenav">
        <div class="brand">AI Health Platform</div>
        <mat-divider></mat-divider>
        <mat-nav-list>
          <!-- Clinician navigation -->
          <ng-container *ngIf="isClinician()">
            <a mat-list-item routerLink="/clinician-review" routerLinkActive="active-link">Clinician Review</a>
            <a mat-list-item routerLink="/review-history" routerLinkActive="active-link">Review History</a>
          </ng-container>
          <!-- Regular user navigation -->
          <ng-container *ngIf="!isClinician()">
            <a mat-list-item routerLink="/dashboard" routerLinkActive="active-link">Dashboard</a>
            <a mat-list-item routerLink="/uploads" routerLinkActive="active-link">Uploads & Status</a>
            <a mat-list-item routerLink="/history" routerLinkActive="active-link">My History</a>
            <a mat-list-item routerLink="/profile" routerLinkActive="active-link">My Profile</a>
          </ng-container>
        </mat-nav-list>
      </mat-sidenav>

      <mat-sidenav-content>
        <mat-toolbar color="primary">
          <span>Health Insights</span>
          <span class="spacer"></span>
          <span class="role-badge" *ngIf="displayRole()">{{ displayRole() }}</span>
          <span class="user-email" *ngIf="userEmail()">{{ userEmail() }}</span>
          <button mat-button (click)="logout()">Logout</button>
        </mat-toolbar>

        <main class="page-content">
          <router-outlet></router-outlet>
        </main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: [
    `
      .shell-container { min-height: 100vh; }
      .shell-sidenav { width: 240px; }
      .brand { font-size: 18px; font-weight: 600; padding: 16px; }
      .active-link { background: rgba(63, 81, 181, 0.12); border-radius: 8px; }
      .spacer { flex: 1; }
      .role-badge {
        margin-right: 10px;
        padding: 3px 10px;
        border-radius: 999px;
        background: rgba(255, 255, 255, 0.2);
        font-size: 12px;
        font-weight: 600;
      }
      .user-email { margin-right: 12px; font-size: 13px; opacity: 0.9; }
      .page-content { padding: 20px; }
    `,
  ],
})
export class ShellComponent {
  protected readonly userEmail = computed(() => this.auth.me()?.email ?? null);
  protected readonly isClinician = computed(() => this.auth.me()?.roles?.includes('Clinician') ?? false);
  protected readonly displayRole = computed(() => {
    const roles = this.auth.me()?.roles ?? [];
    if (roles.includes('Admin')) return 'Admin';
    if (roles.includes('Clinician')) return 'Clinician';
    if (roles.includes('User')) return 'User';
    return roles[0] ?? null;
  });

  constructor(private readonly auth: AuthService, private readonly router: Router) {
    if (!this.auth.me()) {
      this.auth.loadMe().subscribe({ error: () => this.logout() });
    }
  }

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
