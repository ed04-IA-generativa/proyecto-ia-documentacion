namespace ApiBusiness.Models.Request.Conocimiento
{
    public class CriterioBusquedaVectorialM
    {
        public float[] Embedding { get; set; } = [];
        public string? Modulo { get; set; }
        public byte? Empresa { get; set; }
        public short? EstacionTrabajo { get; set; }
        public string? UserName { get; set; }
        public int TopN { get; set; } = 5;
    }
}
