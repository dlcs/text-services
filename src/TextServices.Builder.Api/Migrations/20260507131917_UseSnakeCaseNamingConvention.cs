using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextServices.Builder.Api.Migrations
{
    /// <inheritdoc />
    public partial class UseSnakeCaseNamingConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Jobs",
                table: "Jobs");

            migrationBuilder.RenameTable(
                name: "Jobs",
                newName: "jobs");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "jobs",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "jobs",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Started",
                table: "jobs",
                newName: "started");

            migrationBuilder.RenameColumn(
                name: "Services",
                table: "jobs",
                newName: "services");

            migrationBuilder.RenameColumn(
                name: "Finished",
                table: "jobs",
                newName: "finished");

            migrationBuilder.RenameColumn(
                name: "Errors",
                table: "jobs",
                newName: "errors");

            migrationBuilder.RenameColumn(
                name: "Created",
                table: "jobs",
                newName: "created");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "jobs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TotalWordCount",
                table: "jobs",
                newName: "total_word_count");

            migrationBuilder.RenameColumn(
                name: "TotalPages",
                table: "jobs",
                newName: "total_pages");

            migrationBuilder.RenameColumn(
                name: "TotalImageCount",
                table: "jobs",
                newName: "total_image_count");

            migrationBuilder.RenameColumn(
                name: "SourceUri",
                table: "jobs",
                newName: "source_uri");

            migrationBuilder.RenameColumn(
                name: "SourceDataJson",
                table: "jobs",
                newName: "source_data_json");

            migrationBuilder.RenameColumn(
                name: "PagesCompleted",
                table: "jobs",
                newName: "pages_completed");

            migrationBuilder.RenameColumn(
                name: "HangfireJobId",
                table: "jobs",
                newName: "hangfire_job_id");

            migrationBuilder.RenameColumn(
                name: "CustomTypesJson",
                table: "jobs",
                newName: "custom_types_json");

            migrationBuilder.AddPrimaryKey(
                name: "pk_jobs",
                table: "jobs",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_jobs",
                table: "jobs");

            migrationBuilder.RenameTable(
                name: "jobs",
                newName: "Jobs");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "Jobs",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "Jobs",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "started",
                table: "Jobs",
                newName: "Started");

            migrationBuilder.RenameColumn(
                name: "services",
                table: "Jobs",
                newName: "Services");

            migrationBuilder.RenameColumn(
                name: "finished",
                table: "Jobs",
                newName: "Finished");

            migrationBuilder.RenameColumn(
                name: "errors",
                table: "Jobs",
                newName: "Errors");

            migrationBuilder.RenameColumn(
                name: "created",
                table: "Jobs",
                newName: "Created");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Jobs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "total_word_count",
                table: "Jobs",
                newName: "TotalWordCount");

            migrationBuilder.RenameColumn(
                name: "total_pages",
                table: "Jobs",
                newName: "TotalPages");

            migrationBuilder.RenameColumn(
                name: "total_image_count",
                table: "Jobs",
                newName: "TotalImageCount");

            migrationBuilder.RenameColumn(
                name: "source_uri",
                table: "Jobs",
                newName: "SourceUri");

            migrationBuilder.RenameColumn(
                name: "source_data_json",
                table: "Jobs",
                newName: "SourceDataJson");

            migrationBuilder.RenameColumn(
                name: "pages_completed",
                table: "Jobs",
                newName: "PagesCompleted");

            migrationBuilder.RenameColumn(
                name: "hangfire_job_id",
                table: "Jobs",
                newName: "HangfireJobId");

            migrationBuilder.RenameColumn(
                name: "custom_types_json",
                table: "Jobs",
                newName: "CustomTypesJson");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Jobs",
                table: "Jobs",
                column: "Id");
        }
    }
}
