import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

/** The seven levels of the supply network, top to bottom. `id` is the value the API takes as a level name. */
export const NETWORK_LEVELS = [
  { id: 'zone', label: 'Zone', param: 'zoneId' },
  { id: 'circle', label: 'Circle', param: 'circleId' },
  { id: 'division', label: 'Division', param: 'divisionId' },
  { id: 'subdivision', label: 'Sub-division', param: 'subDivisionId' },
  { id: 'substation', label: 'Substation', param: 'substationId' },
  { id: 'feeder', label: 'Feeder', param: 'feederId' },
  { id: 'dtr', label: 'DTR', param: 'dtrId' },
] as const;

export type NetworkLevelId = (typeof NETWORK_LEVELS)[number]['id'];

export interface NetworkNode {
  id: string;
  code: string;
  name: string;
}

export interface NetworkSummary {
  zones: number;
  circles: number;
  divisions: number;
  subDivisions: number;
  substations: number;
  feeders: number;
  dtrs: number;
  consumersMapped: number;
  consumersUnmapped: number;
}

export interface ImportRowError {
  row: number;
  ok: boolean;
  message: string | null;
}

/** What an import or dry run reports. Nothing is saved when `rowsWithErrors` > 0 or `dryRun` is true. */
export interface ImportResult {
  dryRun: boolean;
  saved: boolean;
  rowsRead: number;
  rowsWithErrors: number;
  created: Record<string, number>;
  renamed: Record<string, number>;
  consumersMapped: number;
  consumersRemapped: number;
  consumersUnchanged: number;
  errors: ImportRowError[];
}

/** One line of a hierarchy file: the path down to one DTR. */
export interface HierarchyRow {
  zoneCode: string; zoneName: string;
  circleCode: string; circleName: string;
  divisionCode: string; divisionName: string;
  subDivisionCode: string; subDivisionName: string;
  substationCode: string; substationName: string;
  feederCode: string; feederName: string;
  dtrCode: string; dtrName: string;
}

export interface ConsumerMappingRow {
  accountNumber: string;
  dtrCode: string;
}

/** Talks to the /api/v1/network endpoints. */
@Injectable({ providedIn: 'root' })
export class NetworkService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/network`;

  constructor(private readonly http: HttpClient) {}

  nodes(level: NetworkLevelId, parentId?: string): Observable<NetworkNode[]> {
    const params: Record<string, string> = { level };
    if (parentId) params['parentId'] = parentId;
    return this.http.get<NetworkNode[]>(`${this.baseUrl}/nodes`, { params });
  }

  summary(): Observable<NetworkSummary> {
    return this.http.get<NetworkSummary>(`${this.baseUrl}/summary`);
  }

  importHierarchy(rows: HierarchyRow[], dryRun: boolean): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.baseUrl}/import`, { rows, dryRun });
  }

  mapConsumers(rows: ConsumerMappingRow[], dryRun: boolean): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.baseUrl}/consumer-mapping`, { rows, dryRun });
  }
}
