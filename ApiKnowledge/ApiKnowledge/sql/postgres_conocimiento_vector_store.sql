-- Esquema de Conocimiento_Documento sobre PostgreSQL + pgvector.
-- Reemplaza la version interina que se habia construido sobre SQL Server 2022: aqui
-- el filtro de permisos (modulo/empresa/estacion_trabajo/user_name) y el ranking por
-- similitud de coseno ocurren en la misma consulta, porque pgvector calcula distancia
-- de forma nativa (operador <=>). Ver PostgresVectorStore.cs para el consumo real.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS conocimiento_documento (
    documento_id       BIGSERIAL PRIMARY KEY,
    modulo             VARCHAR(30)   NOT NULL,
    tipo_documento     VARCHAR(30)   NOT NULL,
    referencia_id      VARCHAR(50)   NOT NULL,
    contenido          TEXT          NOT NULL,
    embedding          VECTOR(1536)  NOT NULL,
    empresa            SMALLINT      NULL,
    estacion_trabajo   SMALLINT      NULL,
    user_name          VARCHAR(30)   NULL,
    estado             SMALLINT      NOT NULL DEFAULT 1,
    fecha_hora         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    m_fecha_hora       TIMESTAMPTZ   NULL,

    CONSTRAINT uq_conocimiento_documento_origen UNIQUE (modulo, tipo_documento, referencia_id)
);

-- Filtro de permisos primero: este indice cubre el WHERE de PostgresVectorStore.BuscarAsync
-- antes de que pgvector calcule ninguna distancia.
CREATE INDEX IF NOT EXISTS ix_conocimiento_documento_filtro
    ON conocimiento_documento (modulo, empresa, estacion_trabajo, user_name);
