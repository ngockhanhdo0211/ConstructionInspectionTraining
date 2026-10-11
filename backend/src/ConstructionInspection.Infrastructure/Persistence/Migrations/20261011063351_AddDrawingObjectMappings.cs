using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDrawingObjectMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DrawingObjectMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DrawingDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    ConstructionObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShapeType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Left = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Bottom = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Right = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Top = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    DeactivatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingObjectMappings", x => x.Id);
                    table.CheckConstraint("CK_DrawingObjectMappings_DeactivationAudit", "([Status] = 'Active' AND [DeactivatedAt] IS NULL AND [DeactivatedByUserId] IS NULL) OR ([Status] = 'Inactive' AND [DeactivatedAt] IS NOT NULL AND [DeactivatedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_DrawingObjectMappings_Geometry", "[Left] >= 0 AND [Left] < [Right] AND [Right] <= 1 AND [Bottom] >= 0 AND [Bottom] < [Top] AND [Top] <= 1");
                    table.CheckConstraint("CK_DrawingObjectMappings_PageNumber", "[PageNumber] > 0");
                    table.CheckConstraint("CK_DrawingObjectMappings_ShapeType", "[ShapeType] = 'Rectangle'");
                    table.CheckConstraint("CK_DrawingObjectMappings_Status", "[Status] IN ('Active','Inactive')");
                    table.ForeignKey(
                        name: "FK_DrawingObjectMappings_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DrawingObjectMappings_AspNetUsers_DeactivatedByUserId",
                        column: x => x.DeactivatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DrawingObjectMappings_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DrawingObjectMappings_ConstructionObjects_ConstructionObjectId",
                        column: x => x.ConstructionObjectId,
                        principalTable: "ConstructionObjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DrawingObjectMappings_DrawingDocuments_DrawingDocumentId",
                        column: x => x.DrawingDocumentId,
                        principalTable: "DrawingDocuments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrawingObjectMappings_ConstructionObjectId_Status",
                table: "DrawingObjectMappings",
                columns: new[] { "ConstructionObjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DrawingObjectMappings_CreatedByUserId",
                table: "DrawingObjectMappings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingObjectMappings_DeactivatedByUserId",
                table: "DrawingObjectMappings",
                column: "DeactivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingObjectMappings_DrawingDocumentId_PageNumber_Status",
                table: "DrawingObjectMappings",
                columns: new[] { "DrawingDocumentId", "PageNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DrawingObjectMappings_UpdatedByUserId",
                table: "DrawingObjectMappings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_DrawingObjectMappings_ActiveExact",
                table: "DrawingObjectMappings",
                columns: new[] { "DrawingDocumentId", "PageNumber", "ConstructionObjectId", "Left", "Bottom", "Right", "Top" },
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrawingObjectMappings");
        }
    }
}
