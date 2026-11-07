import { Link } from '@tanstack/react-router';
import { CheckCircle2, ExternalLink, KeyRound } from 'lucide-react';
import QRCode from 'qrcode';
import { useEffect, useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import { Alert, Button, Field, Input, Spinner } from '../../components/ui';
import { AuthLayout, CodeInput } from './auth-layout';

type Enrollment = { email: string; secret: string; otpauthUri: string };

function groupSecret(secret: string): string {
  return secret.replace(/(.{4})/g, '$1 ').trim();
}

/**
 * Invitation setup (link from the invite email): 1) add the account to an authenticator app,
 * 2) choose a password and confirm the first code.
 */
export function SetupPage({ token }: { token: string | undefined }) {
  const [enrollment, setEnrollment] = useState<Enrollment | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [totp, setTotp] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    call(() => client.POST('/admin/v1/auth/setup/begin', { body: { token } }))
      .then(async (data) => {
        if (cancelled) return;
        setEnrollment(data);
        setQr(await QRCode.toDataURL(data.otpauthUri, { margin: 1, width: 220 }));
      })
      .catch((err: unknown) => !cancelled && setLoadError(errorMessage(err)));
    return () => {
      cancelled = true;
    };
  }, [token]);

  if (!token) {
    return (
      <AuthLayout title="Setup link missing">
        <Alert>Open the full link from your invitation email.</Alert>
      </AuthLayout>
    );
  }
  if (done) {
    return (
      <AuthLayout title="You're all set">
        <div className="space-y-4">
          <Alert tone="ok">
            <span className="inline-flex items-center gap-2">
              <CheckCircle2 className="size-4" aria-hidden /> Your staff account is active.
            </span>
          </Alert>
          <Link
            to="/login"
            className="inline-flex min-h-11 w-full items-center justify-center rounded-lg bg-accent px-4 text-sm font-medium text-on-accent"
          >
            Go to sign in
          </Link>
        </div>
      </AuthLayout>
    );
  }
  if (loadError) {
    return (
      <AuthLayout title="Setup link not valid">
        <Alert>{loadError}</Alert>
        <p className="mt-3 text-sm text-muted">
          Links expire after 72 hours. Ask an administrator for a new invitation.
        </p>
      </AuthLayout>
    );
  }
  if (!enrollment) {
    return (
      <AuthLayout title="Set up your staff account">
        <Spinner label="Preparing your authenticator key" />
      </AuthLayout>
    );
  }

  const mismatch = confirm.length > 0 && password !== confirm;
  return (
    <AuthLayout
      title="Set up your staff account"
      subtitle={
        <>
          Signing in as <strong>{enrollment.email}</strong>
        </>
      }
    >
      <ol className="space-y-6">
        <li>
          <h2 className="text-sm font-semibold">1. Add KINDLING to your authenticator app</h2>
          <p className="mt-1 text-sm text-muted">
            Scan the code, or on this phone tap the button. You can also type the key by hand.
          </p>
          <div className="mt-3 flex flex-col items-center gap-3">
            {qr && (
              <img
                src={qr}
                alt="QR code for your authenticator app"
                className="size-48 rounded-lg bg-white p-2"
              />
            )}
            <a
              href={enrollment.otpauthUri}
              className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-line px-4 text-sm font-medium hover:bg-surface-2"
            >
              <ExternalLink className="size-4" aria-hidden /> Open in authenticator app
            </a>
            <div className="w-full rounded-lg bg-surface-2 px-3 py-2 text-center">
              <div className="text-xs text-muted">Setup key</div>
              <div data-testid="totp-secret" className="font-mono text-sm break-all select-all">
                {groupSecret(enrollment.secret)}
              </div>
            </div>
          </div>
        </li>
        <li>
          <h2 className="text-sm font-semibold">2. Choose a password and confirm a code</h2>
          <form
            className="mt-3 space-y-4"
            onSubmit={async (e) => {
              e.preventDefault();
              if (mismatch) return;
              setBusy(true);
              setError(null);
              try {
                await call(() =>
                  client.POST('/admin/v1/auth/setup/complete', { body: { token, password, totp } }),
                );
                setDone(true);
              } catch (err) {
                setError(errorMessage(err));
                setTotp('');
              } finally {
                setBusy(false);
              }
            }}
          >
            <Field label="Password" hint="At least 14 characters. A long passphrase is easiest.">
              {(p) => (
                <Input
                  {...p}
                  type="password"
                  autoComplete="new-password"
                  minLength={14}
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              )}
            </Field>
            <Field label="Repeat password" error={mismatch ? 'The passwords are different.' : null}>
              {(p) => (
                <Input
                  {...p}
                  type="password"
                  autoComplete="new-password"
                  required
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                />
              )}
            </Field>
            <Field label="Code from the authenticator app">
              {(p) => <CodeInput {...p} value={totp} onChange={setTotp} />}
            </Field>
            {error && <Alert>{error}</Alert>}
            <Button
              type="submit"
              variant="primary"
              className="w-full"
              loading={busy}
              disabled={mismatch}
              icon={<KeyRound className="size-4" aria-hidden />}
            >
              Activate account
            </Button>
          </form>
        </li>
      </ol>
    </AuthLayout>
  );
}
