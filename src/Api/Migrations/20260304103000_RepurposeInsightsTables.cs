using System;
using Api.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260304103000_RepurposeInsightsTables")]
    public partial class RepurposeInsightsTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "ScoreSnapshots",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "InputsHash",
                table: "ScoreSnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingJobId",
                table: "ScoreSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RiskBand",
                table: "ScoreSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.RenameColumn(
                name: "RulesetVersion",
                table: "ScoreSnapshots",
                newName: "ModelVersion");

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "Recommendations",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Recommendations",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<Guid>(
                name: "ScoreSnapshotId",
                table: "Recommendations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Recommendations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_score_document_latest",
                table: "ScoreSnapshots",
                columns: new[] { "UserId", "DocumentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_document_latest",
                table: "Recommendations",
                columns: new[] { "UserId", "DocumentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_snapshot",
                table: "Recommendations",
                column: "ScoreSnapshotId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_score_document_latest",
                table: "ScoreSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_recommendation_document_latest",
                table: "Recommendations");

            migrationBuilder.DropIndex(
                name: "IX_recommendation_snapshot",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "InputsHash",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "ProcessingJobId",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "RiskBand",
                table: "ScoreSnapshots");

            migrationBuilder.RenameColumn(
                name: "ModelVersion",
                table: "ScoreSnapshots",
                newName: "RulesetVersion");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ScoreSnapshotId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Recommendations");
        }
    }
}
