using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DocumentAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "document_chunks",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('english', content)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_search_vector",
                table: "document_chunks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_document_chunks_search_vector",
                table: "document_chunks");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "document_chunks");
        }
    }
}
