import { Component, signal } from '@angular/core';
import { DatePipe, NgFor, NgIf, SlicePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ApiService } from '../../core/services/api.service';
import { ApprovedRecommendationItem } from '../../core/models/api.models';

@Component({
  selector: 'app-patient-review-history',
  standalone: true,
  imports: [NgIf, NgFor, DatePipe, SlicePipe, MatCardModule, MatButtonModule, MatDividerModule, MatProgressSpinnerModule],
  template: `
    <div class="page-container">
      <mat-card class="header-card">
        <div class="header-content">
          <div>
            <h2 class="page-title">Patient Review History</h2>
            <p class="page-subtitle">All recommendations you have approved for patients.</p>
          </div>
          <button mat-stroked-button (click)="reload()" [disabled]="loading()">
            Refresh
          </button>
        </div>
        <p *ngIf="error()" class="error-message">{{ error() }}</p>
      </mat-card>

      <div *ngIf="loading()" class="loading-state">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <mat-card *ngIf="!loading() && items().length === 0" class="empty-card">

        <p class="empty-text">No approved recommendations on record yet.</p>
      </mat-card>

      <mat-card *ngFor="let item of items()" class="history-card">
        <div class="card-header">
          <div class="title-row">
            <h3 class="rec-title">{{ item.title }}</h3>
            <span class="type-badge type-{{ item.type }}">{{ typeName(item.type) }}</span>
          </div>
          <div class="meta-row">
            <span class="meta-item">{{ item.userEmail || item.userId }}</span>
            <span class="meta-item">Doc: {{ item.documentId | slice:0:8 }}…</span>
            <span class="meta-item approved-date">
              ✓ Approved {{ item.approvedAtUtc | date:'medium' }}
            </span>
          </div>
        </div>

        <mat-divider></mat-divider>

        <div class="card-body">
          <p class="content-label">Approved Recommendation</p>
          <p class="content-text">{{ item.content }}</p>
        </div>
      </mat-card>
    </div>
  `,
  styles: [
    `
      .page-container { max-width: 860px; margin: 0 auto; display: flex; flex-direction: column; gap: 16px; }
      .header-card { padding: 20px 24px 16px; }
      .header-content { display: flex; justify-content: space-between; align-items: flex-start; flex-wrap: wrap; gap: 12px; }
      .page-title { margin: 0 0 6px; font-size: 22px; font-weight: 600; color: #1a237e; }
      .page-subtitle { margin: 0; color: #555; font-size: 14px; }
      .error-message { color: #b00020; margin: 12px 0 0; font-size: 13px; }

      .loading-state { display: flex; justify-content: center; padding: 40px 0; }

      .empty-card { display: flex; flex-direction: column; align-items: center; padding: 40px 24px; color: #777; }
      .empty-icon { font-size: 48px; height: 48px; width: 48px; color: #9e9e9e; margin-bottom: 12px; }
      .empty-text { font-size: 15px; margin: 0; }

      .history-card { padding: 0; overflow: hidden; }
      .card-header { padding: 18px 20px 14px; }
      .title-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 10px; }
      .rec-title { margin: 0; font-size: 17px; font-weight: 600; flex: 1; }
      .type-badge {
        font-size: 11px; font-weight: 600; letter-spacing: 0.5px; text-transform: uppercase;
        padding: 3px 10px; border-radius: 999px; white-space: nowrap;
      }
      .type-badge.type-1 { background: #e3f2fd; color: #1565c0; }
      .type-badge.type-2 { background: #fff3e0; color: #e65100; }
      .type-badge.type-3 { background: #e8f5e9; color: #2e7d32; }

      .meta-row { display: flex; gap: 20px; flex-wrap: wrap; }
      .meta-item { display: flex; align-items: center; gap: 4px; font-size: 13px; color: #555; }
      .meta-icon { font-size: 15px; height: 15px; width: 15px; color: #888; }
      .approved-date { color: #2e7d32; font-weight: 500; }
      .approved-date .meta-icon { color: #43a047; }

      .card-body { padding: 16px 20px 20px; }
      .content-label { font-size: 11px; font-weight: 600; letter-spacing: 0.8px; text-transform: uppercase; color: #888; margin: 0 0 8px; }
      .content-text { font-size: 14px; line-height: 1.6; color: #333; margin: 0; white-space: pre-wrap; }
    `,
  ],
})
export class PatientReviewHistoryComponent {
  protected readonly items = signal<ApprovedRecommendationItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  constructor(private readonly api: ApiService) {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.getApprovedRecommendationReviews().subscribe({
      next: (rows) => {
        this.items.set(rows);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.error.set(e?.error?.message ?? 'Unable to load review history.');
        this.items.set([]);
      },
    });
  }

  typeName(type: number): string {
    return type === 1 ? 'Insight' : type === 2 ? 'Risk Prediction' : 'Action';
  }
}
