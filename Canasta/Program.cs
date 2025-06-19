using System.Text.Json.Serialization;
using Canasta.Data;
using Canasta.Models;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddAntiforgery(options => { options.SuppressXFrameOptionsHeader = true; });

builder.Services.Configure<JsonOptions>(opt =>
{
    opt.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}


app.MapGet("/", () => "Hello World!");

app.MapGet("/productos", async (AppDbContext db) =>
    await db.Productos.Include(p => p.Precios).ToListAsync());

app.MapGet("/productos/{id}", async (int id, AppDbContext db) =>
    await db.Productos
            .Include(p => p.Precios)
            .FirstOrDefaultAsync(p => p.Id == id)
        is Producto producto
        ? Results.Ok(producto)
        : Results.NotFound());

app.MapGet("/precios/{productoId}",
    async (int productoId, AppDbContext db) =>
    {
        await db.Precios.Where(p => p.ProductoId == productoId).ToListAsync();
    });

app.MapPost("/Precios", async (Precio precio, AppDbContext db) =>
{
    db.Precios.Add(precio);
    await db.SaveChangesAsync();
    return Results.Created($"/precios/{precio.Id}", precio);
});

app.MapPost("/indices/Laspeyres/{anioBase}/{anioActual}", async (int anioBase, int anioActual, AppDbContext db) =>
{
    var productos = await db.Productos.Include(p => p.Precios).ToListAsync();

    decimal sumNumerador = 0;
    decimal sumDenominador = 0;

    foreach (var p in productos)
    {
        var precioBase = p.Precios.FirstOrDefault(pr => pr.Anio == anioBase)?.Valor ?? 0;
        var precioActual = p.Precios.FirstOrDefault(pr => pr.Anio == anioActual)?.Valor ?? 0;
        var cantidadBase = p.Precios.FirstOrDefault(pr => pr.Anio == anioBase)?.CantidadConsumida ?? 0;

        sumNumerador += precioActual * cantidadBase;
        sumDenominador += precioBase * cantidadBase;
    }

    var indice = sumDenominador != 0 ? (sumNumerador / sumDenominador) * 100 : 0;

    var calculo = new IndiceCalculado
    {
        Tipo = "Laspeyres",
        AnioBase = anioBase,
        AnioActual = anioActual,
        Valor = indice,
    };

    db.Indices.Add(calculo);
    await db.SaveChangesAsync();

    return Results.Ok(
        new
        {
            Indice = Math.Round(indice, 2),
            Interpretacion = indice > 100 ? "Inflacion" : "Deflacion"
        }
    );
});

app.MapPost("/indices/paasche/{anioBase}/{anioActual}", async (int anioBase, int anioActual, AppDbContext db) =>
{
    var productos = await db.Productos.Include(p => p.Precios).ToListAsync();

    decimal sumNumerador = 0;
    decimal sumDenominador = 0;

    foreach (var p in productos)
    {
        var precioBase = p.Precios.FirstOrDefault(pr => pr.Anio == anioBase)?.Valor ?? 0;
        var precioActual = p.Precios.FirstOrDefault(pr => pr.Anio == anioActual)?.Valor ?? 0;
        var cantidadActual = p.Precios.FirstOrDefault(pr => pr.Anio == anioActual)?.CantidadConsumida ?? 0;

        sumNumerador += precioActual * cantidadActual;
        sumDenominador += precioBase * cantidadActual;
    }

    var indice = sumDenominador != 0 ? (sumNumerador / sumDenominador) * 100 : 0;

    var calculo = new IndiceCalculado
    {
        Tipo = "Paasche",
        AnioBase = anioBase,
        AnioActual = anioActual,
        Valor = indice,
    };

    db.Indices.Add(calculo);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        Indice = Math.Round(indice, 2),
        Interpretacion = indice > 100 ? "Inflacion" : "Deflacion"
    });
});

app.MapPost("/indices/fisher/{anioBase}/{anioActual}", async (int anioBase, int anioActual, AppDbContext db) =>
{
    var laspeyres = await db.Indices
        .FirstOrDefaultAsync(i => i.AnioBase == anioBase && i.AnioActual == anioActual && i.Tipo == "Laspeyres");

    var paasche = await db.Indices
        .FirstOrDefaultAsync(i => i.AnioBase == anioBase && i.AnioActual == anioActual && i.Tipo == "Paasche");

    if (laspeyres == null || paasche == null)
    {
        return Results.BadRequest("Debe calcular primero los índices Laspeyres y Paasche para el mismo periodo.");
    }

    var fisher = Math.Sqrt((double)(laspeyres.Valor * paasche.Valor));

    var calculo = new IndiceCalculado
    {
        Tipo = "Fisher",
        AnioBase = anioBase,
        AnioActual = anioActual,
        Valor = (decimal)fisher,
    };

    db.Indices.Add(calculo);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        Indice = Math.Round((decimal)fisher, 2),
        Interpretacion = fisher > 100 ? "Inflación" : "Deflación"
    });
});

static string GenerarInterpretacion(string tipo, decimal valor)
{
    var cambio = valor - 100;
    var direccion = cambio > 0 ? "aumento" : "disminución";
    var magnitud = Math.Abs(cambio);

    return tipo switch
    {
        "Laspeyres" => $"Los precios agregados ({tipo}) tuvieron un {direccion} del {magnitud:F2}% " +
                       "usando cantidades del año base como ponderación.",
        "Paasche" => $"Los precios agregados ({tipo}) tuvieron un {direccion} del {magnitud:F2}% " +
                     "usando cantidades del año actual como ponderación.",
        "Fisher" => $"El indice {tipo} (media geometrica) muestra un {direccion} del {magnitud:F2}%.",
        _ => "Indice no reconocido"
    };
}

static string ObtenerDetalleCalculo(string tipo)
{
    return tipo switch
    {
        "Laspeyres" => "Formula: ∑(Pₜ × Q₀) / ∑(P₀ × Q₀) × 100",
        "Paasche" => "Formula: ∑(Pₜ × Qₜ) / ∑(P₀ × Qₜ) × 100",
        "Fisher" => "Formula: √(Laspeyres × Paasche)",
        _ => "Formula no disponible"
    };
}

app.MapGet("/indices/comparar/{anioBase}/{anioActual}", async (int anioBase, int anioActual, AppDbContext db) =>
{
    var indices = await db.Indices
        .Where(i => i.AnioBase == anioBase && i.AnioActual == anioActual)
        .ToListAsync();

    return Results.Ok(indices.Select(i => new {
        i.Tipo,
        i.Valor,
        Diferencia = i.Valor - 100,
        Interpretacion = GenerarInterpretacion(i.Tipo, i.Valor)
    }));
});


app.Run();