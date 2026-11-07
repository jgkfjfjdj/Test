/**
 * Display-name rules: 3–20 characters of letters, digits and underscores, at least one letter,
 * unique case-insensitively, and not impersonating staff or the game.
 * (Profanity filtering arrives with the text filter in M7.)
 */
const FORMAT = /^[A-Za-z0-9_]{3,20}$/;
const HAS_LETTER = /[A-Za-z]/;
const RESERVED_EXACT = new Set([
  'admin',
  'administrator',
  'mod',
  'moderator',
  'staff',
  'support',
  'system',
  'server',
  'official',
  'gm',
  'dev',
  'developer',
  'root',
  'null',
  'undefined',
  'anonymous',
  'deleted',
]);
const RESERVED_SUBSTRINGS = ['kindling', 'admin', 'moderator', 'official', 'staff_'];

export type DisplayNameProblem = 'format' | 'reserved';

export function displayNameKey(name: string): string {
  return name.toLowerCase();
}

export function checkDisplayName(name: string): DisplayNameProblem | null {
  if (!FORMAT.test(name) || !HAS_LETTER.test(name)) return 'format';
  const key = displayNameKey(name);
  if (RESERVED_EXACT.has(key)) return 'reserved';
  if (RESERVED_SUBSTRINGS.some((s) => key.includes(s))) return 'reserved';
  return null;
}

export const DISPLAY_NAME_RULES =
  '3–20 characters: letters, digits and underscores, with at least one letter. Names that impersonate staff or the game are not allowed.';
