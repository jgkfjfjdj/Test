import { Navigate, useNavigate } from '@tanstack/react-router';
import { LogIn } from 'lucide-react';
import { useState } from 'react';
import { ApiError, call, client, errorMessage } from '../../api/client';
import { useSession } from '../../app/session';
import { Alert, Button, Field, Input } from '../../components/ui';
import { AuthLayout, CodeInput } from './auth-layout';

export function LoginPage({ redirect }: { redirect?: string | undefined }) {
  const { staff, sessionEnded, signedIn } = useSession();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [totp, setTotp] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (staff) return <Navigate to={(redirect as '/') ?? '/'} />;

  return (
    <AuthLayout title="Sign in" subtitle="Use your staff email, password and authenticator code.">
      <form
        className="space-y-4"
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          setError(null);
          try {
            const me = await call(() =>
              client.POST('/admin/v1/auth/login', { body: { email, password, totp } }),
            );
            signedIn(me);
            await navigate({ to: (redirect as '/') ?? '/' });
          } catch (err) {
            setError(
              err instanceof ApiError && err.status === 429
                ? 'Too many attempts. Wait 15 minutes and try again.'
                : errorMessage(err),
            );
            setTotp('');
          } finally {
            setBusy(false);
          }
        }}
      >
        {sessionEnded && !error && <Alert tone="info">Your session ended. Sign in again to continue.</Alert>}
        <Field label="Email">
          {(p) => (
            <Input
              {...p}
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          )}
        </Field>
        <Field label="Password">
          {(p) => (
            <Input
              {...p}
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          )}
        </Field>
        <Field label="Authenticator code" hint="6 digits from your authenticator app.">
          {(p) => <CodeInput {...p} value={totp} onChange={setTotp} />}
        </Field>
        {error && <Alert>{error}</Alert>}
        <Button
          type="submit"
          variant="primary"
          className="w-full"
          loading={busy}
          icon={<LogIn className="size-4" aria-hidden />}
        >
          Sign in
        </Button>
      </form>
      <p className="mt-4 text-xs text-muted">
        Lost your authenticator? Ask an administrator to reset your credentials — you'll get a new setup link.
      </p>
    </AuthLayout>
  );
}
