namespace ApiBusiness.Models.Request.Conocimiento
{
    public class DocumentoVectorialM
    {
        public string Modulo { get; set; } = string.Empty;
        public string TipoDocumento { get; set; } = string.Empty;
        public string ReferenciaId { get; set; } = string.Empty;
        public string Contenido { get; set; } = string.Empty;
        public float[] Embedding { get; set; } = [];
        public byte? Empresa { get; set; }
        public short? EstacionTrabajo { get; set; }
        public string? UserName { get; set; }
    }
}
