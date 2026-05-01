using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextServices.Builder.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleAndCustomTypesColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomTypesJson",
                table: "Jobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CustomTypesJson",
                table: "Jobs");
        }
    }
}
