import { Check, Copy } from 'lucide-react';
import { useState } from 'react';
import { Button } from './ui';

/** Read-only secret/link with a copy button. */
export function CopyField({ label, value, secret }: { label: string; value: string; secret?: boolean }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };
  return (
    <div className="space-y-1.5">
      <span className="block text-sm font-medium">{label}</span>
      <div className="flex gap-2">
        <output
          aria-label={label}
          className={
            'min-h-11 flex-1 overflow-x-auto rounded-lg border border-line bg-surface-2 px-3 py-2.5 font-mono text-sm break-all ' +
            (secret ? 'select-all' : '')
          }
        >
          {value}
        </output>
        <Button
          onClick={copy}
          aria-label={`Copy ${label}`}
          icon={copied ? <Check className="size-4" aria-hidden /> : <Copy className="size-4" aria-hidden />}
        >
          <span className="hidden sm:inline">{copied ? 'Copied' : 'Copy'}</span>
        </Button>
      </div>
    </div>
  );
}
