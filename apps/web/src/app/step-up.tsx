import { useQueryClient } from '@tanstack/react-query';
import { ShieldCheck } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { ApiError, client, errorMessage, setStepUpHandler } from '../api/client';
import { Dialog } from '../components/dialog';
import { Alert, Button, Field, Input } from '../components/ui';
import { ME_KEY } from './session';

/**
 * Global step-up prompt. When an API call answers "staff.step_up_required", the request waits
 * here until the user confirms a fresh authenticator code, then it is retried automatically.
 */
export function StepUpDialog() {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pending = useRef<{ promise: Promise<boolean>; resolve: (ok: boolean) => void } | null>(null);

  useEffect(() => {
    setStepUpHandler(() => {
      if (!pending.current) {
        let resolve!: (ok: boolean) => void;
        const promise = new Promise<boolean>((r) => (resolve = r));
        pending.current = { promise, resolve };
        setCode('');
        setError(null);
        setOpen(true);
      }
      return pending.current.promise;
    });
    return () => setStepUpHandler(null);
  }, []);

  const finish = (ok: boolean) => {
    pending.current?.resolve(ok);
    pending.current = null;
    setOpen(false);
  };

  return (
    <Dialog
      open={open}
      onClose={() => finish(false)}
      title="Confirm it's you"
      description="This action changes live settings. Enter the current code from your authenticator app. You won't be asked again for 5 minutes."
      onSubmit={async () => {
        setBusy(true);
        setError(null);
        try {
          const { response, error: body } = await client.POST('/admin/v1/auth/step-up', {
            body: { totp: code },
          });
          if (!response.ok) {
            const problem = (body ?? {}) as { code?: string; detail?: string; title?: string };
            throw new ApiError(response.status, problem.code ?? 'http', {
              status: response.status,
              code: problem.code ?? 'http',
              title: problem.title ?? 'Error',
              ...problem,
            });
          }
          await queryClient.invalidateQueries({ queryKey: ME_KEY });
          finish(true);
        } catch (err) {
          setError(errorMessage(err));
        } finally {
          setBusy(false);
        }
      }}
      footer={
        <>
          <Button onClick={() => finish(false)}>Cancel</Button>
          <Button
            type="submit"
            variant="primary"
            loading={busy}
            icon={<ShieldCheck className="size-4" aria-hidden />}
          >
            Confirm
          </Button>
        </>
      }
    >
      <Field label="Authenticator code">
        {(props) => (
          <Input
            {...props}
            autoFocus
            required
            inputMode="numeric"
            autoComplete="one-time-code"
            pattern="[0-9]{6}"
            maxLength={6}
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
            className="font-mono tracking-[0.3em]"
          />
        )}
      </Field>
      {error && <Alert>{error}</Alert>}
    </Dialog>
  );
}
