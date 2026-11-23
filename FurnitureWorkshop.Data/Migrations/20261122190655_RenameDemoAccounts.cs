using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureWorkshop.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameDemoAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "69c0fcccee9417e21358ec262f48ae8a:cde94befb4f3e3668dd5b95838b40a39e209460c7ef51538faaca28929f427a0");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "30ea63f9cfa76c645b1c4423a5a0c522:a9408436add257cf15750b8384389b02ba87c2c36c6dc38029fcafaf0d84e2f5");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Иван", "ivan", "eaec7a7d205844d8045627f8bb34570f:61eb04150c153494239322caec8c5c00e7e55778e67084b315e3a4c1dd912ce7" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Павел", "pavel", "16681b9132355af2654f5caa184f01f1:1d0d63f48531d35268159d58f01849bd66b3d283bce519152f17509d386f18a7" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "f9f8827ee3d874bb4ef6ccef88ff8b38:b660d55c888c18000854d1e1c35d065732969d87387660b1d82921d4c1ee1321");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "38a3c4cffdd117e71cc0e82119be7d17:3214f9b1415279fac74a2b83dccc6f4e5c9a2f6825c714d83630ec7ba3a7d81c");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Сборщик 1", "assembler1", "1c188f234ae466bac120eb94eb432b5a:a3d9164b382aeae4281df392a165c61ad021beb582452fbad410e74ac6cd13fd" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Сборщик 2", "assembler2", "409cbaf77c76b05bea4c4642c255d86a:b8cb4495437107c05ca2d878f396270a6450fb9059213160307ac9657d53e3e7" });
        }
    }
}
