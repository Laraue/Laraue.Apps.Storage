using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.Storage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "owners",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    usage_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owners", x => x.id);
                    table.CheckConstraint("ck_owners_kind_id", "(kind = 1 and organization_id is not null and user_id is null) or (kind = 2 and user_id is not null and organization_id is null)");
                    table.ForeignKey(
                        name: "fk_owners_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_stored_files_owners_owner_id",
                        column: x => x.owner_id,
                        principalTable: "owners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stored_files_stored_files_source_file_id",
                        column: x => x.source_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "file_references",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_references", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_references_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "services",
                columns: new[] { "id", "code", "name" },
                values: new object[] { 1, "laraue_boards", "Laraue Boards" });

            migrationBuilder.CreateIndex(
                name: "ix_file_references_file_id",
                table: "file_references",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_references_state_created_at",
                table: "file_references",
                columns: new[] { "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_owners_service_id",
                table: "owners",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_owners_service_id_organization_id",
                table: "owners",
                columns: new[] { "service_id", "organization_id" },
                unique: true,
                filter: "organization_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_owners_service_id_user_id",
                table: "owners",
                columns: new[] { "service_id", "user_id" },
                unique: true,
                filter: "user_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_owner_id_content_hash",
                table: "stored_files",
                columns: new[] { "owner_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_source_file_id",
                table: "stored_files",
                column: "source_file_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_references");

            migrationBuilder.DropTable(
                name: "stored_files");

            migrationBuilder.DropTable(
                name: "owners");

            migrationBuilder.DropTable(
                name: "services");
        }
    }
}
