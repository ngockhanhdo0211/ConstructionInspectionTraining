using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionInspection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionWorkflow : Migration
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

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses");

            migrationBuilder.AddColumn<Guid>(
                name: "NewAssignedToUserId",
                table: "ProcessHistories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousAssignedToUserId",
                table: "ProcessHistories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextRevisionNumber",
                table: "Inspections",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [Inspections] SET [NextRevisionNumber] = 2 WHERE [NextRevisionNumber] IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "NextRevisionNumber",
                table: "Inspections",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_NewAssignedToUserId",
                table: "ProcessHistories",
                column: "NewAssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_PreviousAssignedToUserId",
                table: "ProcessHistories",
                column: "PreviousAssignedToUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories",
                sql: "[Action] IN ('Create','Submit','StartReview','Approve','Reject','RequestRecheck','ReassignReviewer','CreateRevision')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories",
                sql: "[ToStatus] IN ('Draft','Submitted','UnderReview','Approved','Recheck','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Inspections_NextRevisionNumber",
                table: "Inspections",
                sql: "[NextRevisionNumber] >= 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses",
                sql: "[Status] IN ('Draft','Submitted','UnderReview','Approved','Recheck','Rejected')");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessHistories_AspNetUsers_NewAssignedToUserId",
                table: "ProcessHistories",
                column: "NewAssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessHistories_AspNetUsers_PreviousAssignedToUserId",
                table: "ProcessHistories",
                column: "PreviousAssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcessHistories_AspNetUsers_NewAssignedToUserId",
                table: "ProcessHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcessHistories_AspNetUsers_PreviousAssignedToUserId",
                table: "ProcessHistories");

            migrationBuilder.DropIndex(
                name: "IX_ProcessHistories_NewAssignedToUserId",
                table: "ProcessHistories");

            migrationBuilder.DropIndex(
                name: "IX_ProcessHistories_PreviousAssignedToUserId",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Inspections_NextRevisionNumber",
                table: "Inspections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses");

            migrationBuilder.DropColumn(
                name: "NewAssignedToUserId",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "PreviousAssignedToUserId",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "NextRevisionNumber",
                table: "Inspections");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_Action",
                table: "ProcessHistories",
                sql: "[Action] = 'Create'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessHistories_ToStatus",
                table: "ProcessHistories",
                sql: "[ToStatus] = 'Draft'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessProcesses_Status",
                table: "BusinessProcesses",
                sql: "[Status] = 'Draft'");
        }
    }
}
