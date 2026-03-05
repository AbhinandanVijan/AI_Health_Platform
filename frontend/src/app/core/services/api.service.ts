import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  BiomarkerHistoryItem,
  ClinicianRecommendationQueueItem,
  DirectUploadResponse,
  DocumentHistoryItem,
  FinalizeRequest,
  FinalizeResponse,
  InsightRecommendation,
  InsightSnapshotResponse,
  JobHistoryItem,
  ManualBiomarkersRequest,
  ManualBiomarkersResponse,
  ParsedInsufficientError,
  PresignRequest,
  PresignResponse,
  InsightHistoryItem,
  TrackedDocument,
  UploadStatusResponse,
} from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly trackedDocsKey = 'aihealth.trackedDocs';

  constructor(private readonly http: HttpClient) {}

  presign(payload: PresignRequest): Observable<PresignResponse> {
    return this.http.post<PresignResponse>('/api/uploads/presign', payload);
  }

  directUpload(file: File, docType: number): Observable<DirectUploadResponse> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('docType', String(docType));
    return this.http.post<DirectUploadResponse>('/api/uploads/direct', formData);
  }

  finalize(payload: FinalizeRequest): Observable<FinalizeResponse> {
    return this.http.post<FinalizeResponse>('/api/uploads/finalize', payload);
  }

  uploadToPresignedUrl(url: string, file: File): Observable<void> {
    return new Observable<void>((observer) => {
      fetch(url, {
        method: 'PUT',
        headers: {
          'Content-Type': file.type,
        },
        body: file,
      })
        .then((res) => {
          if (!res.ok) {
            throw new Error(`Upload failed with status ${res.status}`);
          }
          observer.next();
          observer.complete();
        })
        .catch((err) => observer.error(err));
    });
  }

  getUploadStatus(docId: string): Observable<UploadStatusResponse> {
    return this.http.get<UploadStatusResponse>(`/api/uploads/status/${docId}`);
  }

  reprocess(docId: string): Observable<unknown> {
    return this.http.post(`/api/uploads/reprocess/${docId}`, {});
  }

  upsertManualBiomarkers(payload: ManualBiomarkersRequest): Observable<ManualBiomarkersResponse> {
    return this.http.post<ManualBiomarkersResponse>('/api/biomarkers/manual', payload);
  }

  getAggregateInsights(): Observable<InsightSnapshotResponse> {
    return this.http.get<InsightSnapshotResponse>('/api/insights/latest');
  }

  generateAggregateInsights(): Observable<InsightSnapshotResponse> {
    return this.http.post<InsightSnapshotResponse>('/api/insights/generate', {});
  }

  getDocumentInsights(docId: string): Observable<InsightSnapshotResponse> {
    return this.http.get<InsightSnapshotResponse>(`/api/insights/${docId}`);
  }

  generateDocumentInsights(docId: string): Observable<InsightSnapshotResponse> {
    return this.http.post<InsightSnapshotResponse>(`/api/insights/generate/${docId}`, {});
  }

  requestRecommendationReview(recommendationId: string): Observable<InsightRecommendation> {
    return this.http.post<InsightRecommendation>(`/api/insights/recommendations/${recommendationId}/request-review`, {});
  }

  approveRecommendation(recommendationId: string): Observable<InsightRecommendation> {
    return this.http.post<InsightRecommendation>(`/api/insights/recommendations/${recommendationId}/approve`, {});
  }

  getPendingRecommendationReviews(skip = 0, take = 50): Observable<ClinicianRecommendationQueueItem[]> {
    return this.http.get<ClinicianRecommendationQueueItem[]>(`/api/insights/recommendations/pending?skip=${skip}&take=${take}`);
  }

  getHistoryDocuments(skip = 0, take = 20): Observable<DocumentHistoryItem[]> {
    return this.http.get<DocumentHistoryItem[]>(`/api/history/documents?skip=${skip}&take=${take}`);
  }

  getDocumentBiomarkers(docId: string): Observable<BiomarkerHistoryItem[]> {
    return this.http.get<BiomarkerHistoryItem[]>(`/api/history/documents/${docId}/biomarkers`);
  }

  getDocumentJobs(docId: string): Observable<JobHistoryItem[]> {
    return this.http.get<JobHistoryItem[]>(`/api/history/documents/${docId}/jobs`);
  }

  getBiomarkerFeed(skip = 0, take = 50): Observable<BiomarkerHistoryItem[]> {
    return this.http.get<BiomarkerHistoryItem[]>(`/api/history/biomarkers?skip=${skip}&take=${take}`);
  }

  getInsightHistory(skip = 0, take = 20): Observable<InsightHistoryItem[]> {
    return this.http.get<InsightHistoryItem[]>(`/api/history/insights?skip=${skip}&take=${take}`);
  }

  parseInsufficientError(raw: string | undefined): ParsedInsufficientError | null {
    if (!raw) return null;
    try {
      return JSON.parse(raw) as ParsedInsufficientError;
    } catch {
      return null;
    }
  }

  getTrackedDocuments(): TrackedDocument[] {
    const raw = localStorage.getItem(this.trackedDocsKey);
    if (!raw) return [];
    try {
      const parsed = JSON.parse(raw) as TrackedDocument[];
      if (!Array.isArray(parsed)) return [];
      return parsed;
    } catch {
      return [];
    }
  }

  trackDocument(document: TrackedDocument): void {
    const items = this.getTrackedDocuments();
    const exists = items.some((d) => d.documentId === document.documentId);
    const next = exists
      ? items.map((d) => (d.documentId === document.documentId ? document : d))
      : [document, ...items];
    localStorage.setItem(this.trackedDocsKey, JSON.stringify(next.slice(0, 100)));
  }
}
