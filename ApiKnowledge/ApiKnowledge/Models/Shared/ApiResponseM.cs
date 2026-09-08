using System;
using System.Collections.Generic;

namespace ApiBusiness.Model.Shared
{
    public class ApiResponseModel<T>
    {
        public bool Status { get; set; } = false; //Estado de la transaccion
        public string Message { get; set; } = String.Empty; //Mensaje opcional
        public string Error { get; set; } = string.Empty; //Descripcion del error
        public string StoredProcedure { get; set; } = string.Empty; //Nombre del procedimiento almacenado si aplica
        public Dictionary<string, object>? Parameters { get; set; } //Objeto con los parametros del procedimiento almacenado si aplica
        public T Data { get; set; } //Respuesta del api
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow; //Fecha y hora de la respuesta en formato UTC
        public string Version { get; set; } //Version de la aplicacion
        public DateTimeOffset? ReleaseDate { get; set; } = null; //Fecha de lanzamiento de la version, si se proporciona en la configuracion, en formato UTC
        public string ErrorCode { get; set; } = string.Empty; // Ej: "1-547" o "2" 1 sql, 2 api, 3 no controlado

        //Constructor para que data y configuration sean obligatorios
        public ApiResponseModel(T data, IConfiguration configuration)
        {
            Data = data; //Asignar data a la propiedad
            Version = configuration["Version"] ?? "Desconocida"; // Usar un valor por defecto si no está configurado
            ReleaseDate = ApiResponseModel<T>.GetReleaseDate(configuration); // Usar un valor por defecto si no está configurado
        }
        private static DateTimeOffset? GetReleaseDate(IConfiguration configuration)
        {
            try
            {
                var year = configuration["ReleaseDate:Year"];
                var month = configuration["ReleaseDate:Month"];
                var day = configuration["ReleaseDate:Date"];
                var hour = configuration["ReleaseDate:Hour"];
                var minute = configuration["ReleaseDate:Minute"];

                if (year == null || month == null || day == null || hour == null || minute == null)
                    return null;

                return new DateTimeOffset(
                    int.Parse(year),
                    int.Parse(month),
                    int.Parse(day),
                    int.Parse(hour),
                    int.Parse(minute),
                    0,
                    TimeSpan.Zero // UTC
                );
            }
            catch
            {
                return null;
            }
        }
    }
}
