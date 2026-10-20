using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    keycloak_sub = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_kind", "kind IN ('user','service')");
                });

            migrationBuilder.CreateTable(
                name: "buckets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quota_bytes = table.Column<long>(type: "bigint", nullable: true),
                    used_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buckets", x => x.id);
                    table.CheckConstraint("ck_buckets_quota_bytes", "quota_bytes IS NULL OR quota_bytes > 0");
                    table.ForeignKey(
                        name: "fk_buckets_principals_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bucket_members",
                columns: table => new
                {
                    bucket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bucket_members", x => new { x.bucket_id, x.user_id });
                    table.CheckConstraint("ck_bucket_members_role", "role IN ('admin','member','viewer')");
                    table.ForeignKey(
                        name: "fk_bucket_members_buckets_bucket_id",
                        column: x => x.bucket_id,
                        principalTable: "buckets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bucket_members_principals_added_by",
                        column: x => x.added_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bucket_members_principals_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visibility = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: true),
                    sha256 = table.Column<string>(type: "char(64)", nullable: true),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    original_file_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objects", x => x.id);
                    table.CheckConstraint("ck_objects_available_has_content", "status <> 'available' OR (size IS NOT NULL AND sha256 IS NOT NULL)");
                    table.CheckConstraint("ck_objects_status", "status IN ('pending','available','deleted')");
                    table.CheckConstraint("ck_objects_visibility", "visibility IN ('shared','private')");
                    table.ForeignKey(
                        name: "fk_objects_buckets_bucket_id",
                        column: x => x.bucket_id,
                        principalTable: "buckets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_objects_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "share_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_downloads = table.Column<int>(type: "integer", nullable: true),
                    downloads = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_links", x => x.id);
                    table.CheckConstraint("ck_share_links_kind", "kind IN ('public','addressed')");
                    table.CheckConstraint("ck_share_links_max_downloads", "max_downloads IS NULL OR max_downloads > 0");
                    table.ForeignKey(
                        name: "fk_share_links_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_share_links_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "share_link_recipients",
                columns: table => new
                {
                    share_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_link_recipients", x => new { x.share_link_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_share_link_recipients_share_links_share_link_id",
                        column: x => x.share_link_id,
                        principalTable: "share_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_share_link_recipients_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bucket_members_added_by",
                table: "bucket_members",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_bucket_members_user_id",
                table: "bucket_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_buckets_owner_name",
                table: "buckets",
                columns: new[] { "owner_id", "name" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_objects_cleanup",
                table: "objects",
                columns: new[] { "status", "updated_at" },
                filter: "status <> 'available'");

            migrationBuilder.CreateIndex(
                name: "ix_objects_listing",
                table: "objects",
                columns: new[] { "bucket_id", "key", "id" },
                filter: "status = 'available'");

            migrationBuilder.CreateIndex(
                name: "ix_objects_owner_id",
                table: "objects",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ux_objects_private_key",
                table: "objects",
                columns: new[] { "bucket_id", "owner_id", "key" },
                unique: true,
                filter: "visibility = 'private' AND status = 'available'");

            migrationBuilder.CreateIndex(
                name: "ux_objects_shared_key",
                table: "objects",
                columns: new[] { "bucket_id", "key" },
                unique: true,
                filter: "visibility = 'shared' AND status = 'available'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_share_link_recipients_user_id",
                table: "share_link_recipients",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_share_links_created_by",
                table: "share_links",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_share_links_object_id",
                table: "share_links",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_share_links_slug",
                table: "share_links",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                filter: "email_verified");

            migrationBuilder.CreateIndex(
                name: "ix_users_keycloak_sub",
                table: "users",
                column: "keycloak_sub",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bucket_members");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "share_link_recipients");

            migrationBuilder.DropTable(
                name: "share_links");

            migrationBuilder.DropTable(
                name: "objects");

            migrationBuilder.DropTable(
                name: "buckets");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
