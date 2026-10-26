using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRfis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_SubmissionOwner",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_EntityType",
                table: "BusinessProcesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_Owner",
                table: "BusinessProcesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses");

            migrationBuilder.CreateSequence(
                name: "RfiNumberSequence");

            migrationBuilder.AddColumn<Guid>(
                name: "RfiEntryId",
                table: "ProcessHistories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RfiId",
                table: "BusinessProcesses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Rfis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConstructionObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NextEntryNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rfis", x => x.Id);
                    table.CheckConstraint("CK_Rfis_NextEntryNumber", "[NextEntryNumber] > 0");
                    table.ForeignKey(
                        name: "FK_Rfis_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Rfis_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Rfis_ConstructionObjects_ConstructionObjectId",
                        column: x => x.ConstructionObjectId,
                        principalTable: "ConstructionObjects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RfiEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfiId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryNumber = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfiEntries", x => x.Id);
                    table.CheckConstraint("CK_RfiEntries_EntryNumber", "[EntryNumber] > 0");
                    table.CheckConstraint("CK_RfiEntries_EntryType", "[EntryType] IN ('Response','ClarificationRequest')");
                    table.ForeignKey(
                        name: "FK_RfiEntries_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RfiEntries_Rfis_RfiId",
                        column: x => x.RfiId,
                        principalTable: "Rfis",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_RfiEntryId",
                table: "ProcessHistories",
                column: "RfiEntryId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories",
                sql: "[Action] IN ('Create','Start','UpdateInspectionLinks','Submit','StartReview','Approve','Reject','RequestRecheck','Resume','ReassignReviewer','CreateRevision','Respond','RequestClarification','Reassign','Close','Cancel')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_SubmissionOwner",
                table: "ProcessHistories",
                sql: "([SubmissionId] IS NULL OR [ChecklistSubmissionId] IS NULL) AND ([RfiEntryId] IS NULL OR ([SubmissionId] IS NULL AND [ChecklistSubmissionId] IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories",
                sql: "[ToStatus] IN ('Draft','InProgress','Submitted','UnderReview','Approved','Recheck','Rejected','Open','Responded','Closed','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcesses_RfiId",
                table: "BusinessProcesses",
                column: "RfiId",
                unique: true,
                filter: "[RfiId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_EntityType",
                table: "BusinessProcesses",
                sql: "[EntityType] IN ('Inspection','Checklist','Rfi')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_Owner",
                table: "BusinessProcesses",
                sql: "([EntityType] = 'Inspection' AND [InspectionId] IS NOT NULL AND [ChecklistId] IS NULL AND [RfiId] IS NULL) OR ([EntityType] = 'Checklist' AND [ChecklistId] IS NOT NULL AND [InspectionId] IS NULL AND [RfiId] IS NULL) OR ([EntityType] = 'Rfi' AND [RfiId] IS NOT NULL AND [InspectionId] IS NULL AND [ChecklistId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses",
                sql: "([EntityType] = 'Inspection' AND [Status] IN ('Draft','Submitted','UnderReview','Approved','Recheck','Rejected')) OR ([EntityType] = 'Checklist' AND [Status] IN ('Draft','InProgress','Submitted','UnderReview','Approved','Recheck','Rejected')) OR ([EntityType] = 'Rfi' AND [Status] IN ('Draft','Open','Responded','Closed','Cancelled'))");

            migrationBuilder.CreateIndex(
                name: "IX_RfiEntries_CreatedByUserId",
                table: "RfiEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_RfiEntries_RfiId_EntryNumber",
                table: "RfiEntries",
                columns: new[] { "RfiId", "EntryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rfis_ConstructionObjectId_CreatedAt",
                table: "Rfis",
                columns: new[] { "ConstructionObjectId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Rfis_CreatedByUserId_CreatedAt",
                table: "Rfis",
                columns: new[] { "CreatedByUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Rfis_UpdatedByUserId",
                table: "Rfis",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_Rfis_Number",
                table: "Rfis",
                column: "Number",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessProcesses_Rfis_RfiId",
                table: "BusinessProcesses",
                column: "RfiId",
                principalTable: "Rfis",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessHistories_RfiEntries_RfiEntryId",
                table: "ProcessHistories",
                column: "RfiEntryId",
                principalTable: "RfiEntries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [Rfis]) OR " +
                "EXISTS (SELECT 1 FROM [BusinessProcesses] WHERE [RfiId] IS NOT NULL) " +
                "THROW 51000, 'Cannot roll back AddRfis while RFI data exists.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessProcesses_Rfis_RfiId",
                table: "BusinessProcesses");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcessHistories_RfiEntries_RfiEntryId",
                table: "ProcessHistories");

            migrationBuilder.DropTable(
                name: "RfiEntries");

            migrationBuilder.DropTable(
                name: "Rfis");

            migrationBuilder.DropIndex(
                name: "IX_ProcessHistories_RfiEntryId",
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
                name: "UX_BusinessProcesses_RfiId",
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
                name: "RfiEntryId",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "RfiId",
                table: "BusinessProcesses");

            migrationBuilder.DropSequence(
                name: "RfiNumberSequence");

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
        }
    }
}
