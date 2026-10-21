using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChecklists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories");

            migrationBuilder.DropIndex(
                name: "UX_BusinessProcesses_InspectionId",
                table: "BusinessProcesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_EntityType",
                table: "BusinessProcesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses");

            migrationBuilder.CreateSequence(
                name: "ChecklistNumberSequence");

            migrationBuilder.AddColumn<Guid>(
                name: "ChecklistSubmissionId",
                table: "ProcessHistories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "InspectionId",
                table: "BusinessProcesses",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ChecklistId",
                table: "BusinessProcesses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChecklistInspections",
                columns: table => new
                {
                    ChecklistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistInspections", x => new { x.ChecklistId, x.InspectionId });
                    table.ForeignKey(
                        name: "FK_ChecklistInspections_AspNetUsers_LinkedByUserId",
                        column: x => x.LinkedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChecklistInspections_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Criterion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Result = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistItems", x => x.Id);
                    table.CheckConstraint("CK_ChecklistItems_Result", "[Result] IN ('NotChecked','Pass','Fail','NotApplicable')");
                    table.CheckConstraint("CK_ChecklistItems_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_ChecklistItems_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChecklistItems_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Checklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConstructionObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NextSubmissionNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Checklists", x => x.Id);
                    table.CheckConstraint("CK_Checklists_NextSubmissionNumber", "[NextSubmissionNumber] > 0");
                    table.ForeignKey(
                        name: "FK_Checklists_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Checklists_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Checklists_ConstructionObjects_ConstructionObjectId",
                        column: x => x.ConstructionObjectId,
                        principalTable: "ConstructionObjects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChecklistSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionNumber = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistSubmissions", x => x.Id);
                    table.CheckConstraint("CK_ChecklistSubmissions_Number", "[SubmissionNumber] > 0");
                    table.ForeignKey(
                        name: "FK_ChecklistSubmissions_AspNetUsers_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChecklistSubmissions_Checklists_ChecklistId",
                        column: x => x.ChecklistId,
                        principalTable: "Checklists",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChecklistSubmissionInspections",
                columns: table => new
                {
                    ChecklistSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistSubmissionInspections", x => new { x.ChecklistSubmissionId, x.InspectionId });
                    table.ForeignKey(
                        name: "FK_ChecklistSubmissionInspections_ChecklistSubmissions_ChecklistSubmissionId",
                        column: x => x.ChecklistSubmissionId,
                        principalTable: "ChecklistSubmissions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChecklistSubmissionInspections_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChecklistSubmissionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChecklistSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceChecklistItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Criterion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Result = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistSubmissionItems", x => x.Id);
                    table.CheckConstraint("CK_ChecklistSubmissionItems_Result", "[Result] IN ('Pass','Fail','NotApplicable')");
                    table.CheckConstraint("CK_ChecklistSubmissionItems_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_ChecklistSubmissionItems_ChecklistItems_SourceChecklistItemId",
                        column: x => x.SourceChecklistItemId,
                        principalTable: "ChecklistItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChecklistSubmissionItems_ChecklistSubmissions_ChecklistSubmissionId",
                        column: x => x.ChecklistSubmissionId,
                        principalTable: "ChecklistSubmissions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_ChecklistSubmissionId",
                table: "ProcessHistories",
                column: "ChecklistSubmissionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories",
                sql: "[Action] IN ('Create','Start','UpdateInspectionLinks','Submit','StartReview','Approve','Reject','RequestRecheck','Resume','ReassignReviewer','CreateRevision')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_SubmissionOwner",
                table: "ProcessHistories",
                sql: "[SubmissionId] IS NULL OR [ChecklistSubmissionId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories",
                sql: "[ToStatus] IN ('Draft','InProgress','Submitted','UnderReview','Approved','Recheck','Rejected')");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcesses_ChecklistId",
                table: "BusinessProcesses",
                column: "ChecklistId",
                unique: true,
                filter: "[ChecklistId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcesses_InspectionId",
                table: "BusinessProcesses",
                column: "InspectionId",
                unique: true,
                filter: "[InspectionId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_EntityType",
                table: "BusinessProcesses",
                sql: "[EntityType] IN ('Inspection','Checklist')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_Owner",
                table: "BusinessProcesses",
                sql: "([EntityType] = 'Inspection' AND [InspectionId] IS NOT NULL AND [ChecklistId] IS NULL) OR ([EntityType] = 'Checklist' AND [ChecklistId] IS NOT NULL AND [InspectionId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses",
                sql: "[Status] IN ('Draft','InProgress','Submitted','UnderReview','Approved','Recheck','Rejected') AND NOT ([EntityType] = 'Inspection' AND [Status] = 'InProgress')");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistInspections_InspectionId",
                table: "ChecklistInspections",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistInspections_LinkedByUserId",
                table: "ChecklistInspections",
                column: "LinkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItems_CreatedByUserId",
                table: "ChecklistItems",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItems_UpdatedByUserId",
                table: "ChecklistItems",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ChecklistItems_ChecklistId_Sequence",
                table: "ChecklistItems",
                columns: new[] { "ChecklistId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Checklists_ConstructionObjectId_CreatedAt",
                table: "Checklists",
                columns: new[] { "ConstructionObjectId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Checklists_CreatedByUserId_CreatedAt",
                table: "Checklists",
                columns: new[] { "CreatedByUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Checklists_CurrentSubmissionId",
                table: "Checklists",
                column: "CurrentSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Checklists_ReviewerUserId_CreatedAt",
                table: "Checklists",
                columns: new[] { "ReviewerUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Checklists_Number",
                table: "Checklists",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistSubmissionInspections_InspectionId",
                table: "ChecklistSubmissionInspections",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistSubmissionItems_ChecklistSubmissionId_Sequence",
                table: "ChecklistSubmissionItems",
                columns: new[] { "ChecklistSubmissionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistSubmissionItems_ChecklistSubmissionId_SourceChecklistItemId",
                table: "ChecklistSubmissionItems",
                columns: new[] { "ChecklistSubmissionId", "SourceChecklistItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistSubmissionItems_SourceChecklistItemId",
                table: "ChecklistSubmissionItems",
                column: "SourceChecklistItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistSubmissions_SubmittedByUserId",
                table: "ChecklistSubmissions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ChecklistSubmissions_ChecklistId_SubmissionNumber",
                table: "ChecklistSubmissions",
                columns: new[] { "ChecklistId", "SubmissionNumber" },
                unique: true,
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessProcesses_Checklists_ChecklistId",
                table: "BusinessProcesses",
                column: "ChecklistId",
                principalTable: "Checklists",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessHistories_ChecklistSubmissions_ChecklistSubmissionId",
                table: "ProcessHistories",
                column: "ChecklistSubmissionId",
                principalTable: "ChecklistSubmissions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChecklistInspections_Checklists_ChecklistId",
                table: "ChecklistInspections",
                column: "ChecklistId",
                principalTable: "Checklists",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChecklistItems_Checklists_ChecklistId",
                table: "ChecklistItems",
                column: "ChecklistId",
                principalTable: "Checklists",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Checklists_ChecklistSubmissions_CurrentSubmissionId",
                table: "Checklists",
                column: "CurrentSubmissionId",
                principalTable: "ChecklistSubmissions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [Checklists]) " +
                "THROW 51000, 'Cannot roll back AddChecklists while Checklist data exists.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessProcesses_Checklists_ChecklistId",
                table: "BusinessProcesses");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcessHistories_ChecklistSubmissions_ChecklistSubmissionId",
                table: "ProcessHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_ChecklistSubmissions_Checklists_ChecklistId",
                table: "ChecklistSubmissions");

            migrationBuilder.DropTable(
                name: "ChecklistInspections");

            migrationBuilder.DropTable(
                name: "ChecklistSubmissionInspections");

            migrationBuilder.DropTable(
                name: "ChecklistSubmissionItems");

            migrationBuilder.DropTable(
                name: "ChecklistItems");

            migrationBuilder.DropTable(
                name: "Checklists");

            migrationBuilder.DropTable(
                name: "ChecklistSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_ProcessHistories_ChecklistSubmissionId",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_SubmissionOwner",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories");

            migrationBuilder.DropIndex(
                name: "UX_BusinessProcesses_ChecklistId",
                table: "BusinessProcesses");

            migrationBuilder.DropIndex(
                name: "UX_BusinessProcesses_InspectionId",
                table: "BusinessProcesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_EntityType",
                table: "BusinessProcesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_Owner",
                table: "BusinessProcesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses");

            migrationBuilder.DropColumn(
                name: "ChecklistSubmissionId",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "ChecklistId",
                table: "BusinessProcesses");

            migrationBuilder.DropSequence(
                name: "ChecklistNumberSequence");

            migrationBuilder.AlterColumn<Guid>(
                name: "InspectionId",
                table: "BusinessProcesses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories",
                sql: "[Action] IN ('Create','Submit','StartReview','Approve','Reject','RequestRecheck','ReassignReviewer','CreateRevision')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories",
                sql: "[ToStatus] IN ('Draft','Submitted','UnderReview','Approved','Recheck','Rejected')");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcesses_InspectionId",
                table: "BusinessProcesses",
                column: "InspectionId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_EntityType",
                table: "BusinessProcesses",
                sql: "[EntityType] = 'Inspection'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses",
                sql: "[Status] IN ('Draft','Submitted','UnderReview','Approved','Recheck','Rejected')");
        }
    }
}
