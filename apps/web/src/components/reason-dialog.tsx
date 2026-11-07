import { useState, type ReactNode } from 'react';
import { errorMessage } from '../api/client';
import { Dialog } from './dialog';
import { Alert, Button, Field, Textarea } from './ui';

/**
 * Confirmation for audited actions: asks for the reason that is stored in the audit log.
 * `onConfirm` runs the request; errors are shown inline and the dialog stays open.
 */
export function ReasonDialog({
  open,
  onClose,
  title,
  description,
  confirmLabel,
  danger,
  onConfirm,
  children,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  onConfirm: (reason: string) => Promise<unknown>;
  children?: ReactNode;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const close = () => {
    setReason('');
    setError(null);
    onClose();
  };
  return (
    <Dialog
      open={open}
      onClose={close}
      title={title}
      description={description}
      onSubmit={async () => {
        setBusy(true);
        setError(null);
        try {
          await onConfirm(reason.trim());
          close();
        } catch (err) {
          setError(errorMessage(err));
        } finally {
          setBusy(false);
        }
      }}
      footer={
        <>
          <Button onClick={close}>Cancel</Button>
          <Button type="submit" variant={danger ? 'danger' : 'primary'} loading={busy}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      {children}
      <ReasonField value={reason} onChange={setReason} />
      {error && <Alert>{error}</Alert>}
    </Dialog>
  );
}

export function ReasonField({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <Field label="Reason" hint="Required. Stored in the audit log with your name.">
      {(props) => (
        <Textarea
          {...props}
          required
          minLength={3}
          maxLength={500}
          rows={2}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder="Why is this change being made?"
        />
      )}
    </Field>
  );
}
