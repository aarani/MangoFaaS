using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangoFaaS.Functions.Migrations
{
    /// <inheritdoc />
    public partial class FunctionOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Functions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Functions");
        }
    }
}
