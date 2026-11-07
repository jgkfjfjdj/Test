CREATE TABLE "audit_log" (
	"id" uuid NOT NULL,
	"occurred_at" timestamp with time zone DEFAULT now() NOT NULL,
	"actor_type" text NOT NULL,
	"actor_id" uuid,
	"action" text NOT NULL,
	"target_type" text,
	"target_id" text,
	"reason" text,
	"before" jsonb,
	"after" jsonb,
	"result" text DEFAULT 'ok' NOT NULL,
	"request_id" text,
	"ip_hash" "bytea",
	CONSTRAINT "pk_audit_log" PRIMARY KEY("id","occurred_at"),
	CONSTRAINT "ck_audit_log_actor_type" CHECK ("audit_log"."actor_type" IN ('staff','owner','server','system'))
) PARTITION BY RANGE ("occurred_at");
--> statement-breakpoint
-- Hand-edited: audit_log is range-partitioned by month. Until the scheduled job that creates
-- monthly partitions exists (M9), every row lands in the DEFAULT partition.
CREATE TABLE "audit_log_default" PARTITION OF "audit_log" DEFAULT;
--> statement-breakpoint
-- Hand-edited: append-only enforcement for every role (old partitions are removed with DETACH/DROP, not DELETE).
CREATE FUNCTION "audit_log_append_only"() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'audit_log is append-only (% blocked)', TG_OP USING ERRCODE = 'insufficient_privilege';
END;
$$;
--> statement-breakpoint
CREATE TRIGGER "audit_log_no_update_delete" BEFORE UPDATE OR DELETE ON "audit_log"
  FOR EACH ROW EXECUTE FUNCTION "audit_log_append_only"();
