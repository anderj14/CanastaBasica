
namespace Canasta.Models
{
    public class Precio
    {
        public int Id { get; set; }
        public int Anio { get; set; }
        public decimal Valor { get; set; }
        public int CantidadConsumida { get; set; }
        public int ProductoId { get; set; }
        public Producto? Producto { get; set; }
    }
}