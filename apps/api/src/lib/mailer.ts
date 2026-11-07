import nodemailer, { type Transporter } from 'nodemailer';
import type { FastifyBaseLogger } from 'fastify';

export interface MailMessage {
  to: string;
  subject: string;
  text: string;
}

export interface Mailer {
  send(message: MailMessage): Promise<void>;
}

/**
 * Development/test mailer: logs a one-line notice (never the body, which contains secrets) and
 * keeps recent messages in memory so tests and local tools can read them.
 */
export class MemoryMailer implements Mailer {
  readonly sent: (MailMessage & { at: Date })[] = [];

  constructor(private readonly log?: FastifyBaseLogger) {}

  async send(message: MailMessage): Promise<void> {
    this.sent.push({ ...message, at: new Date() });
    if (this.sent.length > 200) this.sent.shift();
    this.log?.info({ to: message.to, subject: message.subject }, 'mail captured (MAIL_TRANSPORT=log)');
  }

  /** Most recent message sent to an address. */
  lastTo(address: string): (MailMessage & { at: Date }) | undefined {
    return [...this.sent].reverse().find((m) => m.to.toLowerCase() === address.toLowerCase());
  }
}

/** SMTP delivery (Mailpit locally; a transactional provider's SMTP endpoint in production). */
export class SmtpMailer implements Mailer {
  private readonly transport: Transporter;

  constructor(
    smtpUrl: string,
    private readonly from: string,
  ) {
    this.transport = nodemailer.createTransport(smtpUrl);
  }

  async send(message: MailMessage): Promise<void> {
    await this.transport.sendMail({
      from: this.from,
      to: message.to,
      subject: message.subject,
      text: message.text,
    });
  }

  close(): void {
    this.transport.close();
  }
}
