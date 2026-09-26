using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GeoCommand.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    callsign = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    last_position = table.Column<Point>(type: "geometry(Point,4326)", nullable: true),
                    last_speed_mps = table.Column<double>(type: "double precision", nullable: false),
                    last_heading_degrees = table.Column<double>(type: "double precision", nullable: false),
                    last_update_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active_mission_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    area = table.Column<Polygon>(type: "geometry(Polygon,4326)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message = table.Column<string>(type: "character varying(700)", maxLength: 700, nullable: false),
                    location = table.Column<Point>(type: "geometry(Point,4326)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_events_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "missions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_missions", x => x.id);
                    table.ForeignKey(
                        name: "fk_missions_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "position_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    speed_mps = table.Column<double>(type: "double precision", nullable: false),
                    heading_degrees = table.Column<double>(type: "double precision", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_records_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zone_memberships",
                columns: table => new
                {
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zone_memberships", x => new { x.vehicle_id, x.zone_id });
                    table.ForeignKey(
                        name: "fk_zone_memberships_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_zone_memberships_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "vehicles",
                columns: new[] { "id", "active_mission_id", "callsign", "last_heading_degrees", "last_position", "last_speed_mps", "last_update_utc", "status" },
                values: new object[,]
                {
                    { new Guid("0f1c7a52-6a3e-4c1e-9d11-0000000000a1"), null, "ALFA-1", 0.0, null, 0.0, null, "Unknown" },
                    { new Guid("0f1c7a52-6a3e-4c1e-9d11-0000000000b2"), null, "BRAVO-2", 0.0, null, 0.0, null, "Unknown" },
                    { new Guid("0f1c7a52-6a3e-4c1e-9d11-0000000000c3"), null, "CHARLIE-3", 0.0, null, 0.0, null, "Unknown" },
                    { new Guid("0f1c7a52-6a3e-4c1e-9d11-0000000000d4"), null, "DELTA-4", 0.0, null, 0.0, null, "Unknown" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_occurred_at_utc",
                table: "events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_events_vehicle_id_occurred_at_utc",
                table: "events",
                columns: new[] { "vehicle_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_events_zone_transition_unique",
                table: "events",
                columns: new[] { "vehicle_id", "zone_id", "type", "occurred_at_utc" },
                unique: true,
                filter: "zone_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_missions_one_active_per_vehicle",
                table: "missions",
                column: "vehicle_id",
                unique: true,
                filter: "status IN ('Assigned', 'InProgress')");

            migrationBuilder.CreateIndex(
                name: "ix_missions_vehicle_id_assigned_at_utc",
                table: "missions",
                columns: new[] { "vehicle_id", "assigned_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_position_records_vehicle_id_recorded_at_utc",
                table: "position_records",
                columns: new[] { "vehicle_id", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_callsign",
                table: "vehicles",
                column: "callsign",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_last_position",
                table: "vehicles",
                column: "last_position")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_zone_memberships_zone_id",
                table: "zone_memberships",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_zones_area",
                table: "zones",
                column: "area")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "missions");

            migrationBuilder.DropTable(
                name: "position_records");

            migrationBuilder.DropTable(
                name: "zone_memberships");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropTable(
                name: "zones");
        }
    }
}
