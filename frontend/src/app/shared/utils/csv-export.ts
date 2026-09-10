/**
 * Client-side CSV export for report tables — real data the user already has on screen, encoded
 * as CSV and handed to the browser's normal download flow (a Blob + temporary <a download>).
 * No backend report-generation infrastructure exists yet, so this is the whole export path;
 * it only ever serializes data already fetched from the real API, never invents rows.
 */
export function exportToCsv(filename: string, headers: string[], rows: (string | number)[][]): void {
  const escapeCell = (cell: string | number): string => {
    const text = String(cell);
    return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
  };

  const lines = [headers, ...rows].map((row) => row.map(escapeCell).join(','));
  const csvContent = lines.join('\r\n');

  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
