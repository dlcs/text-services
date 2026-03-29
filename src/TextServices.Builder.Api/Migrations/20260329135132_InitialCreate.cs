using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextServices.Builder.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceUri = table.Column<string>(type: "text", nullable: true),
                    SourceDataJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Started = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Finished = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalPages = table.Column<int>(type: "integer", nullable: false),
                    PagesCompleted = table.Column<int>(type: "integer", nullable: false),
                    TotalWordCount = table.Column<int>(type: "integer", nullable: false),
                    TotalImageCount = table.Column<int>(type: "integer", nullable: false),
                    Errors = table.Column<string>(type: "text", nullable: true),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
