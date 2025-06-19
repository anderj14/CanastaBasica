namespace Canasta.Models;

public class IndiceCalculado
{
    public int Id { get; set; }
    public required string Tipo { get; set; } // "Laspeyres", "Paasche", "Fisher"
    public int AnioBase { get; set; }
    public int AnioActual { get; set; }
    public decimal Valor { get; set; }
    public DateTime FechaCalculo { get; set; } = DateTime.UtcNow;
}