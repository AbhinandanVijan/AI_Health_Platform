import { Component, OnDestroy, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NgFor, NgIf } from '@angular/common';
import { Subscription, interval } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApiService } from '../../core/services/api.service';
import { ParsedInsufficientError, UploadStatusResponse } from '../../core/models/api.models';

@Component({
  selector: 'app-uploads',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    NgIf,
    NgFor,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <mat-card>
      <h2>Upload Lab File</h2>
      <input class="file-input" type="file" (change)="onFileSelected($event)" accept=".pdf,.png,.jpg,.jpeg" />
      <mat-form-field appearance="outline">
        <mat-label>Document Type</mat-label>
        <mat-select [value]="docType()" (valueChange)="docType.set($event)">
          <mat-option [value]="1">Lab PDF</mat-option>
        </mat-select>
      </mat-form-field>
      <button mat-flat-button color="primary" (click)="upload()" [disabled]="!selectedFile() || loading()">
        {{ loading() ? 'Uploading...' : 'Upload & Start Processing' }}
      </button>
      <p *ngIf="lastDocId()">Last uploaded document: {{ lastDocId() }}</p>
      <p *ngIf="info()">{{ info() }}</p>
      <p class="error" *ngIf="error()">{{ error() }}</p>
    </mat-card>

    <mat-card *ngIf="lastDocId() as docId">
      <h3>Status for {{ docId }}</h3>
      <button mat-stroked-button (click)="refreshStatus()">Refresh Status</button>
      <button mat-stroked-button (click)="reprocess()">Reprocess</button>
      <div *ngIf="status() as s" class="status-block">
        <p>Document status: {{ documentStatusLabel(s.documentStatus) }}</p>
        <p>Job status: {{ s.latestJobStatus ? jobStatusLabel(s.latestJobStatus) : '-' }}</p>
        <p *ngIf="s.missingMandatoryBiomarkers?.length">Missing mandatory: {{ s.missingMandatoryBiomarkers?.join(', ') }}</p>
      </div>
      <div *ngIf="insufficientError() as ie" class="insufficient">
        <p><strong>{{ ie.message }}</strong></p>
        <p *ngIf="ie.missingMandatoryBiomarkers?.length">Missing: {{ ie.missingMandatoryBiomarkers?.join(', ') }}</p>
      </div>
    </mat-card>

    <mat-card *ngIf="lastDocId() as docId">
      <h3>Manually Add Missing Biomarkers</h3>
      <form [formGroup]="manualForm" (ngSubmit)="submitManual()">
        <div formArrayName="biomarkers" class="manual-rows">
          <div class="manual-row" *ngFor="let row of biomarkerRows.controls; let i = index" [formGroupName]="i">
            <mat-form-field appearance="outline">
              <mat-label>Biomarker name</mat-label>
              <input matInput formControlName="biomarkerName" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Value</mat-label>
              <input matInput type="number" formControlName="value" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Unit</mat-label>
              <mat-select formControlName="unit">
                <mat-option *ngFor="let unit of standardUnits" [value]="unit">{{ unit }}</mat-option>
              </mat-select>
            </mat-form-field>
            <button mat-button color="warn" type="button" (click)="removeRow(i)">Remove</button>
          </div>
        </div>
        <div class="manual-actions">
          <button mat-stroked-button color="primary" class="manual-action-button" type="button" (click)="addRow()">Add biomarker</button>
          <button mat-flat-button color="primary" class="manual-action-button" type="submit">Save manual biomarkers</button>
        </div>
      </form>
    </mat-card>
  `,
  styles: [
    `
      mat-card { margin-bottom: 14px; }
      .file-input { display: block; margin-bottom: 14px; }
      .status-block, .insufficient { margin-top: 10px; }
      .error { color: #b00020; }
      .manual-rows { display: grid; gap: 10px; }
      .manual-row { display: grid; grid-template-columns: 1fr 120px 120px auto; gap: 8px; align-items: center; }
      .manual-actions { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 10px; }
      .manual-action-button { min-height: 40px; min-width: 180px; }
      @media (max-width: 900px) { .manual-row { grid-template-columns: 1fr; } }
    `,
  ],
})
export class UploadsComponent implements OnDestroy {
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly docType = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly lastDocId = signal<string | null>(null);
  protected readonly status = signal<UploadStatusResponse | null>(null);
  protected readonly insufficientError = signal<ParsedInsufficientError | null>(null);
  protected readonly info = signal('');
  protected readonly standardUnits = [
    'mg/dL',
    'g/dL',
    'mmol/L',
    'IU/L',
    'U/L',
    'ng/mL',
    'pg/mL',
    '%',
    'mEq/L',
    'cells/uL',
  ];

  protected readonly manualForm;
  private pollSub?: Subscription;

  constructor(private readonly api: ApiService, private readonly fb: FormBuilder) {
    this.manualForm = this.fb.group({
      biomarkers: this.fb.array([]),
    });
    this.addRow();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  get biomarkerRows(): FormArray {
    return this.manualForm.get('biomarkers') as FormArray;
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  upload(): void {
    const file = this.selectedFile();
    if (!file) return;

    this.loading.set(true);
    this.error.set('');

    this.api.directUpload(file, this.docType()).subscribe({
      next: (res) => {
        this.lastDocId.set(res.id);
        this.api.trackDocument({
          documentId: res.id,
          fileName: file.name,
          contentType: file.type || 'application/octet-stream',
          createdAtUtc: new Date().toISOString(),
        });
        this.loading.set(false);
        this.refreshStatus();
        this.startPolling();
      },
      error: (e) => {
        this.loading.set(false);
        this.error.set(e?.error?.message ?? 'Upload failed.');
      },
    });
  }

  refreshStatus(): void {
    const docId = this.lastDocId();
    if (!docId) return;

    this.api.getUploadStatus(docId).subscribe({
      next: (res) => {
        this.status.set(res);
        this.insufficientError.set(this.api.parseInsufficientError(res.latestJobError));
        if (res.latestJobStatus === 3 || res.latestJobStatus === 4 || res.latestJobStatus === 5) {
          this.stopPolling();
        }

        if (res.latestJobStatus === 4) {
          this.info.set('Parsing failed. You can reprocess the file or add biomarkers manually if needed.');
        } else if (res.latestJobStatus === 5) {
          this.info.set('Insufficient mandatory biomarkers detected. Add remaining biomarkers manually below.');
        } else if (res.latestJobStatus === 2 || res.documentStatus === 2) {
          this.info.set('Processing in progress... status refresh is active.');
        } else {
          this.info.set('');
        }
      },
      error: () => this.error.set('Unable to fetch status.'),
    });
  }

  reprocess(): void {
    const docId = this.lastDocId();
    if (!docId) return;
    this.api.reprocess(docId).subscribe({
      next: () => {
        this.refreshStatus();
        this.startPolling();
      },
      error: () => this.error.set('Reprocess request failed.'),
    });
  }

  addRow(): void {
    this.biomarkerRows.push(this.fb.group({
      biomarkerName: ['', Validators.required],
      value: [null, Validators.required],
      unit: ['', Validators.required],
    }));
  }

  removeRow(index: number): void {
    this.biomarkerRows.removeAt(index);
  }

  submitManual(): void {
    const docId = this.lastDocId();
    if (!docId || this.manualForm.invalid) return;

    const biomarkers = this.biomarkerRows.controls.map((row) => ({
      biomarkerName: row.get('biomarkerName')?.value,
      value: Number(row.get('value')?.value),
      unit: row.get('unit')?.value,
      observedAtUtc: new Date().toISOString(),
    }));

    this.api.upsertManualBiomarkers({ documentId: docId, biomarkers }).subscribe({
      next: () => {
        this.refreshStatus();
        this.info.set('Manual biomarkers saved. Status has been recomputed.');
      },
      error: () => this.error.set('Manual biomarker save failed.'),
    });
  }

  private startPolling(): void {
    this.stopPolling();
    this.pollSub = interval(3000).subscribe(() => this.refreshStatus());
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
  }

  documentStatusLabel(status: number): string {
    switch (status) {
      case 1:
        return 'Uploaded';
      case 2:
        return 'Processing';
      case 3:
        return 'Processed';
      case 4:
        return 'Failed';
      default:
        return `Unknown (${status})`;
    }
  }

  jobStatusLabel(status: number): string {
    switch (status) {
      case 1:
        return 'Ready';
      case 2:
        return 'Processing';
      case 3:
        return 'Succeeded';
      case 4:
        return 'Failed';
      case 5:
        return 'Insufficient Data';
      default:
        return `Unknown (${status})`;
    }
  }
}
