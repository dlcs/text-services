using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextServices.Builder.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvocationCountToBuilderJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "invocation_count",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invocation_count",
                table: "jobs");
        }
    }
}
