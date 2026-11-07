import { Type, type Static } from 'typebox';

/**
 * Game server ↔ backend link protocol (WebSocket, JSON text frames).
 * docs/blueprint/06-dedicated-servers.md §5 and docs/protocol/server-link.md.
 *
 * Envelope: { t: type, id?: request id, re?: id being answered, d: payload }.
 * The game server must send `hello` first and then `heartbeat` every heartbeatIntervalSeconds.
 */
export const SERVER_LINK_PATH = '/server/v1/link';
export const TICKET_ISSUER = 'kindling-tickets';

/** WebSocket close codes used by the backend. */
export const LinkCloseCodes = {
  replaced: 4000,
  protocolViolation: 4002,
  tokenRevoked: 4003,
  helloTimeout: 4004,
} as const;

const Id = Type.String({ minLength: 1, maxLength: 64 });
const Uuid = Type.String({
  pattern: '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
});

export const HelloPayload = Type.Object(
  {
    buildVersion: Type.String({ minLength: 1, maxLength: 64 }),
    protocolVersion: Type.Integer({ minimum: 1 }),
    /** Public DNS name (preferred, NAT64-friendly) or IP clients connect to. */
    host: Type.String({ minLength: 1, maxLength: 253 }),
    gamePort: Type.Integer({ minimum: 1, maximum: 65535 }),
    queryPort: Type.Integer({ minimum: 1, maximum: 65535 }),
    maxPlayers: Type.Integer({ minimum: 1, maximum: 200 }),
    /** argon2id hash of the join password, or null for public servers. Never the password itself. */
    passwordHash: Type.Union([Type.String({ pattern: '^\\$argon2id\\$', maxLength: 200 }), Type.Null()]),
    mapId: Type.String({ minLength: 1, maxLength: 64 }),
    mode: Type.Union([Type.Literal('pvp'), Type.Literal('pve')]),
    tags: Type.Array(Type.String({ minLength: 1, maxLength: 24 }), { maxItems: 12 }),
    /** Rates and rules shown in the server browser (gather multiplier, sleepers, raid rules…). */
    configSummary: Type.Record(
      Type.String({ maxLength: 40 }),
      Type.Union([Type.String({ maxLength: 100 }), Type.Number(), Type.Boolean()]),
    ),
    lastWipeAt: Type.Union([Type.String({ maxLength: 40 }), Type.Null()]),
    nextWipeAt: Type.Union([Type.String({ maxLength: 40 }), Type.Null()]),
  },
  { additionalProperties: false },
);
export type HelloPayload = Static<typeof HelloPayload>;

export const HeartbeatPayload = Type.Object(
  {
    players: Type.Integer({ minimum: 0, maximum: 1000 }),
    queue: Type.Integer({ minimum: 0, maximum: 10000 }),
    tickMsAvg: Type.Number({ minimum: 0, maximum: 10000 }),
    tickMsP99: Type.Number({ minimum: 0, maximum: 10000 }),
    fps: Type.Number({ minimum: 0, maximum: 1000 }),
    memoryMb: Type.Integer({ minimum: 0 }),
    entities: Type.Integer({ minimum: 0 }),
    uptimeSeconds: Type.Integer({ minimum: 0 }),
  },
  { additionalProperties: false },
);
export type HeartbeatPayload = Static<typeof HeartbeatPayload>;

export const PlayerJoinedPayload = Type.Object({ accountId: Uuid, jti: Id }, { additionalProperties: false });
export const PlayerLeftPayload = Type.Object(
  { accountId: Uuid, reason: Type.String({ maxLength: 64 }) },
  { additionalProperties: false },
);
export const CommandResultPayload = Type.Object(
  {
    ok: Type.Boolean(),
    result: Type.Optional(Type.Unknown()),
    error: Type.Optional(Type.String({ maxLength: 500 })),
  },
  { additionalProperties: false },
);

/** Commands the backend can send in M2. More arrive with moderation (M7) and owner tools (M8). */
export const SERVER_COMMANDS = ['broadcast', 'kick', 'save', 'restart'] as const;
export type ServerCommandName = (typeof SERVER_COMMANDS)[number];

export const CommandPayload = Type.Object(
  {
    name: Type.Union(SERVER_COMMANDS.map((c) => Type.Literal(c))),
    args: Type.Record(Type.String(), Type.Unknown()),
  },
  { additionalProperties: false },
);
export type CommandPayload = Static<typeof CommandPayload>;

export interface LinkEnvelope<T = unknown> {
  t: string;
  id?: string;
  re?: string;
  d: T;
}
