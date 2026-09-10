# Instalación de n8n (Docker, local)

n8n corre en un contenedor separado del de Postgres, con un volumen nombrado para no perder workflows/credenciales entre reinicios del contenedor.

## Levantar el contenedor

```bash
docker run -d --name n8n -p 5678:5678 -v n8n_data:/home/node/.n8n docker.n8n.io/n8nio/n8n
```

- Se accede en `http://localhost:5678`.
- El volumen `n8n_data` guarda toda la configuración de n8n (workflows, credenciales, historial de ejecuciones). Si el contenedor se borra y se vuelve a crear con el mismo nombre de volumen (`n8n_data`), no se pierde nada.
- `host.docker.internal` (la forma en que los workflows de n8n le hablan a ApiKnowledge corriendo en el host — ver `documentacion/N8N-Workflow-IA-Generativa.md`) funciona automáticamente en Docker Desktop (Windows/Mac). En Linux hace falta agregar `--add-host=host.docker.internal:host-gateway` al comando de arriba.

## Verificar que está arriba

```bash
docker ps --filter "name=n8n"
```

Si el contenedor existe pero está detenido (`Exited`), se levanta de nuevo sin perder nada con:

```bash
docker start n8n
```
