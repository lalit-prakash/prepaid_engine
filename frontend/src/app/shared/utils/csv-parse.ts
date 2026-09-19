/**
 * Parses CSV text into rows of cells. Handles quoted cells (with doubled quotes and embedded commas or
 * line breaks), CRLF or LF line endings and a leading byte-order mark. Blank lines are dropped.
 */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;
  const source = text.replace(/^﻿/, '');

  for (let i = 0; i < source.length; i++) {
    const ch = source[i];
    if (quoted) {
      if (ch === '"' && source[i + 1] === '"') { cell += '"'; i++; }
      else if (ch === '"') quoted = false;
      else cell += ch;
    } else if (ch === '"') {
      quoted = true;
    } else if (ch === ',') {
      row.push(cell); cell = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && source[i + 1] === '\n') i++;
      row.push(cell); cell = '';
      if (row.some((c) => c.trim() !== '')) rows.push(row);
      row = [];
    } else {
      cell += ch;
    }
  }
  row.push(cell);
  if (row.some((c) => c.trim() !== '')) rows.push(row);
  return rows;
}

/** Turns parsed CSV into objects keyed by header name (matched case-insensitively, ignoring spaces, dashes and underscores). */
export function csvToObjects(rows: string[][], keys: string[]): { objects: Record<string, string>[]; missing: string[] } {
  if (rows.length === 0) return { objects: [], missing: keys };
  const normalise = (s: string) => s.toLowerCase().replace(/[\s_-]/g, '');
  const header = rows[0].map(normalise);
  const index = new Map(keys.map((k) => [k, header.indexOf(normalise(k))]));
  const missing = keys.filter((k) => (index.get(k) ?? -1) < 0);
  const objects = rows.slice(1).map((r) => {
    const o: Record<string, string> = {};
    for (const k of keys) {
      const i = index.get(k) ?? -1;
      o[k] = i >= 0 ? (r[i] ?? '').trim() : '';
    }
    return o;
  });
  return { objects, missing };
}
