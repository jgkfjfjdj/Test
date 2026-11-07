import { CheckCircle2, XCircle } from 'lucide-react';
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import { cn } from '../lib/cn';

interface ToastItem {
  id: number;
  tone: 'ok' | 'danger';
  message: string;
}

const ToastContext = createContext<{ show: (message: string, tone?: ToastItem['tone']) => void } | null>(
  null,
);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const next = useRef(1);
  const show = useCallback((message: string, tone: ToastItem['tone'] = 'ok') => {
    const id = next.current++;
    setItems((current) => [...current, { id, tone, message }]);
    setTimeout(
      () => setItems((current) => current.filter((t) => t.id !== id)),
      tone === 'danger' ? 8000 : 4000,
    );
  }, []);
  const value = useMemo(() => ({ show }), [show]);
  return (
    <ToastContext.Provider value={value}>
      {children}
      <div
        aria-live="polite"
        className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-2 p-4 pb-[max(1rem,env(safe-area-inset-bottom))] sm:items-end"
      >
        {items.map((t) => (
          <div
            key={t.id}
            role={t.tone === 'danger' ? 'alert' : 'status'}
            className={cn(
              'pointer-events-auto flex max-w-md items-start gap-2 rounded-lg border border-line bg-surface px-4 py-3 text-sm shadow-lg',
            )}
          >
            {t.tone === 'ok' ? (
              <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-ok" aria-hidden />
            ) : (
              <XCircle className="mt-0.5 size-4 shrink-0 text-danger" aria-hidden />
            )}
            <span>{t.message}</span>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast outside ToastProvider');
  return ctx;
}
