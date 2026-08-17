using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LTR.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMovieDetailAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailAttemptedUtc",
                table: "Movies",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailAttemptedUtc",
                table: "Movies");
        }
    }
}
