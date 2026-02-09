using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoManager.Migrations
{
    /// <inheritdoc />
    public partial class AddTagColorAndFileSizeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VideoEntries_Duration",
                table: "VideoEntries");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "VideoEntries");

            migrationBuilder.AddColumn<long>(
                name: "DurationTicks",
                table: "VideoEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Tags",
                type: "TEXT",
                maxLength: 9,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoEntries_DurationTicks",
                table: "VideoEntries",
                column: "DurationTicks");

            migrationBuilder.CreateIndex(
                name: "IX_VideoEntries_FileSize",
                table: "VideoEntries",
                column: "FileSize");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VideoEntries_DurationTicks",
                table: "VideoEntries");

            migrationBuilder.DropIndex(
                name: "IX_VideoEntries_FileSize",
                table: "VideoEntries");

            migrationBuilder.DropColumn(
                name: "DurationTicks",
                table: "VideoEntries");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Tags");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "Duration",
                table: "VideoEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.CreateIndex(
                name: "IX_VideoEntries_Duration",
                table: "VideoEntries",
                column: "Duration");
        }
    }
}
