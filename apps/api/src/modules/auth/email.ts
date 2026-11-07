const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Light syntactic check; the verification email is the real proof of ownership. */
export function normalizeEmail(input: string): string | null {
  const email = input.trim();
  if (email.length < 3 || email.length > 254 || !EMAIL.test(email)) return null;
  return email;
}
