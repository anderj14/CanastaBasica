using Canasta.Models;
using Microsoft.EntityFrameworkCore;

namespace Canasta.Data;

public class AppDbContext: DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Precio> Precios => Set<Precio>();
    public DbSet<IndiceCalculado> Indices => Set<IndiceCalculado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Producto>()
            .HasMany(p => p.Precios)
            .WithOne(p => p.Producto)
            .HasForeignKey(p => p.ProductoId);
        
        modelBuilder.Entity<Precio>(entity =>
        {
            entity.Property(p => p.Valor)
                .HasPrecision(18, 4);
        });
        
        modelBuilder.Entity<IndiceCalculado>(entity =>
        {
            entity.Property(p => p.Valor)
                .HasPrecision(18, 4);
        });
    }
}