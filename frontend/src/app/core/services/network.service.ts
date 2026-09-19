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

/** Talks to GET /api/v1/network/nodes. */
@Injectable({ providedIn: 'root' })
export class NetworkService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/network`;

  constructor(private readonly http: HttpClient) {}

  nodes(level: NetworkLevelId, parentId?: string): Observable<NetworkNode[]> {
    const params: Record<string, string> = { level };
    if (parentId) params['parentId'] = parentId;
    return this.http.get<NetworkNode[]>(`${this.baseUrl}/nodes`, { params });
  }
}
