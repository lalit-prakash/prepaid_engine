import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export type ReportJobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Expired';

/** One export request, as GET /api/v1/report-jobs returns it. */
export interface ReportJob {
  id: string;
  report: string;
  status: ReportJobStatus;
  requestedBy: string;
  requestedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  expiresAt: string | null;
  rowCount: number;
  fileSizeBytes: number | null;
  error: string | null;
  parameters: Record<string, unknown>;
  canDownload: boolean;
}

/** What the API needs to build a full export: the report and the same filters the report page uses. */
export interface ReportJobRequest {
  report: string;
  from?: string | null;
  to?: string | null;
  status?: string | null;
  [level: string]: string | null | undefined;
}

/** Talks to the /api/v1/report-jobs endpoints: request a full export, watch it, download the CSV. */
@Injectable({ providedIn: 'root' })
export class ReportJobService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/report-jobs`;

  constructor(private readonly http: HttpClient) {}

  request(body: ReportJobRequest): Observable<ReportJob> {
    return this.http.post<ReportJob>(this.baseUrl, body);
  }

  list(): Observable<ReportJob[]> {
    return this.http.get<ReportJob[]>(this.baseUrl);
  }

  /** Fetches the finished file with the user's credentials and hands it to the browser's normal download flow. */
  download(job: ReportJob): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${encodeURIComponent(job.id)}/download`, { responseType: 'blob' }).pipe(
      tap((blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${job.report}-${job.requestedAt.slice(0, 10)}.csv`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
      }),
    );
  }
}
