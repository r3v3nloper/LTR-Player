using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LTR.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastGuideImportedUtc",
                table: "Sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GuideChannelId",
                table: "Channels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GuideChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuideChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuideChannels_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EpgEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuideChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StopUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EpisodeReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpgEntries_GuideChannels_GuideChannelId",
                        column: x => x.GuideChannelId,
                        principalTable: "GuideChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_GuideChannelId",
                table: "Channels",
                column: "GuideChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgEntries_GuideChannelId_StartUtc",
                table: "EpgEntries",
                columns: new[] { "GuideChannelId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EpgEntries_StopUtc",
                table: "EpgEntries",
                column: "StopUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GuideChannels_SourceId_ExternalId",
                table: "GuideChannels",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_GuideChannels_GuideChannelId",
                table: "Channels",
                column: "GuideChannelId",
                principalTable: "GuideChannels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Channels_GuideChannels_GuideChannelId",
                table: "Channels");

            migrationBuilder.DropTable(
                name: "EpgEntries");

            migrationBuilder.DropTable(
                name: "GuideChannels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_GuideChannelId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "LastGuideImportedUtc",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "GuideChannelId",
                table: "Channels");
        }
    }
}
