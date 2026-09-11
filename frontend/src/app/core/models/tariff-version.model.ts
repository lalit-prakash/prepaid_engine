/** One row of GET /api/v1/tariffs/{id}/versions. */
export interface TariffVersionSummary {
  id: string;
  fieldName: string;
  oldValue: string;
  newValue: string;
  changeNote: string;
  effectiveDate: string;
  recordedAt: string;
}
