const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });
const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : dateTime.format(date);
}

export function formatRelative(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return 'never';
  const diff = new Date(iso).getTime() - now;
  const abs = Math.abs(diff);
  if (abs < 45_000) return diff < 0 ? 'just now' : 'in a moment';
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['minute', 60_000],
    ['hour', 3_600_000],
    ['day', 86_400_000],
  ];
  for (let i = units.length - 1; i >= 0; i--) {
    const [unit, ms] = units[i]!;
    if (abs >= ms || unit === 'minute') return relative.format(Math.round(diff / ms), unit);
  }
  return dateTime.format(new Date(iso));
}

export function formatDuration(seconds: number): string {
  const d = Math.floor(seconds / 86_400);
  const h = Math.floor((seconds % 86_400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}

export function formatNumber(value: number, fractionDigits = 0): string {
  return value.toLocaleString(undefined, {
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
  });
}

/** Converts a <input type="datetime-local"> value to ISO (local time zone). */
export function localInputToIso(value: string): string | undefined {
  if (!value) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}
