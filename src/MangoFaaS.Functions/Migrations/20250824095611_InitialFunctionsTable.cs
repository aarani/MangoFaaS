using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangoFaaS.Functions.Migrations
{
    /// <inheritdoc />
    public partial class InitialFunctionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Functions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Runtime = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Functions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FunctionVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Entrypoint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FunctionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FunctionVersions_Functions_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "Functions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FunctionVersions_FunctionId",
                table: "FunctionVersions",
                column: "FunctionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FunctionVersions");

            migrationBuilder.DropTable(
                name: "Functions");
        }
    }
}
