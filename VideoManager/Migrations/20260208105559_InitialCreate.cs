using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoManager.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FolderCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolderCategories_FolderCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "FolderCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Codec = table.Column<string>(type: "TEXT", nullable: true),
                    Bitrate = table.Column<long>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoCategory",
                columns: table => new
                {
                    CategoriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideosId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoCategory", x => new { x.CategoriesId, x.VideosId });
                    table.ForeignKey(
                        name: "FK_VideoCategory_FolderCategories_CategoriesId",
                        column: x => x.CategoriesId,
                        principalTable: "FolderCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoCategory_VideoEntries_VideosId",
                        column: x => x.VideosId,
                        principalTable: "VideoEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoTag",
                columns: table => new
                {
                    TagsId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideosId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoTag", x => new { x.TagsId, x.VideosId });
                    table.ForeignKey(
                        name: "FK_VideoTag_Tags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoTag_VideoEntries_VideosId",
                        column: x => x.VideosId,
                        principalTable: "VideoEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderCategories_ParentId",
                table: "FolderCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoCategory_VideosId",
                table: "VideoCategory",
                column: "VideosId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoEntries_Duration",
                table: "VideoEntries",
                column: "Duration");

            migrationBuilder.CreateIndex(
                name: "IX_VideoEntries_ImportedAt",
                table: "VideoEntries",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VideoEntries_Title",
                table: "VideoEntries",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_VideoTag_VideosId",
                table: "VideoTag",
                column: "VideosId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoCategory");

            migrationBuilder.DropTable(
                name: "VideoTag");

            migrationBuilder.DropTable(
                name: "FolderCategories");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "VideoEntries");
        }
    }
}