--> statement-breakpoint
CREATE TABLE "accounts" (
	"id" uuid PRIMARY KEY NOT NULL,
	"display_name" text NOT NULL,
	"display_name_key" text NOT NULL,
	"email" "citext",
	"email_verified_at" timestamp with time zone,
	"password_hash" text,
	"status" text DEFAULT 'active' NOT NULL,
	"deletion_requested_at" timestamp with time zone,
	"last_login_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "uq_accounts_display_name_key" UNIQUE("display_name_key"),
	CONSTRAINT "uq_accounts_email" UNIQUE("email"),
	CONSTRAINT "ck_accounts_status" CHECK ("accounts"."status" IN ('active','suspended','pending_deletion','deleted'))
);
--> statement-breakpoint
CREATE TABLE "auth_identities" (
	"id" uuid PRIMARY KEY NOT NULL,
	"account_id" uuid NOT NULL,
	"provider" text NOT NULL,
	"subject" text NOT NULL,
	"email" text,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "uq_auth_identities_provider_subject" UNIQUE("provider","subject"),
	CONSTRAINT "ck_auth_identities_provider" CHECK ("auth_identities"."provider" IN ('apple'))
);
--> statement-breakpoint
CREATE TABLE "email_tokens" (
	"id" uuid PRIMARY KEY NOT NULL,
	"account_id" uuid NOT NULL,
	"purpose" text NOT NULL,
	"token_hash" "bytea" NOT NULL,
	"expires_at" timestamp with time zone NOT NULL,
	"used_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "uq_email_tokens_hash" UNIQUE("token_hash"),
	CONSTRAINT "ck_email_tokens_purpose" CHECK ("email_tokens"."purpose" IN ('verify','reset'))
);
--> statement-breakpoint
CREATE TABLE "refresh_tokens" (
	"id" uuid PRIMARY KEY NOT NULL,
	"account_id" uuid NOT NULL,
	"family_id" uuid NOT NULL,
	"token_hash" "bytea" NOT NULL,
	"expires_at" timestamp with time zone NOT NULL,
	"rotated_at" timestamp with time zone,
	"revoked_at" timestamp with time zone,
	"revoke_reason" text,
	"client_version" text,
	"user_agent" text,
	"created_ip_hash" "bytea",
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "uq_refresh_tokens_hash" UNIQUE("token_hash")
);
--> statement-breakpoint
CREATE TABLE "maintenance_windows" (
	"id" uuid PRIMARY KEY NOT NULL,
	"scope" text NOT NULL,
	"starts_at" timestamp with time zone NOT NULL,
	"ends_at" timestamp with time zone,
	"message" jsonb NOT NULL,
	"created_by" uuid,
	"cancelled_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "ck_maintenance_scope" CHECK ("maintenance_windows"."scope" IN ('full','login','matchmaking','store'))
);
--> statement-breakpoint
CREATE TABLE "remote_config" (
	"key" text PRIMARY KEY NOT NULL,
	"value" jsonb NOT NULL,
	"scope" text DEFAULT 'client' NOT NULL,
	"description" text,
	"version" integer DEFAULT 1 NOT NULL,
	"updated_by" uuid,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "ck_remote_config_scope" CHECK ("remote_config"."scope" IN ('client','server','both'))
);
--> statement-breakpoint
CREATE TABLE "version_policy" (
	"platform" text PRIMARY KEY NOT NULL,
	"min_supported_build" integer NOT NULL,
	"latest_build" integer NOT NULL,
	"store_url" text NOT NULL,
	"updated_by" uuid,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "cloud_saves" (
	"account_id" uuid NOT NULL,
	"slot" text NOT NULL,
	"version" integer NOT NULL,
	"data" jsonb NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "pk_cloud_saves" PRIMARY KEY("account_id","slot"),
	CONSTRAINT "ck_cloud_saves_slot" CHECK ("cloud_saves"."slot" IN ('settings','hud','loadout','tutorial'))
);
--> statement-breakpoint
CREATE TABLE "profiles" (
	"account_id" uuid PRIMARY KEY NOT NULL,
	"level" integer DEFAULT 1 NOT NULL,
	"xp" bigint DEFAULT 0 NOT NULL,
	"loadout" jsonb DEFAULT '{}'::jsonb NOT NULL,
	"privacy" jsonb DEFAULT '{}'::jsonb NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "game_servers" (
	"id" uuid PRIMARY KEY NOT NULL,
	"kind" text NOT NULL,
	"owner_account_id" uuid,
	"name" text NOT NULL,
	"description" text DEFAULT '' NOT NULL,
	"region" text NOT NULL,
	"host" text,
	"game_port" integer,
	"query_port" integer,
	"max_players" integer DEFAULT 50 NOT NULL,
	"password_hash" text,
	"map_id" text,
	"mode" text DEFAULT 'pvp' NOT NULL,
	"tags" text[] DEFAULT '{}'::text[] NOT NULL,
	"listing" text DEFAULT 'unlisted' NOT NULL,
	"delist_reason" text,
	"build_version" text,
	"protocol_version" integer,
	"last_wipe_at" timestamp with time zone,
	"next_wipe_at" timestamp with time zone,
	"config_summary" jsonb DEFAULT '{}'::jsonb NOT NULL,
	"last_seen_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "ck_game_servers_kind" CHECK ("game_servers"."kind" IN ('official','community')),
	CONSTRAINT "ck_game_servers_listing" CHECK ("game_servers"."listing" IN ('unlisted','listed','verified','delisted')),
	CONSTRAINT "ck_game_servers_mode" CHECK ("game_servers"."mode" IN ('pvp','pve')),
	CONSTRAINT "ck_game_servers_max_players" CHECK ("game_servers"."max_players" BETWEEN 1 AND 200)
);
--> statement-breakpoint
CREATE TABLE "play_sessions" (
	"id" uuid PRIMARY KEY NOT NULL,
	"account_id" uuid NOT NULL,
	"server_id" uuid NOT NULL,
	"joined_at" timestamp with time zone NOT NULL,
	"left_at" timestamp with time zone,
	"leave_reason" text
);
--> statement-breakpoint
CREATE TABLE "server_tokens" (
	"id" uuid PRIMARY KEY NOT NULL,
	"server_id" uuid NOT NULL,
	"token_hash" "bytea" NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"revoked_at" timestamp with time zone,
	CONSTRAINT "uq_server_tokens_hash" UNIQUE("token_hash")
);
--> statement-breakpoint
CREATE TABLE "staff_roles" (
	"id" text PRIMARY KEY NOT NULL,
	"name" text NOT NULL,
	"permissions" text[] NOT NULL
);
--> statement-breakpoint
CREATE TABLE "staff_sessions" (
	"id" uuid PRIMARY KEY NOT NULL,
	"staff_id" uuid NOT NULL,
	"token_hash" "bytea" NOT NULL,
	"csrf_hash" "bytea" NOT NULL,
	"step_up_at" timestamp with time zone,
	"expires_at" timestamp with time zone NOT NULL,
	"last_seen_at" timestamp with time zone NOT NULL,
	"ip_hash" "bytea",
	"user_agent" text,
	"revoked_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "uq_staff_sessions_token" UNIQUE("token_hash")
);
--> statement-breakpoint
CREATE TABLE "staff_user_roles" (
	"staff_id" uuid NOT NULL,
	"role_id" text NOT NULL,
	CONSTRAINT "pk_staff_user_roles" PRIMARY KEY("staff_id","role_id")
);
--> statement-breakpoint
CREATE TABLE "staff_users" (
	"id" uuid PRIMARY KEY NOT NULL,
	"email" "citext" NOT NULL,
	"name" text NOT NULL,
	"password_hash" text,
	"totp_secret_enc" "bytea",
	"status" text DEFAULT 'invited' NOT NULL,
	"setup_token_hash" "bytea",
	"setup_expires_at" timestamp with time zone,
	"last_login_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "uq_staff_users_email" UNIQUE("email"),
	CONSTRAINT "uq_staff_users_setup_token" UNIQUE("setup_token_hash"),
	CONSTRAINT "ck_staff_users_status" CHECK ("staff_users"."status" IN ('invited','active','disabled'))
);
--> statement-breakpoint
ALTER TABLE "auth_identities" ADD CONSTRAINT "auth_identities_account_id_accounts_id_fk" FOREIGN KEY ("account_id") REFERENCES "public"."accounts"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "email_tokens" ADD CONSTRAINT "email_tokens_account_id_accounts_id_fk" FOREIGN KEY ("account_id") REFERENCES "public"."accounts"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "refresh_tokens" ADD CONSTRAINT "refresh_tokens_account_id_accounts_id_fk" FOREIGN KEY ("account_id") REFERENCES "public"."accounts"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "cloud_saves" ADD CONSTRAINT "cloud_saves_account_id_accounts_id_fk" FOREIGN KEY ("account_id") REFERENCES "public"."accounts"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "profiles" ADD CONSTRAINT "profiles_account_id_accounts_id_fk" FOREIGN KEY ("account_id") REFERENCES "public"."accounts"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "game_servers" ADD CONSTRAINT "game_servers_owner_account_id_accounts_id_fk" FOREIGN KEY ("owner_account_id") REFERENCES "public"."accounts"("id") ON DELETE set null ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "play_sessions" ADD CONSTRAINT "play_sessions_account_id_accounts_id_fk" FOREIGN KEY ("account_id") REFERENCES "public"."accounts"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "play_sessions" ADD CONSTRAINT "play_sessions_server_id_game_servers_id_fk" FOREIGN KEY ("server_id") REFERENCES "public"."game_servers"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "server_tokens" ADD CONSTRAINT "server_tokens_server_id_game_servers_id_fk" FOREIGN KEY ("server_id") REFERENCES "public"."game_servers"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "staff_sessions" ADD CONSTRAINT "staff_sessions_staff_id_staff_users_id_fk" FOREIGN KEY ("staff_id") REFERENCES "public"."staff_users"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "staff_user_roles" ADD CONSTRAINT "staff_user_roles_staff_id_staff_users_id_fk" FOREIGN KEY ("staff_id") REFERENCES "public"."staff_users"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "staff_user_roles" ADD CONSTRAINT "staff_user_roles_role_id_staff_roles_id_fk" FOREIGN KEY ("role_id") REFERENCES "public"."staff_roles"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "ix_audit_log_target" ON "audit_log" USING btree ("target_type","target_id","occurred_at");--> statement-breakpoint
CREATE INDEX "ix_audit_log_actor" ON "audit_log" USING btree ("actor_type","actor_id","occurred_at");--> statement-breakpoint
CREATE INDEX "ix_audit_log_action" ON "audit_log" USING btree ("action","occurred_at");--> statement-breakpoint
CREATE INDEX "ix_auth_identities_account" ON "auth_identities" USING btree ("account_id");--> statement-breakpoint
CREATE INDEX "ix_email_tokens_account" ON "email_tokens" USING btree ("account_id","purpose");--> statement-breakpoint
CREATE INDEX "ix_refresh_tokens_family" ON "refresh_tokens" USING btree ("family_id");--> statement-breakpoint
CREATE INDEX "ix_refresh_tokens_account_active" ON "refresh_tokens" USING btree ("account_id") WHERE "refresh_tokens"."revoked_at" IS NULL;--> statement-breakpoint
CREATE INDEX "ix_maintenance_windows_active" ON "maintenance_windows" USING btree ("starts_at") WHERE "maintenance_windows"."cancelled_at" IS NULL;--> statement-breakpoint
CREATE INDEX "ix_game_servers_browse" ON "game_servers" USING btree ("listing","region");--> statement-breakpoint
CREATE INDEX "ix_play_sessions_account" ON "play_sessions" USING btree ("account_id","joined_at");--> statement-breakpoint
CREATE INDEX "ix_play_sessions_server" ON "play_sessions" USING btree ("server_id","joined_at");--> statement-breakpoint
CREATE INDEX "ix_server_tokens_server" ON "server_tokens" USING btree ("server_id");--> statement-breakpoint
CREATE INDEX "ix_staff_sessions_staff_active" ON "staff_sessions" USING btree ("staff_id") WHERE "staff_sessions"."revoked_at" IS NULL;