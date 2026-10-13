using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_signatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignatureAlgorithm = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    HashAlgorithm = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SignatureValue = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PublicKeyFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_signatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_signatures_document_versions_DocumentId_DocumentVe~",
                        columns: x => new { x.DocumentId, x.DocumentVersionId },
                        principalTable: "document_versions",
                        principalColumns: new[] { "DocumentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_signatures_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_signatures_users_SignedByUserId",
                        column: x => x.SignedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_signatures_DocumentId",
                table: "document_signatures",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_signatures_DocumentId_DocumentVersionId",
                table: "document_signatures",
                columns: new[] { "DocumentId", "DocumentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_signatures_DocumentVersionId_SignedByUserId",
                table: "document_signatures",
                columns: new[] { "DocumentVersionId", "SignedByUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_signatures_SignedAtUtc",
                table: "document_signatures",
                column: "SignedAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_document_signatures_SignedByUserId",
                table: "document_signatures",
                column: "SignedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_signatures");
        }
    }
}
