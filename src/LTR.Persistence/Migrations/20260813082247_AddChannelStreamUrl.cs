using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LTR.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelStreamUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StreamUrl",
                table: "Channels",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StreamUrl",
                table: "Channels");
        }
    }
}
