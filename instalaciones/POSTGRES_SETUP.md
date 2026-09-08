# Levantar PostgreSQL + pgvector (desarrollo local)

Esta guía deja el camino más simple: Docker Desktop + un archivo `docker-compose.yml` (su
contenido está en el paso 3), usando la imagen oficial `pgvector/pgvector`, que trae la
extensión ya compilada (evita compilar pgvector a mano en Windows, que es la parte
complicada de instalarlo nativo).

## Requisitos para instalar Docker

- Windows 10 de 64 bits versión 2004 o superior (build 19041+), o Windows 11.
- Permisos de administrador en la máquina (para habilitar WSL2 e instalar Docker Desktop).
- Virtualización de hardware habilitada en el BIOS/UEFI (Intel VT-x / AMD-V) — es lo que usan WSL2 y Docker por debajo para correr contenedores. Ver la sección siguiente para activarla y comprobarla.
- Al menos 4 GB de RAM libres (Docker Desktop corre una máquina virtual ligera de Linux). Si en la misma máquina vas a tener varios servicios pesados corriendo a la vez dentro de Docker (por ejemplo SQL Server, Python/Jupyter, PostgreSQL, Redis, etc.), con 16 GB de RAM totales conviene vigilar cuántos de esos servicios corren al mismo tiempo — puede empezar a quedarse corto.
- Unos 2-4 GB de espacio en disco libres, para Docker Desktop más la imagen de Postgres/pgvector.
- Conexión a internet, para descargar WSL2, Ubuntu, Docker Desktop y la imagen de pgvector.

### Activar la virtualización en el BIOS (si hace falta)

Docker y WSL2 necesitan la virtualización de hardware habilitada en el BIOS/UEFI. Si no está
activada, Docker Desktop no llega a arrancar.

**Paso 1 — Entrar al BIOS y activarla**

1. Reinicia la computadora.
2. En cuanto aparezca el logo del fabricante, presiona repetidamente la tecla para entrar al BIOS/Setup. Varía según la marca:
   - HP: `F10`
   - Dell: `F2`
   - Lenovo: `F1` o `F2`
   - Otra marca: revisa el mensaje que aparece al encender (algo como "Press ... to enter Setup"); suele ser `F2`, `F10`, `Esc` o `Del`.
3. Busca una opción relacionada con virtualización, normalmente llamada `Virtualization Technology (VTx)`, `Intel VT-x` o `AMD-V` / `SVM Mode`.
4. Cámbiala a `Enabled`.
5. Guarda los cambios y reinicia (usualmente `F10` dentro del BIOS para guardar y salir).

Ejemplo concreto (equipo HP ProDesk 600 G2 MT): reiniciar, presionar `F10` repetidamente al ver el logo de HP, entrar a `Virtualization Technology (VTx)`, ponerlo en `[Enabled]`, guardar y reiniciar.

**Paso 2 — Comprobar que quedó habilitada**

Con Windows ya iniciado de nuevo, abre PowerShell y ejecuta:

```powershell
systeminfo
```

Busca la sección `Requisitos Hyper-V` al final del resultado. Debe mostrar estas cuatro líneas en `Sí`:

- Extensiones de modo de monitor de VM: Sí
- Virtualización habilitada en firmware: Sí
- Traducción de direcciones de segundo nivel: Sí
- Prevención de ejecución de datos disponible: Sí

Cuando las cuatro digan "Sí", la máquina ya tiene la virtualización que necesita Docker. Si en
vez de esas cuatro líneas aparece el mensaje "Se detectó un hipervisor. No se mostrarán las
características necesarias para Hyper-V", también es buena señal: significa que la
virtualización ya está activa y en uso (por ejemplo porque WSL2 ya está instalado y corriendo).

## 1. Habilitar WSL2 (si no lo tienes)

Docker Desktop en Windows lo necesita. Desde PowerShell como administrador:

```powershell
wsl --install
```

Reinicia la máquina cuando termine. Si `wsl --status` ya responde con una versión en vez de
decir que no está instalado, puedes saltarte este paso.

Escribe un nombre de usuario en minúsculas (ya pusiste dell, está bien, presiona Enter).
Te va a pedir una contraseña — escríbela (no se ve nada en pantalla mientras escribes, es normal en Linux) y Enter.
Te la va a pedir de nuevo para confirmar

Cuando termine, deberías ver una línea de comandos tipo nombredeusuario@...:~$ — eso confirma que Ubuntu ya quedó listo dentro de WSL.

## 2. Instalar Docker Desktop
dell@GD02-01:/mnt/c/Windows/system32$
```powershell
winget install Docker.DockerDesktop
```

Ábrelo una vez desde el menú de inicio y espera a que el ícono/estado diga que ya está
corriendo (la primera vez puede pedir aceptar términos o reiniciar de nuevo).

## 3. Levantar el contenedor de Postgres

