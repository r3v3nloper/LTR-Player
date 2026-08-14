using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LTR.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVodCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CategoryExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ContainerExtension = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Rating = table.Column<double>(type: "REAL", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Plot = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Genre = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Cast = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Director = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HasDetail = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResumePositionSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    LastWatchedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsWatched = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movies_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Movies_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CategoryExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Plot = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Genre = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Cast = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Director = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Rating = table.Column<double>(type: "REAL", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DetailFetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DetailModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Series_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CoverUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Plot = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    ContainerExtension = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Plot = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StillUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResumePositionSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    LastWatchedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsWatched = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_LastWatchedUtc",
                table: "Episodes",
                column: "LastWatchedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeasonId_ExternalId",
                table: "Episodes",
                columns: new[] { "SeasonId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movies_CategoryId",
                table: "Movies",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_SourceId_ExternalId",
                table: "Movies",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movies_SourceId_LastWatchedUtc",
                table: "Movies",
                columns: new[] { "SourceId", "LastWatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_SeriesId_Number",
                table: "Seasons",
                columns: new[] { "SeriesId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_CategoryId",
                table: "Series",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_SourceId_ExternalId",
                table: "Series",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Series");
        }
    }
}
