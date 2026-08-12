using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LTR.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    PreferredStreamFormat = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastRefreshedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Capabilities_SupportsLive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_SupportsVod = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_SupportsSeries = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_SupportsXmltvEpg = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_SupportsShortEpg = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_SupportsMpegTs = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_SupportsHls = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_RequiresLivePathSegment = table.Column<bool>(type: "INTEGER", nullable: false),
                    Capabilities_ProbedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 21, nullable: false),
                    PlaylistUrl = table.Column<string>(type: "TEXT", nullable: true),
                    EpgUrl = table.Column<string>(type: "TEXT", nullable: true),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Password = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CategoryExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    EpgChannelId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Number = table.Column<int>(type: "INTEGER", nullable: true),
                    HasArchive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ArchiveDurationDays = table.Column<int>(type: "INTEGER", nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Channels_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Channels_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SourceId_ExternalId_Kind",
                table: "Categories",
                columns: new[] { "SourceId", "ExternalId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_CategoryId",
                table: "Channels",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_EpgChannelId",
                table: "Channels",
                column: "EpgChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_SourceId_ExternalId",
                table: "Channels",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Sources");
        }
    }
}