Crea una carpeta en tu máquina para este propósito (el nombre y la ubicación no importan,
por ejemplo `C:\postgres-pgvector\`) y dentro de ella un archivo llamado `docker-compose.yml`
con este contenido:

```yaml
services:
  postgres-conocimiento:                    # Nombre del servicio/contenedor. Es un ejemplo, ponle el que quieras.
    image: pgvector/pgvector:pg16           # Imagen oficial: Postgres 16 con pgvector ya instalado adentro.
    environment:
      POSTGRES_DB: nombredb             # Nombre de la base de datos a crear. Ejemplo, configúralo a tu gusto.
      POSTGRES_USER: usuariodb           # Usuario dueño de esa base. Ejemplo, configúralo a tu gusto.
      POSTGRES_PASSWORD: contrasena_user   # Contraseña de ese usuario. Ejemplo para desarrollo local.
    ports:
      - "5432:5432"                        # "puerto_en_tu_maquina:puerto_dentro_del_contenedor"
    volumes:
      - conocimiento_pgdata:/var/lib/postgresql/data   # Ver explicación de "volumes" más abajo.

volumes:
  conocimiento_pgdata:                      # Declara el volumen usado arriba, para que Docker lo cree y reutilice.
```

**Sobre `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD`**: los valores de este ejemplo
(`nombredb` / `usuariodb` / `contrasena_user`) son solo para probar en tu máquina local — son
configurables, cada quien debería poner los propios. En particular, `POSTGRES_PASSWORD` con
un valor tan simple no debe usarse fuera de una máquina de desarrollo personal.

**Sobre el puerto `5432`**: es el puerto estándar en el que Postgres escucha por convención
(cualquier cliente de Postgres — `psql`, pgAdmin, una API — lo prueba por defecto ahí). Con
`docker-compose` esto solo se publica hacia tu propia máquina (`localhost`), así que sirve
para pruebas locales sin exponer nada a la red. Si esto se fuera a correr en un servidor real
compartido, ese puerto necesitaría reglas de firewall/red propias, no basta con lo de aquí.

**Sobre `volumes`**: por defecto, si borras o recreas un contenedor de Docker, se pierde todo
lo que tenía adentro — incluyendo los datos de la base. Un *volumen* es un espacio de
almacenamiento que Docker mantiene aparte del contenedor, para que sobreviva aunque el
contenedor se borre y se vuelva a crear. La línea `conocimiento_pgdata:/var/lib/postgresql/data`
le dice a Docker: "todo lo que Postgres escriba en su carpeta de datos interna
(`/var/lib/postgresql/data`, donde siempre guarda sus archivos) consérvalo en el volumen
`conocimiento_pgdata`". Sin esa línea, cada `docker compose down` borraría toda la información
guardada.

Parado en esa carpeta (la que tenga el `docker-compose.yml`):

```powershell
docker compose up -d
```

Verificar que quedó corriendo:

```powershell
docker ps
```

Debe aparecer un contenedor `postgres-conocimiento` con el puerto `5432` publicado.

## 4. Comprobar que pgvector quedó disponible

```powershell
docker compose exec postgres-conocimiento psql -U usuariodb -d nombredb -c "CREATE EXTENSION IF NOT EXISTS vector;" -c "\dx"
```

`vector` debe aparecer en el listado de extensiones. Con esto Postgres ya tiene soporte de
tipos y búsquedas vectoriales listo para usarse.

## 5. (Opcional) Interfaz visual para ver la base de datos: pgAdmin

Postgres no trae una interfaz gráfica propia (como SQL Server Management Studio) — para eso
se instala un programa aparte. **pgAdmin** es el oficial del proyecto Postgres.

Instalarlo (en cualquier carpeta, `winget install` no depende de dónde lo corras):

```powershell
winget install PostgreSQL.pgAdmin
```

**Crear la conexión (una sola vez):**

1. Abre pgAdmin. La primera vez pide crear una "contraseña maestra" — es solo para proteger pgAdmin en tu máquina, pon la que quieras y recuérdala.
2. En el panel izquierdo, clic derecho sobre **Servers** → **Register → Server...**
3. Pestaña **General**: en `Name` pon cualquier nombre para identificarlo (ej. `Postgres Local`).
4. Pestaña **Connection**, con los mismos datos del `docker-compose.yml`:
   - Host name/address: `localhost`
   - Port: `5432`
   - Maintenance database: `nombredb`
   - Username: `usuariodb`
   - Password: `contrasena_user`
   - (opcional: marcar "Save password" para no escribirla cada vez)
5. Clic en **Save**. Debería conectar de inmediato si el contenedor ya está corriendo.

**Ver las tablas:**

En el árbol de la izquierda, navega: `[nombre que le pusiste] → Databases → nombredb →
Schemas → public → Tables`. Ahí aparecen las tablas que existan. Clic derecho sobre una →
**View/Edit Data → All Rows** para verla como tabla, o usar el ícono de **Query Tool** (arriba)
para correr consultas SQL directas.

Con eso ya se pueden ver las tablas, sus datos y correr consultas de forma visual, sin usar la
terminal.

## Comandos útiles

- Apagar el contenedor sin borrar datos: `docker compose stop`
- Volver a levantarlo: `docker compose start`
- Borrar todo (contenedor + datos) para empezar de cero: `docker compose down -v`
