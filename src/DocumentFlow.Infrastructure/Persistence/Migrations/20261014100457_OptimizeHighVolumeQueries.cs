using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeHighVolumeQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_webhook_endpoints_CreatedAtUtc_Id",
                table: "webhook_endpoints",
                columns: new[] { "CreatedAtUtc", "Id" },
                descending: new bool[0],
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_CreatedAtUtc_Id",
                table: "webhook_deliveries",
                columns: new[] { "CreatedAtUtc", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_Action_CreatedAtUtc_Id",
                table: "audit_logs",
                columns: new[] { "Action", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_UserId_CreatedAtUtc_Id",
                table: "audit_logs",
                columns: new[] { "UserId", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_AssignedToUserId_CreatedAtUtc_Id",
                table: "approval_requests",
                columns: new[] { "AssignedToUserId", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_endpoints_CreatedAtUtc_Id",
                table: "webhook_endpoints");

            migrationBuilder.DropIndex(
                name: "IX_webhook_deliveries_CreatedAtUtc_Id",
                table: "webhook_deliveries");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_Action_CreatedAtUtc_Id",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_UserId_CreatedAtUtc_Id",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_approval_requests_AssignedToUserId_CreatedAtUtc_Id",
                table: "approval_requests");
        }
    }
}
