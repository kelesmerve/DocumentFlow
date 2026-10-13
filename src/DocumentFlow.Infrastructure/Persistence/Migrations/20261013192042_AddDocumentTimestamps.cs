using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_timestamps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentSignatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimestampToken = table.Column<string>(type: "text", nullable: false),
                    TimestampAuthority = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MessageImprintAlgorithm = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MessageImprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    GenTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_timestamps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_timestamps_document_signatures_DocumentSignatureId",
                        column: x => x.DocumentSignatureId,
                        principalTable: "document_signatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_timestamps_DocumentSignatureId",
                table: "document_timestamps",
                column: "DocumentSignatureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_timestamps_GenTimeUtc",
                table: "document_timestamps",
                column: "GenTimeUtc",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_timestamps");
        }
    }
}
