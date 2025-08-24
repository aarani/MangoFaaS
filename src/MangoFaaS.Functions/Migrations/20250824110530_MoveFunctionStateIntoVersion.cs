using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangoFaaS.Functions.Migrations
{
    /// <inheritdoc />
    public partial class MoveFunctionStateIntoVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "State",
                table: "Functions");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "FunctionVersions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "State",
                table: "FunctionVersions");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Functions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
