-- M2 seed data. Roles mirror DEFAULT_ROLES in packages/contracts/src/permissions.ts
-- (an integration test fails if they drift). Later role changes are made in the admin console.
INSERT INTO "staff_roles" ("id", "name", "permissions") VALUES
  ('super_admin', 'Super Admin', ARRAY['players.read','players.edit','players.sanction','players.sanction.permanent','players.inventory.read','players.inventory.write','players.entitlements.grant','reports.handle','moderation.filters','servers.read','servers.command','servers.manage','versions.manage','content.publish','liveops.announcements','liveops.events','liveops.seasons','liveops.config','liveops.maintenance','analytics.read','audit.read','system.db_monitor','staff.manage']::text[]),
  ('admin', 'Admin', ARRAY['players.read','players.edit','players.sanction','players.sanction.permanent','players.inventory.read','players.inventory.write','players.entitlements.grant','reports.handle','moderation.filters','servers.read','servers.command','servers.manage','versions.manage','content.publish','liveops.announcements','liveops.events','liveops.seasons','liveops.config','liveops.maintenance','analytics.read','audit.read','system.db_monitor']::text[]),
  ('moderator', 'Moderator', ARRAY['players.read','players.sanction','players.inventory.read','reports.handle','moderation.filters','servers.read','servers.command']::text[]),
  ('support', 'Support', ARRAY['players.read','players.edit','players.inventory.read','players.entitlements.grant','servers.read']::text[]),
  ('liveops', 'Live Ops', ARRAY['servers.read','content.publish','liveops.announcements','liveops.events','liveops.seasons','liveops.config','analytics.read']::text[]),
  ('analyst', 'Analyst', ARRAY['players.read','servers.read','analytics.read']::text[]),
  ('server_ops', 'Server Ops', ARRAY['servers.read','servers.command','servers.manage','versions.manage','liveops.maintenance','system.db_monitor']::text[])
ON CONFLICT ("id") DO NOTHING;
--> statement-breakpoint
-- Default iOS version policy: every build is accepted until the first real release sets these values.
INSERT INTO "version_policy" ("platform", "min_supported_build", "latest_build", "store_url")
VALUES ('ios', 0, 0, 'https://apps.apple.com/')
ON CONFLICT ("platform") DO NOTHING;
