export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  role: string;
}

export interface LoginResponse {
  token: string;
}

export interface MeResponse {
  userId: string;
  email: string;
  roles: string[];
}

export interface PresignRequest {
  fileName: string;
  contentType: string;
  docType: number;
}

export interface PresignResponse {
  uploadUrl: string;
  bucket: string;
  objectKey: string;
  expiresAtUtc: string;
}

export interface FinalizeRequest {
  objectKey: string;
  fileName: string;
  contentType: string;
  docType: number;
  sizeBytes?: number;
  sha256?: string;
}

export interface FinalizeResponse {
  id: string;
  jobId?: string;
  alreadyExists?: boolean;
}

export interface DirectUploadResponse {
  id: string;
  jobId: string;
  objectKey: string;
}

export interface UploadStatusResponse {
  documentId: string;
  documentStatus: number;
  latestJobId?: string;
  latestJobStatus?: number;
  latestJobError?: string;
  missingMandatoryBiomarkers?: string[];
}

export interface ManualBiomarkerInput {
  biomarkerName: string;
  value: number;
  unit: string;
  normalizedValue?: number;
  normalizedUnit?: string;
  observedAtUtc?: string;
}

export interface ManualBiomarkersRequest {
  documentId: string;
  biomarkers: ManualBiomarkerInput[];
}

export interface ManualBiomarkersResponse {
  documentId: string;
  savedCount: number;
  latestJobId?: string;
  latestJobStatus?: number;
  latestJobError?: string;
  missingMandatoryBiomarkers?: string[];
}

export interface InsightRecommendation {
  id: string;
  type: number;
  status: number;
  priority: number;
  title: string;
  content: string;
  evidenceJson?: string;
  createdAtUtc: string;
  isClinicianApproved: boolean;
  approvedAtUtc?: string;
}

export interface InsightSnapshotResponse {
  documentId: string;
  snapshotId: string;
  overallScore: number;
  confidence: number;
  riskBand: number;
  modelVersion: string;
  breakdownJson: string;
  scope: string;
  recencyStrategy: string;
  contributingDocumentIds: string[];
  createdAtUtc: string;
  recommendations: InsightRecommendation[];
}

export interface TrackedDocument {
  documentId: string;
  fileName: string;
  contentType: string;
  createdAtUtc: string;
}

export interface DocumentHistoryItem {
  documentId: string;
  fileName?: string;
  contentType?: string;
  documentType: number;
  documentStatus: number;
  createdAtUtc: string;
  processedAtUtc?: string;
  latestJobId?: string;
  latestJobStatus?: number;
  latestJobError?: string;
  biomarkerCount: number;
  manualBiomarkerCount: number;
  missingMandatoryBiomarkers?: string[];
}

export interface BiomarkerHistoryItem {
  id: string;
  documentId?: string;
  biomarkerCode: string;
  sourceName?: string;
  value: number;
  unit: string;
  sourceType: number;
  observedAtUtc: string;
  createdAtUtc: string;
}

export interface JobHistoryItem {
  id: string;
  documentId?: string;
  type: number;
  status: number;
  attemptCount: number;
  createdAtUtc: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
  error?: string;
}

export interface InsightHistoryItem {
  snapshotId: string;
  documentId: string;
  overallScore: number;
  confidence: number;
  riskBand: number;
  modelVersion: string;
  createdAtUtc: string;
  recommendationCount: number;
  approvedRecommendationCount: number;
  reviewRequestedRecommendationCount: number;
}

export interface ClinicianRecommendationQueueItem {
  id: string;
  userId: string;
  userEmail?: string;
  documentId: string;
  scoreSnapshotId?: string;
  type: number;
  status: number;
  priority: number;
  title: string;
  content: string;
  createdAtUtc: string;
}

export interface ParsedInsufficientError {
  code?: string;
  message?: string;
  missingMandatoryBiomarkers?: string[];
  presentMandatoryBiomarkers?: string[];
  extractedCanonicalBiomarkerCount?: number;
  minimumRequiredCanonicalBiomarkerCount?: number;
}
