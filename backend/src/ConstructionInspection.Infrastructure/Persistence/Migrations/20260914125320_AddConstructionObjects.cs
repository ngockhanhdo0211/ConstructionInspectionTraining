using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConstructionObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConstructionObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ObjectType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConstructionObjects", x => x.Id);
                    table.CheckConstraint("CK_ConstructionObjects_ObjectType", "[ObjectType] IN ('Project','Building','Floor','Room','WorkItem')");
                    table.ForeignKey(
                        name: "FK_ConstructionObjects_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConstructionObjects_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConstructionObjects_ConstructionObjects_ParentId",
                        column: x => x.ParentId,
                        principalTable: "ConstructionObjects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionObjects_CreatedByUserId",
                table: "ConstructionObjects",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionObjects_IsActive_ObjectType",
                table: "ConstructionObjects",
                columns: new[] { "IsActive", "ObjectType" });

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionObjects_ParentId",
                table: "ConstructionObjects",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionObjects_UpdatedByUserId",
                table: "ConstructionObjects",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ConstructionObjects_Code",
                table: "ConstructionObjects",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConstructionObjects");
        }
    }
}
