// Production bundle: workspace packages (@kindling/*) are bundled in; npm dependencies stay external
// and are installed in the image by `pnpm deploy --prod`.
import { readFileSync } from 'node:fs';
import { build } from 'esbuild';

const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8'));
const external = Object.keys(pkg.dependencies ?? {}).filter((name) => !name.startsWith('@kindling/'));
// Bundled workspace code imports these directly; they are also declared in its package.json.
external.push('drizzle-orm', 'drizzle-orm/*', 'postgres', 'typebox/*');

await build({
  entryPoints: {
    main: 'src/main.ts',
    migrate: '../../packages/db/src/cli/migrate.ts',
  },
  outdir: 'dist',
  bundle: true,
  platform: 'node',
  target: 'node22',
  format: 'esm',
  sourcemap: true,
  external,
  banner: {
    // Allow bundled CommonJS helpers to use require() inside an ES module bundle.
    js: "import { createRequire as __kindlingCreateRequire } from 'node:module'; const require = __kindlingCreateRequire(import.meta.url);",
  },
  logLevel: 'info',
});
