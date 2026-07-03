using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextServices.Builder.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelationIdToBuilderJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "jobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "jobs");
        }
    }
}
