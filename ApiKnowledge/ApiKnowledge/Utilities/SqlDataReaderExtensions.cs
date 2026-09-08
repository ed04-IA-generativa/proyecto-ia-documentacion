using System.Collections.Concurrent;
using System.Data.SqlClient;

namespace ApiBusiness.Utilidades
{
    public static class SqlDataReaderExtensions
    {

        // Diccionario thread-safe para almacenar los ordinales (posiciones) de las columnas.
        // La clave combina un identificador único del lector actual y el nombre de la columna.
        private static readonly ConcurrentDictionary<string, int> _bulkOrdinalCache = new();

        /// <summary>
        /// Obtiene el valor de una columna optimizado para consultas masivas (Bulk/Reportes).
        /// Utiliza un caché de posiciones numéricas para evitar comparar strings en cada fila.
        /// </summary>
        public static T? GetBulkValue<T>(this SqlDataReader reader, string columnName)
        {
            // Creamos un identificador único para el conjunto de resultados de este reader.
            // Esto evita que SPs diferentes con los mismos nombres de columna colisionen en el caché.
            string cacheKey = $"{reader.GetHashCode()}_{columnName}";

            // Si el índice numérico no está en caché, lo buscamos y guardamos una sola vez
            if (!_bulkOrdinalCache.TryGetValue(cacheKey, out int ordinal))
            {
                try
                {
                    // GetOrdinal busca internamente la posición de la columna de forma nativa
                    ordinal = reader.GetOrdinal(columnName);
                    _bulkOrdinalCache.TryAdd(cacheKey, ordinal);
                }
                catch (IndexOutOfRangeException)
                {
                    // Si la columna no viene en el SELECT del Stored Procedure, guardamos -1
                    _bulkOrdinalCache.TryAdd(cacheKey, -1);
                    return default;
                }
            }

            // Si en la primera fila detectamos que la columna no existe (-1), 
            // las siguientes miles de filas saltarán directo aquí de manera inmediata.
            if (ordinal == -1) return default;

            // Recuperar por índice numérico (GetFieldValue o GetValue) es la forma más rápida en ADO.NET
            if (reader.IsDBNull(ordinal)) return default;

            // Intentamos usar GetFieldValue que evita conversiones de boxing de tipos primitivos (int, decimal, etc.)
            return reader.GetFieldValue<T>(ordinal);
        }

        //Obtener valor de columna por nombre
        public static T? GetValueOrDefault<T>(this SqlDataReader reader, string columnName)
        {
            // Verifica si la columna existe en el SqlDataReader
            if (reader.HasColumn(columnName))
            {
                object value = reader[columnName];
                return value != DBNull.Value ? (T)value : default;
            }

            // Si la columna no existe, devuelve null o el valor por defecto
            return default;
        }

        //Obtener valor de columna por indice
        public static T? GetValueOrDefaulInt<T>(this SqlDataReader reader, int indexColumn)
        {
            // Verifica si el índice de la columna está dentro del rango de columnas
            if (indexColumn >= 0 && indexColumn < reader.FieldCount)
            {
                object value = reader[indexColumn];
                return value != DBNull.Value ? (T)value : default;
            }

            // Si el índice de la columna es inválido, devuelve null o el valor por defecto
            return default;
        }

        // Método auxiliar para verificar si la columna existe en el SqlDataReader
        private static bool HasColumn(this SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
