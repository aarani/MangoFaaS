using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangoFaaS.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Host = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Data = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FunctionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FunctionVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Routes");
        }
    }
}
