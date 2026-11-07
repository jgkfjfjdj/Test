import { X } from 'lucide-react';
import { useEffect, useId, useRef, type FormEvent, type ReactNode } from 'react';
import { cn } from '../lib/cn';
import { Button } from './ui';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: ReactNode;
  children: ReactNode;
  /** Footer buttons. If `onSubmit` is set, the body is a <form> and Enter submits. */
  footer?: ReactNode;
  onSubmit?: (event: FormEvent<HTMLFormElement>) => void;
  size?: 'md' | 'lg';
}

/**
 * Modal built on the native <dialog> element (focus trapping, Esc to close and inert background
 * come from the browser). On phones it becomes a bottom sheet.
 */
export function Dialog({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  onSubmit,
  size = 'md',
}: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);

  const body = (
    <>
      <header className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
        <div>
          <h2 id={titleId} className="text-lg font-semibold">
            {title}
          </h2>
          {description && <p className="mt-1 text-sm text-muted">{description}</p>}
        </div>
        <Button variant="ghost" size="sm" aria-label="Close" onClick={onClose} className="-mr-2 px-2">
          <X className="size-4" aria-hidden />
        </Button>
      </header>
      <div className="max-h-[70dvh] space-y-4 overflow-y-auto px-5 py-4">{children}</div>
      {footer && (
        <footer className="flex flex-wrap justify-end gap-2 border-t border-line px-5 py-3">{footer}</footer>
      )}
    </>
  );

  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      onClose={onClose}
      onCancel={(e) => {
        e.preventDefault();
        onClose();
      }}
      className={cn(
        'm-0 mt-auto w-full max-w-none rounded-t-2xl border border-line bg-surface p-0 text-ink shadow-xl',
        'sm:m-auto sm:rounded-2xl',
        size === 'lg' ? 'sm:max-w-2xl' : 'sm:max-w-lg',
      )}
    >
      {open &&
        (onSubmit ? (
          <form
            noValidate={false}
            onSubmit={(e) => {
              e.preventDefault();
              onSubmit(e);
            }}
          >
            {body}
          </form>
        ) : (
          body
        ))}
    </dialog>
  );
}
