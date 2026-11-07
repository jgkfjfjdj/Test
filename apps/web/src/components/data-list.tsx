import type { ReactNode } from 'react';
import { cn } from '../lib/cn';

export interface Column<T> {
  header: string;
  cell: (row: T) => ReactNode;
  /** Hide this column in the phone card layout (e.g. when it's already in the card title). */
  hideOnCard?: boolean;
  className?: string;
}

/**
 * A table on wider screens and a stack of cards on phones (each card shows label/value pairs).
 * `rowLabel` gives every row an accessible name and is the card title.
 */
export function DataList<T>({
  rows,
  columns,
  rowKey,
  rowLabel,
  rowActions,
  caption,
}: {
  rows: T[];
  columns: Column<T>[];
  rowKey: (row: T) => string;
  rowLabel: (row: T) => ReactNode;
  rowActions?: (row: T) => ReactNode;
  caption: string;
}) {
  return (
    <>
      <div className="hidden overflow-x-auto rounded-xl border border-line bg-surface md:block">
        <table className="w-full text-left text-sm">
          <caption className="sr-only">{caption}</caption>
          <thead className="border-b border-line bg-surface-2 text-xs tracking-wide text-muted uppercase">
            <tr>
              {columns.map((c) => (
                <th key={c.header} scope="col" className={cn('px-4 py-2.5 font-medium', c.className)}>
                  {c.header}
                </th>
              ))}
              {rowActions && (
                <th scope="col" className="px-4 py-2.5 text-right font-medium">
                  <span className="sr-only">Actions</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map((row) => (
              <tr key={rowKey(row)} className="align-middle">
                {columns.map((c) => (
                  <td key={c.header} className={cn('px-4 py-3', c.className)}>
                    {c.cell(row)}
                  </td>
                ))}
                {rowActions && <td className="px-4 py-3 text-right whitespace-nowrap">{rowActions(row)}</td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ul className="space-y-3 md:hidden" aria-label={caption}>
        {rows.map((row) => (
          <li key={rowKey(row)} className="rounded-xl border border-line bg-surface p-4">
            <div className="mb-2 font-medium">{rowLabel(row)}</div>
            <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
              {columns
                .filter((c) => !c.hideOnCard)
                .map((c) => (
                  <div key={c.header} className="min-w-0">
                    <dt className="text-xs text-muted">{c.header}</dt>
                    <dd className="truncate">{c.cell(row)}</dd>
                  </div>
                ))}
            </dl>
            {rowActions && <div className="mt-3 flex flex-wrap gap-2">{rowActions(row)}</div>}
          </li>
        ))}
      </ul>
    </>
  );
}
