using FurnitureWorkshop.Core;
using Microsoft.EntityFrameworkCore;

namespace FurnitureWorkshop.Data;

public sealed class WorkshopDb(DbContextOptions<WorkshopDb> options) : DbContext(options)
{
    public DbSet<WorkshopUser> Users => Set<WorkshopUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<FurnitureOrder> Orders => Set<FurnitureOrder>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<WorkshopUser>().HasIndex(user => user.Login).IsUnique();
        model.Entity<Customer>().HasIndex(customer => customer.Name);
        model.HasDbFunction(typeof(WorkshopStore).GetMethod(nameof(WorkshopStore.WorkshopContains))!)
            .HasName("workshop_contains").IsBuiltIn();
        model.Entity<WorkshopUser>().HasData(
            new WorkshopUser { Id = 1, Login = "admin", DisplayName = "Администратор", Role = Roles.Admin, PasswordHash = "69c0fcccee9417e21358ec262f48ae8a:cde94befb4f3e3668dd5b95838b40a39e209460c7ef51538faaca28929f427a0" },
            new WorkshopUser { Id = 2, Login = "manager", DisplayName = "Менеджер", Role = Roles.Manager, PasswordHash = "30ea63f9cfa76c645b1c4423a5a0c522:a9408436add257cf15750b8384389b02ba87c2c36c6dc38029fcafaf0d84e2f5" },
            new WorkshopUser { Id = 3, Login = "ivan", DisplayName = "Иван", Role = Roles.Assembler, PasswordHash = "eaec7a7d205844d8045627f8bb34570f:61eb04150c153494239322caec8c5c00e7e55778e67084b315e3a4c1dd912ce7" },
            new WorkshopUser { Id = 4, Login = "pavel", DisplayName = "Павел", Role = Roles.Assembler, PasswordHash = "16681b9132355af2654f5caa184f01f1:1d0d63f48531d35268159d58f01849bd66b3d283bce519152f17509d386f18a7" });
        var order = model.Entity<FurnitureOrder>();
        order.HasIndex(row => new { row.Status, row.CreatedAt });
        order.HasIndex(row => new { row.AssemblerId, row.CreatedAt });
        order.Property(row => row.Version).IsConcurrencyToken();
        var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        order.Property(row => row.CreatedAt).HasConversion(utc);
        order.Property(row => row.CompletedAt).HasConversion(utc);
        model.Entity<Customer>().Property(row => row.RegisteredAt).HasConversion(utc);
        order.HasOne(row => row.Customer).WithMany().HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Restrict);
        order.HasOne(row => row.Assembler).WithMany().HasForeignKey(row => row.AssemblerId).OnDelete(DeleteBehavior.Restrict);
    }
}
