namespace ApiBusiness.Models.Response.Conocimiento
{
    public class ResultadoBusquedaVectorialM
    {
        public long DocumentoId { get; set; }
        public string Modulo { get; set; } = string.Empty;
        public string TipoDocumento { get; set; } = string.Empty;
        public string ReferenciaId { get; set; } = string.Empty;
        public string Contenido { get; set; } = string.Empty;
        public double Similitud { get; set; }
    }
}
