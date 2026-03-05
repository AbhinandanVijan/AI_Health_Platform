import { Component, signal } from '@angular/core';
import { DatePipe, NgFor, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { ApiService } from '../../core/services/api.service';
import { ClinicianRecommendationQueueItem } from '../../core/models/api.models';

@Component({
  selector: 'app-clinician-review',
  standalone: true,
  imports: [NgIf, NgFor, DatePipe, MatCardModule, MatButtonModule],
  template: `
    <mat-card>
      <h2>Clinician Review Queue</h2>
      <p>Approve AI-generated recommendations that users have requested for clinician validation.</p>
      <button mat-button (click)="reload()">Refresh</button>
      <p *ngIf="error()" class="error">{{ error() }}</p>
    </mat-card>

    <mat-card *ngIf="!loading() && items().length === 0" class="queue-item">
      <p>No pending review requests.</p>
    </mat-card>

    <mat-card *ngFor="let item of items()" class="queue-item">
      <h3>{{ item.title }}</h3>
      <p><strong>User:</strong> {{ item.userEmail || item.userId }}</p>
      <p><strong>Document:</strong> {{ item.documentId }}</p>
      <p><strong>Requested at:</strong> {{ item.createdAtUtc | date:'medium' }}</p>
      <p>{{ item.content }}</p>
      <button
        mat-flat-button
        color="primary"
        (click)="approve(item.id)"
        [disabled]="approvingIds().has(item.id)">
        {{ approvingIds().has(item.id) ? 'Approving...' : 'Approve Recommendation' }}
      </button>
    </mat-card>
  `,
  styles: [
    `
      .queue-item { margin-top: 12px; }
      .error { color: #b00020; margin-top: 8px; }
      p { margin: 6px 0; }
    `,
  ],
})
export class ClinicianReviewComponent {
  protected readonly items = signal<ClinicianRecommendationQueueItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly approvingIds = signal<Set<string>>(new Set<string>());

  constructor(private readonly api: ApiService) {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.getPendingRecommendationReviews().subscribe({
      next: (rows) => {
        this.items.set(rows);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.error.set(e?.error?.message ?? 'Unable to load clinician queue.');
        this.items.set([]);
      },
    });
  }

  approve(recommendationId: string): void {
    this.approvingIds.update((current) => {
      const next = new Set(current);
      next.add(recommendationId);
      return next;
    });

    this.api.approveRecommendation(recommendationId).subscribe({
      next: () => {
        this.items.update((current) => current.filter((item) => item.id !== recommendationId));
        this.approvingIds.update((current) => {
          const next = new Set(current);
          next.delete(recommendationId);
          return next;
        });
      },
      error: (e) => {
        this.error.set(e?.error?.message ?? 'Unable to approve recommendation.');
        this.approvingIds.update((current) => {
          const next = new Set(current);
          next.delete(recommendationId);
          return next;
        });
      },
    });
  }
}
