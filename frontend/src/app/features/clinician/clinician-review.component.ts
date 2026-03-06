import { Component, signal } from '@angular/core';
import { DatePipe, NgFor, NgIf, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/services/api.service';
import { ClinicianRecommendationQueueItem } from '../../core/models/api.models';

@Component({
  selector: 'app-clinician-review',
  standalone: true,
  imports: [NgIf, NgFor, DatePipe, SlicePipe, FormsModule, MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, MatDividerModule, MatIconModule],
  template: `
    <div class="page-container">
      <mat-card class="header-card">
        <div class="header-content">
          <div>
            <h2 class="page-title">Clinician Review Queue</h2>
            <p class="page-subtitle">Review and approve AI-generated recommendations requested by patients.</p>
          </div>
          <button mat-stroked-button (click)="reload()" [disabled]="loading()">
            <mat-icon>refresh</mat-icon> Refresh
          </button>
        </div>
        <p *ngIf="error()" class="error-message">{{ error() }}</p>
      </mat-card>

      <div *ngIf="loading()" class="loading-state">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <mat-card *ngIf="!loading() && items().length === 0" class="empty-card">
        <mat-icon class="empty-icon">check_circle_outline</mat-icon>
        <p class="empty-text">No pending review requests. All caught up!</p>
      </mat-card>

      <mat-card *ngFor="let item of items()" class="review-card">
        <div class="card-header">
          <div class="title-row">
            <h3 class="rec-title">{{ item.title }}</h3>
            <span class="type-badge type-{{ item.type }}">{{ typeName(item.type) }}</span>
          </div>
          <div class="meta-row">
            <span class="meta-item"><mat-icon class="meta-icon">person</mat-icon>{{ item.userEmail || item.userId }}</span>
            <span class="meta-item"><mat-icon class="meta-icon">folder</mat-icon>{{ item.documentId | slice:0:8 }}…</span>
            <span class="meta-item"><mat-icon class="meta-icon">schedule</mat-icon>{{ item.createdAtUtc | date:'medium' }}</span>
          </div>
        </div>

        <mat-divider></mat-divider>

        <div class="card-body">
          <p class="content-label">Recommendation</p>
          <div *ngIf="!editingIds().has(item.id)" class="content-text">{{ item.content }}</div>
          <mat-form-field *ngIf="editingIds().has(item.id)" class="edit-field" appearance="outline">
            <mat-label>Edit recommendation text</mat-label>
            <textarea matInput
              rows="5"
              [value]="editContents[item.id] ?? item.content"
              (input)="onEditInput(item.id, $event)">
            </textarea>
          </mat-form-field>
        </div>

        <mat-divider></mat-divider>

        <div class="card-actions">
          <button mat-stroked-button
            (click)="toggleEdit(item)"
            [disabled]="approvingIds().has(item.id)">
            <mat-icon>{{ editingIds().has(item.id) ? 'close' : 'edit' }}</mat-icon>
            {{ editingIds().has(item.id) ? 'Cancel Edit' : 'Edit' }}
          </button>
          <button mat-flat-button color="primary"
            (click)="approve(item)"
            [disabled]="approvingIds().has(item.id)">
            <mat-spinner *ngIf="approvingIds().has(item.id)" diameter="18" class="btn-spinner"></mat-spinner>
            <mat-icon *ngIf="!approvingIds().has(item.id)">check_circle</mat-icon>
            {{ approvingIds().has(item.id) ? 'Approving…' : (editingIds().has(item.id) ? 'Approve with Changes' : 'Approve') }}
          </button>
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
      .empty-icon { font-size: 48px; height: 48px; width: 48px; color: #43a047; margin-bottom: 12px; }
      .empty-text { font-size: 15px; margin: 0; }

      .review-card { padding: 0; overflow: hidden; }
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

      .card-body { padding: 16px 20px; }
      .content-label { font-size: 11px; font-weight: 600; letter-spacing: 0.8px; text-transform: uppercase; color: #888; margin: 0 0 8px; }
      .content-text { font-size: 14px; line-height: 1.6; color: #333; white-space: pre-wrap; }
      .edit-field { width: 100%; margin-top: 4px; }

      .card-actions { display: flex; gap: 10px; padding: 12px 20px 16px; justify-content: flex-end; }
      .btn-spinner { display: inline-block; margin-right: 6px; }
    `,
  ],
})
export class ClinicianReviewComponent {
  protected readonly items = signal<ClinicianRecommendationQueueItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly approvingIds = signal<Set<string>>(new Set<string>());
  protected readonly editingIds = signal<Set<string>>(new Set<string>());
  protected editContents: Record<string, string> = {};

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

  toggleEdit(item: ClinicianRecommendationQueueItem): void {
    const id = item.id;
    const isEditing = this.editingIds().has(id);
    if (isEditing) {
      this.editingIds.update((s) => { const n = new Set(s); n.delete(id); return n; });
    } else {
      this.editContents[id] = item.content;
      this.editingIds.update((s) => { const n = new Set(s); n.add(id); return n; });
    }
  }

  onEditInput(id: string, event: Event): void {
    this.editContents[id] = (event.target as HTMLTextAreaElement).value;
  }

  approve(item: ClinicianRecommendationQueueItem): void {
    const isEditing = this.editingIds().has(item.id);
    const content = isEditing ? (this.editContents[item.id] ?? item.content) : undefined;

    this.approvingIds.update((s) => new Set([...s, item.id]));

    this.api.approveRecommendation(item.id, content).subscribe({
      next: () => {
        this.items.update((list) => list.filter((i) => i.id !== item.id));
        this.approvingIds.update((s) => { const n = new Set(s); n.delete(item.id); return n; });
        this.editingIds.update((s) => { const n = new Set(s); n.delete(item.id); return n; });
      },
      error: (e) => {
        this.error.set(e?.error?.message ?? 'Unable to approve recommendation.');
        this.approvingIds.update((s) => { const n = new Set(s); n.delete(item.id); return n; });
      },
    });
  }

  typeName(type: number): string {
    return type === 1 ? 'Insight' : type === 2 ? 'Risk Prediction' : 'Action';
  }
}
