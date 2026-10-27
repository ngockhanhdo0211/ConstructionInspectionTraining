using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_Dashboard_Assigned_Entity_Status_UpdatedAt_Id",
                table: "BusinessProcesses",
                columns: new[] { "AssignedToUserId", "EntityType", "Status", "UpdatedAt", "Id" },
                filter: "[AssignedToUserId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "InspectionId", "ChecklistId", "RfiId", "DueAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_Dashboard_Creator_Entity_Status_UpdatedAt_Id",
                table: "BusinessProcesses",
                columns: new[] { "CreatedByUserId", "EntityType", "Status", "UpdatedAt", "Id" },
                filter: "[CreatedByUserId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "InspectionId", "ChecklistId", "RfiId", "DueAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_Dashboard_RfiOpen_DueAt_UpdatedAt_Id",
                table: "BusinessProcesses",
                columns: new[] { "DueAt", "UpdatedAt", "Id" },
                filter: "[EntityType] = 'Rfi' AND [Status] = 'Open' AND [DueAt] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "RfiId", "AssignedToUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessProcesses_Dashboard_Assigned_Entity_Status_UpdatedAt_Id",
                table: "BusinessProcesses");

            migrationBuilder.DropIndex(
                name: "IX_BusinessProcesses_Dashboard_Creator_Entity_Status_UpdatedAt_Id",
                table: "BusinessProcesses");

            migrationBuilder.DropIndex(
                name: "IX_BusinessProcesses_Dashboard_RfiOpen_DueAt_UpdatedAt_Id",
                table: "BusinessProcesses");
        }
    }
}
