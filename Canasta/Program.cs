using System.Text.Json.Serialization;
using Canasta.Data;
using Canasta.Models;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

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


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Habilitar Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Canasta API V1");
    c.RoutePrefix = "swagger"; // Swagger UI en /swagger
});


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}


app.MapGet("/", () => "Hello World!");

app.MapGet("/productos", async (AppDbContext db) =>
    await db.Productos.Include(p => p.Precios).ToListAsync())
    .WithName("ObtenerProductos")
    .WithTags("Productos")
    .WithSummary("Obtiene todos los productos con sus precios asociados");


app.MapGet("/productos/{id}", async (int id, AppDbContext db) =>
    await db.Productos
            .Include(p => p.Precios)
            .FirstOrDefaultAsync(p => p.Id == id)
        is Producto producto
        ? Results.Ok(producto)
        : Results.NotFound())
    .WithName("ObtenerProductoPorId")
    .WithTags("Productos")
    .WithSummary("Obtiene un producto específico por su Id, incluyendo sus precios.");


app.MapGet("/precios/{productoId}",
    async (int productoId, AppDbContext db) =>
    {
        await db.Precios.Where(p => p.ProductoId == productoId).ToListAsync();
    })
    .WithName("ObtenerPreciosPorProducto")
    .WithTags("Precios")
    .WithSummary("Obtiene todos los precios asociados a un producto específico.");

app.MapPost("/Precios", async (Precio precio, AppDbContext db) =>
{
    db.Precios.Add(precio);
    await db.SaveChangesAsync();
    return Results.Created($"/precios/{precio.Id}", precio);
})
.WithName("CrearPrecio")
.WithTags("Precios")
.WithSummary("Crea un nuevo precio para un producto.");

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
})
.WithName("CalcularIndiceLaspeyres")
.WithTags("Indices")
.WithSummary("Calcula el índice de Laspeyres para los años indicados.");

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
})
.WithName("CalcularIndicePaasche")
.WithTags("Indices")
.WithSummary("Calcula el índice de Paasche para los años indicados.");

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
})
.WithName("CalcularIndiceFisher")
.WithTags("Indices")
.WithSummary("Calcula el índice de Fisher basado en los índices Laspeyres y Paasche existentes.");


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

    return Results.Ok(indices.Select(i => new
    {
        i.Tipo,
        i.Valor,
        Diferencia = i.Valor - 100,
        Interpretacion = GenerarInterpretacion(i.Tipo, i.Valor)
    }));
})
.WithName("CompararIndices")
.WithTags("Indices")
.WithSummary("Obtiene todos los índices calculados para el periodo indicado, con interpretación.");

app.MapGet("/graficos/productos-precios-grafico", async (AppDbContext db) =>
{
    var productosDb = await db.Productos
        .Include(p => p.Precios)
        .ToListAsync();

    var productos = productosDb.Select(p => new ProductoGrafico
    {
        Nombre = p.Nombre,
        Categoria = p.Categoria,
        Valores = p.Precios
                    .OrderBy(pr => pr.Anio)
                    .Select(pr => new ValorAnual { Anio = pr.Anio, Valor = pr.Valor })
                    .ToList()
    }).ToList();

    // Crear el gráfico
    var plot = CrearGraficoProductos(productos);

    // Guardar como PNG
    var outputPath = "ProductosPrecios.png";
    using (var stream = File.Create(outputPath))
    {
        var exporter = new OxyPlot.SkiaSharp.PngExporter { Width = 800, Height = 600 };
        exporter.Export(plot, stream);
    }

    return Results.Ok($"Gráfico generado en {outputPath}");
})
.WithName("GraficarProductosPrecios")
.WithTags("Graficos")
.WithSummary("Genera un gráfico de evolución de precios por producto y lo guarda como PNG.");

static PlotModel CrearGraficoProductos(List<ProductoGrafico> productos)
{
    var plotModel = new PlotModel { Title = "Evolución de Precios por Producto" };
    plotModel.Background = OxyColors.White;

    foreach (var producto in productos)
    {
        var series = new LineSeries { Title = producto.Nombre, MarkerType = MarkerType.Circle };
        foreach (var valor in producto.Valores)
        {
            series.Points.Add(new DataPoint(valor.Anio, (double)valor.Valor));
        }
        plotModel.Series.Add(series);
    }

    return plotModel;
}

app.MapGet("/graficos/indices/{anioBase}/{anioActual}", async (int anioBase, int anioActual, AppDbContext db) =>
{
    var indicesDb = await db.Indices
        .Where(i => i.AnioBase == anioBase && i.AnioActual == anioActual)
        .ToListAsync();

    var indicesGrafico = indicesDb.Select(i => new IndiceGrafico
    {
        Tipo = i.Tipo,
        Valor = i.Valor
    }).ToList();

    var plot = CrearGraficoIndices(indicesGrafico);

    // Guardar como PNG
    var outputPath = $"Indices_{anioBase}_{anioActual}.png";
    using (var stream = File.Create(outputPath))
    {
        var exporter = new OxyPlot.SkiaSharp.PngExporter { Width = 800, Height = 600 };
        exporter.Export(plot, stream);
    }

    return Results.Ok($"Gráfico de índices generado en {outputPath}");
})
.WithName("GraficarIndices")
.WithTags("Graficos")
.WithSummary("Genera un gráfico comparativo de los índices calculados y lo guarda como PNG.");

static PlotModel CrearGraficoIndices(List<IndiceGrafico> indices)
{
    var plotModel = new PlotModel { Title = "Comparación de Índices" };
    plotModel.Background = OxyColors.White;

    int x = 0; // eje X inicial
    foreach (var indice in indices)
    {
        x++;
        var yInicio = 100;
        var yFinal = (double)indice.Valor;

        var series = new LineSeries
        {
            Title = indice.Tipo,
            MarkerType = MarkerType.Circle,
            StrokeThickness = 2
        };

        // Línea desde 100 hasta el valor del índice
        series.Points.Add(new DataPoint(0, yInicio));
        series.Points.Add(new DataPoint(x, yFinal));

        plotModel.Series.Add(series);

        // Calcular punto medio
        var midX = (0 + x) / 2.0;
        var midY = (yInicio + yFinal) / 2.0;

        // Agregar texto en el medio de la línea
        var label = new TextAnnotation
        {
            Text = $"{indice.Tipo}: {Math.Round(indice.Valor, 2)}",
            TextPosition = new DataPoint(midX, midY),
            Stroke = OxyColors.Transparent,
            FontWeight = OxyPlot.FontWeights.Bold
        };

        plotModel.Annotations.Add(label);
    }

    plotModel.Axes.Add(new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Left, Minimum = 0 });
    plotModel.Axes.Add(new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, Minimum = 0 });

    return plotModel;
}


app.Run();