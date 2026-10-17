using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "InspectionNumberSequence");

            migrationBuilder.CreateTable(
                name: "BusinessProcesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProcesses", x => x.Id);
                    table.CheckConstraint("CK_BusinessProcesses_EntityType", "[EntityType] = 'Inspection'");
                    table.CheckConstraint("CK_BusinessProcesses_Status", "[Status] = 'Draft'");
                    table.ForeignKey(
                        name: "FK_BusinessProcesses_AspNetUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BusinessProcesses_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Inspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConstructionObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inspections_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Inspections_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Inspections_ConstructionObjects_ConstructionObjectId",
                        column: x => x.ConstructionObjectId,
                        principalTable: "ConstructionObjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Inspections_FormTemplateVersions_FormTemplateVersionId",
                        column: x => x.FormTemplateVersionId,
                        principalTable: "FormTemplateVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InspectionSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionSubmissions", x => x.Id);
                    table.CheckConstraint("CK_InspectionSubmissions_DataJson", "ISJSON([DataJson]) = 1");
                    table.CheckConstraint("CK_InspectionSubmissions_RevisionNumber", "[RevisionNumber] > 0");
                    table.ForeignKey(
                        name: "FK_InspectionSubmissions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InspectionSubmissions_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProcessHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessProcessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessHistories", x => x.Id);
                    table.CheckConstraint("CK_ProcessHistories_Action", "[Action] = 'Create'");
                    table.CheckConstraint("CK_ProcessHistories_ToStatus", "[ToStatus] = 'Draft'");
                    table.ForeignKey(
                        name: "FK_ProcessHistories_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProcessHistories_BusinessProcesses_BusinessProcessId",
                        column: x => x.BusinessProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProcessHistories_InspectionSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "InspectionSubmissions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_AssignedToUserId",
                table: "BusinessProcesses",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_CreatedByUserId",
                table: "BusinessProcesses",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_EntityType_Status_UpdatedAt",
                table: "BusinessProcesses",
                columns: new[] { "EntityType", "Status", "UpdatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcesses_InspectionId",
                table: "BusinessProcesses",
                column: "InspectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_ConstructionObjectId_CreatedAt",
                table: "Inspections",
                columns: new[] { "ConstructionObjectId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_CreatedByUserId_CreatedAt",
                table: "Inspections",
                columns: new[] { "CreatedByUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_CurrentSubmissionId",
                table: "Inspections",
                column: "CurrentSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_FormTemplateVersionId",
                table: "Inspections",
                column: "FormTemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_ReviewerUserId_CreatedAt",
                table: "Inspections",
                columns: new[] { "ReviewerUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Inspections_Number",
                table: "Inspections",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSubmissions_CreatedByUserId",
                table: "InspectionSubmissions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_InspectionSubmissions_InspectionId_RevisionNumber",
                table: "InspectionSubmissions",
                columns: new[] { "InspectionId", "RevisionNumber" },
                unique: true,
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_BusinessProcessId_PerformedAt_Id",
                table: "ProcessHistories",
                columns: new[] { "BusinessProcessId", "PerformedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_PerformedByUserId",
                table: "ProcessHistories",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_SubmissionId",
                table: "ProcessHistories",
                column: "SubmissionId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessProcesses_Inspections_InspectionId",
                table: "BusinessProcesses",
                column: "InspectionId",
                principalTable: "Inspections",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Inspections_InspectionSubmissions_CurrentSubmissionId",
                table: "Inspections",
                column: "CurrentSubmissionId",
                principalTable: "InspectionSubmissions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionSubmissions_Inspections_InspectionId",
                table: "InspectionSubmissions");

            migrationBuilder.DropTable(
                name: "ProcessHistories");

            migrationBuilder.DropTable(
                name: "BusinessProcesses");

            migrationBuilder.DropTable(
                name: "Inspections");

            migrationBuilder.DropTable(
                name: "InspectionSubmissions");

            migrationBuilder.DropSequence(
                name: "InspectionNumberSequence");
        }
    }
}
