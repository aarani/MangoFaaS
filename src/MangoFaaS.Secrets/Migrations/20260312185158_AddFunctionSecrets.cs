using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangoFaaS.Secrets.Migrations
{
    /// <inheritdoc />
    public partial class AddFunctionSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FunctionSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FunctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FunctionSecrets_Secrets_SecretId",
                        column: x => x.SecretId,
                        principalTable: "Secrets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FunctionSecrets_FunctionId_SecretId",
                table: "FunctionSecrets",
                columns: new[] { "FunctionId", "SecretId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FunctionSecrets_SecretId",
                table: "FunctionSecrets",
                column: "SecretId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FunctionSecrets");
        }
    }
}
