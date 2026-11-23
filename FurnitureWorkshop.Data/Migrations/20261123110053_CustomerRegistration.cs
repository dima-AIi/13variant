using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureWorkshop.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAt",
                table: "Customers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                table: "Customers");
        }
    }
}
