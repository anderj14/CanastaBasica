
namespace Canasta.Models
{
    public class Producto
    {
        public int Id { get; set; }
        public required string Nombre { get; set; }
        public required string UnidadMedida { get; set; }
        public string? Categoria { get; set; }
        public List<Precio> Precios { get; set; } = new();
    }
}