namespace ApiBusiness.Models.Request.Ventas
{
    public class CriterioVentasM
    {
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string? Cliente { get; set; }
        public string? Producto { get; set; }
        public string? TipoCanal { get; set; }
        public int TopN { get; set; } = 500;
    }
}
