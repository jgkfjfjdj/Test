/**
 * Staff permission catalogue (docs/blueprint/09-admin-panel.md §3).
 * Roles are rows in staff_roles (seeded by migration 0002); this file is the source of truth for
 * permission names and the default role definitions. An integration test checks they match.
 */
export const PERMISSIONS = [
  'players.read',
  'players.edit',
  'players.sanction',
  'players.sanction.permanent',
  'players.inventory.read',
  'players.inventory.write',
  'players.entitlements.grant',
  'reports.handle',
  'moderation.filters',
  'servers.read',
  'servers.command',
  'servers.manage',
  'versions.manage',
  'content.publish',
  'liveops.announcements',
  'liveops.events',
  'liveops.seasons',
  'liveops.config',
  'liveops.maintenance',
  'analytics.read',
  'audit.read',
  'system.db_monitor',
  'staff.manage',
] as const;

export type Permission = (typeof PERMISSIONS)[number];

/** Dangerous permissions: routes guarded by these also require a recent TOTP step-up (5 minutes). */
export const STEP_UP_PERMISSIONS: readonly Permission[] = [
  'players.sanction.permanent',
  'players.inventory.write',
  'players.entitlements.grant',
  'servers.manage',
  'versions.manage',
  'content.publish',
  'liveops.config',
  'liveops.maintenance',
  'staff.manage',
];

const all = [...PERMISSIONS];

export const DEFAULT_ROLES: Record<string, { name: string; permissions: readonly Permission[] }> = {
  super_admin: { name: 'Super Admin', permissions: all },
  admin: { name: 'Admin', permissions: all.filter((p) => p !== 'staff.manage') },
  moderator: {
    name: 'Moderator',
    permissions: [
      'players.read',
      'players.sanction',
      'players.inventory.read',
      'reports.handle',
      'moderation.filters',
      'servers.read',
      'servers.command',
    ],
  },
  support: {
    name: 'Support',
    permissions: [
      'players.read',
      'players.edit',
      'players.inventory.read',
      'players.entitlements.grant',
      'servers.read',
    ],
  },
  liveops: {
    name: 'Live Ops',
    permissions: [
      'servers.read',
      'content.publish',
      'liveops.announcements',
      'liveops.events',
      'liveops.seasons',
      'liveops.config',
      'analytics.read',
    ],
  },
  analyst: { name: 'Analyst', permissions: ['players.read', 'servers.read', 'analytics.read'] },
  server_ops: {
    name: 'Server Ops',
    permissions: [
      'servers.read',
      'servers.command',
      'servers.manage',
      'versions.manage',
      'liveops.maintenance',
      'system.db_monitor',
    ],
  },
};

export function isPermission(value: string): value is Permission {
  return (PERMISSIONS as readonly string[]).includes(value);
}
