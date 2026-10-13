using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileSignatureSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "document_signatures",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Direct");

            migrationBuilder.CreateTable(
                name: "mobile_signature_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocumentSignatureId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_signature_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_signature_requests_document_signatures_DocumentSigna~",
                        column: x => x.DocumentSignatureId,
                        principalTable: "document_signatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_signature_requests_document_versions_DocumentId_Docu~",
                        columns: x => new { x.DocumentId, x.DocumentVersionId },
                        principalTable: "document_versions",
                        principalColumns: new[] { "DocumentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_signature_requests_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_signature_requests_users_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_signature_requests_DocumentId",
                table: "mobile_signature_requests",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_signature_requests_DocumentId_DocumentVersionId",
                table: "mobile_signature_requests",
                columns: new[] { "DocumentId", "DocumentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_signature_requests_DocumentSignatureId",
                table: "mobile_signature_requests",
                column: "DocumentSignatureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_signature_requests_DocumentVersionId_ManagerId",
                table: "mobile_signature_requests",
                columns: new[] { "DocumentVersionId", "ManagerId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_signature_requests_ManagerId_Status_CreatedAtUtc",
                table: "mobile_signature_requests",
                columns: new[] { "ManagerId", "Status", "CreatedAtUtc" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_signature_requests");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "document_signatures");
        }
    }
}
