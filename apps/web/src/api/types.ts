import type { paths } from './schema';

type Json<T> = T extends { content: { 'application/json': infer B } } ? B : never;
type Ok<Op> = Op extends { responses: infer R }
  ? R extends { 200: infer S }
    ? Json<S>
    : R extends { 201: infer S }
      ? Json<S>
      : never
  : never;

export type StaffMe = Ok<paths['/admin/v1/auth/me']['get']>;
export type AdminServer = Ok<paths['/admin/v1/servers']['get']>['items'][number];
export type CreatedServer = Ok<paths['/admin/v1/servers']['post']>;
export type MaintenanceWindow = Ok<paths['/admin/v1/maintenance']['get']>['items'][number];
export type ConfigEntry = Ok<paths['/admin/v1/config']['get']>['items'][number];
export type VersionPolicy = Ok<paths['/admin/v1/version-policy']['get']>['items'][number];
export type StaffMember = Ok<paths['/admin/v1/staff']['get']>['items'][number];
export type RolesResponse = Ok<paths['/admin/v1/staff/roles']['get']>;
export type AuditPage = Ok<paths['/admin/v1/audit']['get']>;
export type AuditItem = AuditPage['items'][number];
export type BuildInfo = Ok<paths['/health/info']['get']>;
export type CommandResult = Ok<paths['/admin/v1/servers/{id}/commands']['post']>;
export type MaintenanceScope = MaintenanceWindow['scope'];
