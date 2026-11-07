import type { ReactNode } from 'react';

export function AuthLayout({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: ReactNode;
  children: ReactNode;
}) {
  return (
    <main className="flex min-h-dvh items-start justify-center px-4 py-10 sm:items-center">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex items-center gap-3">
          <img src="/admin/favicon.svg" alt="" className="size-10" />
          <div>
            <div className="text-sm font-semibold tracking-wide">KINDLING</div>
            <div className="text-xs text-muted">Staff console</div>
          </div>
        </div>
        <div className="rounded-2xl border border-line bg-surface p-5 shadow-sm sm:p-6">
          <h1 className="text-xl font-semibold">{title}</h1>
          {subtitle && <p className="mt-1 text-sm text-muted">{subtitle}</p>}
          <div className="mt-5">{children}</div>
        </div>
      </div>
    </main>
  );
}

export function CodeInput(props: {
  id: string;
  value: string;
  onChange: (v: string) => void;
  'aria-describedby'?: string;
}) {
  return (
    <input
      id={props.id}
      aria-describedby={props['aria-describedby']}
      required
      inputMode="numeric"
      autoComplete="one-time-code"
      pattern="[0-9]{6}"
      maxLength={6}
      value={props.value}
      onChange={(e) => props.onChange(e.target.value.replace(/\D/g, ''))}
      className="min-h-11 w-full rounded-lg border border-line bg-surface px-3 font-mono text-lg tracking-[0.4em] focus:border-accent focus:ring-2 focus:ring-accent/25 focus:outline-none"
    />
  );
}
