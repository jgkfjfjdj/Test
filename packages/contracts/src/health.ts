import { Type, type Static } from 'typebox';

export const LivenessResponse = Type.Object(
  { status: Type.Literal('ok') },
  { $id: 'LivenessResponse', additionalProperties: false },
);
export type LivenessResponse = Static<typeof LivenessResponse>;

export const DependencyCheck = Type.Object(
  {
    status: Type.Union([Type.Literal('ok'), Type.Literal('fail')]),
    latencyMs: Type.Number({ minimum: 0 }),
    error: Type.Optional(Type.String()),
  },
  { additionalProperties: false },
);
export type DependencyCheck = Static<typeof DependencyCheck>;

export const ReadinessResponse = Type.Object(
  {
    status: Type.Union([Type.Literal('ok'), Type.Literal('degraded')]),
    checks: Type.Object(
      {
        postgres: DependencyCheck,
        redis: DependencyCheck,
      },
      { additionalProperties: false },
    ),
  },
  { $id: 'ReadinessResponse', additionalProperties: false },
);
export type ReadinessResponse = Static<typeof ReadinessResponse>;

export const BuildInfoResponse = Type.Object(
  {
    service: Type.String(),
    version: Type.String(),
    commit: Type.String(),
    environment: Type.String(),
    startedAt: Type.String({ format: 'date-time' }),
    uptimeSeconds: Type.Number({ minimum: 0 }),
  },
  { $id: 'BuildInfoResponse', additionalProperties: false },
);
export type BuildInfoResponse = Static<typeof BuildInfoResponse>;
