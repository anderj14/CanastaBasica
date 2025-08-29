using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Canasta.Models
{
    public class ProductoGrafico
    {
        public string Nombre { get; set; }
        public string Categoria { get; set; }
        public List<ValorAnual> Valores { get; set; }
    }
}