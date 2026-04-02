using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionTimeout = table.Column<int>(type: "int", nullable: false),
                    PasswordExpiry = table.Column<int>(type: "int", nullable: false),
                    MfaRequired = table.Column<bool>(type: "bit", nullable: false),
                    AlertCritical = table.Column<bool>(type: "bit", nullable: false),
                    AlertLogins = table.Column<bool>(type: "bit", nullable: false),
                    AlertExports = table.Column<bool>(type: "bit", nullable: false),
                    StorageUsage = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");
        }
    }
}
