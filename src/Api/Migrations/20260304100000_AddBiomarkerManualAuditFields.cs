using System;
using Api.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260304100000_AddBiomarkerManualAuditFields")]
    public partial class AddBiomarkerManualAuditFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "BiomarkerReadings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "EnteredByUserId",
                table: "BiomarkerReadings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                table: "BiomarkerReadings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "BiomarkerReadings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_biomarker_document_code",
                table: "BiomarkerReadings",
                columns: new[] { "UserId", "DocumentId", "BiomarkerCode" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_biomarker_document_code",
                table: "BiomarkerReadings");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "BiomarkerReadings");

            migrationBuilder.DropColumn(
                name: "EnteredByUserId",
                table: "BiomarkerReadings");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "BiomarkerReadings");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "BiomarkerReadings");
        }
    }
}
