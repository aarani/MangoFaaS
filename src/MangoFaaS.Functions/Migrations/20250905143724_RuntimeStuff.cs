using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangoFaaS.Functions.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeStuff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompressionMethod",
                table: "FunctionVersions",
                type: "text",
                nullable: false,
                defaultValue: "Deflate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompressionMethod",
                table: "FunctionVersions");
        }
    }
}
