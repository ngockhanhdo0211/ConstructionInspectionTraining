using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    NextVersionNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    DeactivatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplates", x => x.Id);
                    table.CheckConstraint("CK_FormTemplates_DeactivatedAudit", "([Status] = 'Active' AND [DeactivatedByUserId] IS NULL AND [DeactivatedAt] IS NULL) OR ([Status] = 'Inactive' AND [DeactivatedByUserId] IS NOT NULL AND [DeactivatedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_FormTemplates_NextVersionNumber", "[NextVersionNumber] > 1");
                    table.CheckConstraint("CK_FormTemplates_Status", "[Status] IN ('Active','Inactive')");
                    table.CheckConstraint("CK_FormTemplates_UpdatedAudit", "([UpdatedByUserId] IS NULL AND [UpdatedAt] IS NULL) OR ([UpdatedByUserId] IS NOT NULL AND [UpdatedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FormTemplates_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FormTemplates_AspNetUsers_DeactivatedByUserId",
                        column: x => x.DeactivatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FormTemplates_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FormTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplateVersions", x => x.Id);
                    table.CheckConstraint("CK_FormTemplateVersions_PublishedAudit", "([Status] = 'Draft' AND [PublishedByUserId] IS NULL AND [PublishedAt] IS NULL) OR ([Status] = 'Published' AND [PublishedByUserId] IS NOT NULL AND [PublishedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_FormTemplateVersions_SchemaJson", "ISJSON([SchemaJson]) = 1");
                    table.CheckConstraint("CK_FormTemplateVersions_Status", "[Status] IN ('Draft','Published')");
                    table.CheckConstraint("CK_FormTemplateVersions_UpdatedAudit", "([UpdatedByUserId] IS NULL AND [UpdatedAt] IS NULL) OR ([UpdatedByUserId] IS NOT NULL AND [UpdatedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_FormTemplateVersions_VersionNumber", "[VersionNumber] > 0");
                    table.ForeignKey(
                        name: "FK_FormTemplateVersions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FormTemplateVersions_AspNetUsers_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FormTemplateVersions_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FormTemplateVersions_FormTemplates_FormTemplateId",
                        column: x => x.FormTemplateId,
                        principalTable: "FormTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_CreatedByUserId",
                table: "FormTemplates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_DeactivatedByUserId",
                table: "FormTemplates",
                column: "DeactivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_Status_Name",
                table: "FormTemplates",
                columns: new[] { "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_UpdatedByUserId",
                table: "FormTemplates",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_FormTemplates_Code",
                table: "FormTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersions_CreatedByUserId",
                table: "FormTemplateVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersions_PublishedByUserId",
                table: "FormTemplateVersions",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersions_TemplateId_Status_VersionNumber",
                table: "FormTemplateVersions",
                columns: new[] { "FormTemplateId", "Status", "VersionNumber" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersions_UpdatedByUserId",
                table: "FormTemplateVersions",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_FormTemplateVersions_OneDraft",
                table: "FormTemplateVersions",
                column: "FormTemplateId",
                unique: true,
                filter: "[Status] = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "UX_FormTemplateVersions_TemplateId_VersionNumber",
                table: "FormTemplateVersions",
                columns: new[] { "FormTemplateId", "VersionNumber" },
                unique: true,
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormTemplateVersions");

            migrationBuilder.DropTable(
                name: "FormTemplates");
        }
    }
}
