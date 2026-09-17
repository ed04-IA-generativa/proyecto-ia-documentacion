# Publicar ApiKnowledge en IIS (para consumirla desde otras máquinas de la red)

Esta guía deja `ApiKnowledge` corriendo como un sitio de **IIS** (Internet Information
Services) en vez de con `dotnet run` — con IIS, la API queda arriba de forma estable (no
depende de que alguien deje una terminal abierta) y cualquier otra máquina en la misma
red local puede consumirla usando la **IP de la máquina que la hostea**, sin necesidad de
Docker ni de que n8n corra en la misma computadora.

**Está dividida en dos partes**, porque la primera vez es bastante más trabajo que las
siguientes:

- **Configuración inicial** (secciones 1 a 7) — se hace **una sola vez** por máquina.
- **Actualizar la API** (última sección) — el día a día, cada vez que hay un cambio nuevo
  para publicar.

## Requisitos

- Windows 10/11 con permisos de administrador en la máquina.
- El proyecto `ApiKnowledge` compilando sin errores (`dotnet build`) antes de publicar.
- Estar en la misma red (LAN/Wi-Fi) que la máquina que hostea la API, para poder
  consumirla por IP desde otro equipo.

## 1. Habilitar IIS en Windows

Panel de control → Programas → **Activar o desactivar las características de Windows**
→ marcar **Internet Information Services** y, dentro de su árbol, al menos:

- Herramientas de administración web → **Consola de administración de IIS**
- Servicios World Wide Web → Características HTTP comunes → **Contenido estático**,
  **Documento predeterminado**, **Errores HTTP**
- Servicios World Wide Web → Características de mantenimiento y diagnóstico →
  **Registro HTTP** (recomendado, ayuda a diagnosticar errores más adelante)
- Servicios World Wide Web → Seguridad → **Filtrado de solicitudes**

Equivalente por PowerShell (como administrador), si se prefiere no ir clic por clic:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-StaticContent, IIS-DefaultDocument, IIS-ManagementConsole -All
```

Puede pedir reiniciar. Cuando termine, **IIS Manager** (buscar "Administrador de Internet
Information Services" en el menú de inicio, o `inetmgr` desde Ejecutar) ya debería abrir.

## 2. Instalar el ASP.NET Core Hosting Bundle

**Este paso es la parte que más se olvida la primera vez.** IIS por sí solo no sabe correr
una aplicación de ASP.NET Core — el *Hosting Bundle* instala el módulo (`ASP.NET Core
Module`, ANCM) que IIS necesita para poder recibir una petición HTTP y pasársela al
proceso de la API, más el runtime de .NET que esa API necesita para ejecutarse.

1. Descargarlo de la página oficial de descargas de .NET (`dotnet.microsoft.com`, sección
   **.NET 8.0** → **ASP.NET Core Runtime** → **Hosting Bundle**). Tiene que ser la misma
   versión mayor de .NET que usa `ApiKnowledge` (.NET 8).
2. Correr el instalador (`dotnet-hosting-8.x.x-win.exe`).
3. **Reiniciar IIS** para que reconozca el módulo nuevo — no basta con que el instalador
   termine:

```powershell
net stop was /y
net start w3svc
```

   Si es la primera vez que se instala en esa máquina, es más seguro reiniciar la
   computadora completa en vez de solo reiniciar el servicio.

**Cómo confirmar que quedó instalado**: en IIS Manager, seleccionar el nodo del servidor
(el nombre de la máquina, arriba del árbol) → doble clic en **Módulos** → debe aparecer
`AspNetCoreModuleV2` en la lista.

## 3. Crear el Application Pool

En IIS Manager, panel izquierdo → **Grupos de aplicaciones** (Application Pools) → clic
derecho → **Agregar grupo de aplicaciones...**

- **Nombre**: uno que identifique la API (ej. `ApiKnowledgePool`).
- **.NET CLR version**: **Sin código administrado** (`No Managed Code`).
  ⚠️ Este es el otro detalle que se pasa por alto la primera vez — aunque `ApiKnowledge`
  es una app .NET, no corre sobre el CLR clásico que IIS gestiona directamente; el
  ASP.NET Core Module (instalado en el paso 2) es quien arranca y administra el proceso
  de la API por su cuenta. Dejar la versión de CLR clásica ahí no ayuda y puede causar
  errores al iniciar el sitio.
- **Modo de canalización administrada**: Integrado (el valor por defecto, no hay que
  tocarlo).

## 4. Publicar el proyecto

Con Visual Studio (clic derecho sobre el proyecto `ApiKnowledge` → **Publicar**), o por
línea de comandos:

```powershell
dotnet publish "C:\ruta\a\ApiKnowledge\ApiKnowledge.csproj" -c Release -o "C:\inetpub\wwwroot\host\<NombreCliente>\ED04\Apis"
```

**Sobre la carpeta de destino**: la convención usada es
`C:\inetpub\wwwroot\host\<NombreCliente>\<CodigoEquipo>\Apis\`:

- `host` — carpeta raíz para todas las APIs publicadas en esta máquina (los nombres de
  carpeta bajo IIS no distinguen mayúsculas/minúsculas, así que `host` o `Host` da igual).
- `<NombreCliente>` — cada cliente/instalación tiene su propia subcarpeta (igual que cada
  cliente tiene su propia base de datos física en SQL Server, ver
  `documentacion/SINCRONIZACION_SQLSERVER_POSTGRES.md`).
- `<CodigoEquipo>` — **no** es el nombre de la máquina, es el código interno del equipo de
  desarrollo dueño de esa publicación (ej. `GD02` = Grupo Desarrollo 02, `ED04` = Equipo
  Desarrollo 04). **Este proyecto usa `ED04`.** Sirve para que varios equipos puedan
  publicar APIs distintas del mismo cliente en el mismo servidor sin pisarse.
- `Apis` — adentro de esa carpeta van **directo** los archivos que deja el `publish`
  (el `.dll` principal, `web.config`, `appsettings.json`, etc.), sin subcarpetas extra.

No hace falta crear la carpeta a mano — tanto `dotnet publish` como el Publish de Visual
Studio la crean solos si no existe.

**Antes de continuar**: revisar que `appsettings.json` tenga las cadenas de conexión reales de esa instalación — ver la sección
de configuración en `documentacion/ApiKnowledge-Guia-Implementacion.md`. IIS no usa
`appsettings.Development.json`.

## 5. Convertir la carpeta publicada en una Aplicación de IIS

**No se crea un sitio nuevo ni se le asigna un puerto propio.** Todas las APIs de esta
máquina cuelgan del mismo sitio — el **Default Web Site** que IIS deja creado por
defecto, escuchando en el puerto **80** (el puerto HTTP estándar, por eso no aparece en
las URLs) — cada una diferenciada por su ruta dentro del árbol de carpetas, no por el
puerto. Por eso el consumo final se ve así, **sin puerto**:

```
http://<IP-de-la-máquina>/host/<NombreCliente>/ED04/Apis/api/status
```

Pasos:

1. Confirmar que **Default Web Site** existe y escucha en el puerto 80: IIS Manager →
   **Sitios → Default Web Site** → panel derecho → **Enlaces...** (Bindings) → debe
   aparecer un enlace `http` en el puerto `80`. Si no existe ningún sitio ahí, crear uno
   (**Sitios** → clic derecho → **Agregar sitio web...**) con nombre `Default Web Site`,
   ruta física `C:\inetpub\wwwroot`, tipo `http`, puerto `80`.
2. En el árbol de la izquierda, expandir **Default Web Site** y navegar hasta la carpeta
   `Apis` (la que quedó del `publish` en el paso 4, dentro de `host\<NombreCliente>\ED04\`)
   — IIS la muestra sola porque físicamente vive dentro de `C:\inetpub\wwwroot`. Si no
   aparece todavía, clic derecho sobre **Default Web Site** → **Actualizar**.
3. Clic derecho sobre la carpeta `Apis` → **Convertir en aplicación** (Convert to
   Application).
4. **Grupo de aplicaciones**: seleccionar el creado en el paso 3 (no dejar el que propone
   IIS por defecto) → **Aceptar**.

Esto es lo que hace que esa carpeta deje de ser "solo archivos" y pase a ser un proceso
real de ASP.NET Core que IIS arranca y administra — el ícono de la carpeta cambia (a uno
con un engranaje) cuando quedó bien convertida. Cada API nueva de este mismo cliente o de
otro repite este mismo paso 5 sobre su propia carpeta `Apis`, todas bajo el mismo sitio y
el mismo puerto 80, sin pisarse porque cada una vive en su propia ruta.

**Por qué HTTP y no HTTPS con certificado**: al correr con `dotnet run --launch-profile
https` (ver `documentacion/N8N-Workflow-IA-Generativa.md`), la API usa el certificado de
desarrollo autofirmado de ASP.NET Core (`dotnet dev-certs`) — ese certificado **no es
válido para otras máquinas** de la red, solo para `localhost` de la máquina donde se
generó. Publicar en IIS con HTTP simple evita ese problema por completo para consumo
dentro de la red local; exponerlo con HTTPS de verdad requeriría un certificado real
(de una CA, o auto-firmado e instalado manualmente como confiable en cada máquina
cliente), lo cual queda fuera del alcance de esta guía de desarrollo/red local.

## 6. Abrir el puerto en el Firewall de Windows

Si otras máquinas de la red no logran conectarse (aunque desde la misma máquina sí
responde en `localhost`), casi siempre es el Firewall de Windows bloqueando el puerto
para conexiones entrantes desde otros equipos.

Como es el puerto 80 (el estándar de HTTP), Windows suele traer ya habilitado un grupo de
reglas predefinido llamado **"World Wide Web Services (HTTP)"** apenas se instala el rol
de IIS — conviene revisar eso primero:

```powershell
Get-NetFirewallRule -DisplayGroup "World Wide Web Services (HTTP)" | Select-Object DisplayName, Enabled, Direction
```

Si aparece deshabilitada, o no aparece nada, crear la regla a mano. Por PowerShell, como
administrador:

```powershell
New-NetFirewallRule -DisplayName "ApiKnowledge IIS" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow
```

O por interfaz gráfica: **Firewall de Windows Defender con seguridad avanzada** → **Reglas
de entrada** → **Nueva regla...** → **Puerto** → TCP, puerto específico (`80`) →
**Permitir la conexión** → marcar los tres perfiles (Dominio, Privado, Público) o solo
los que apliquen → ponerle un nombre (ej. `ApiKnowledge IIS`).

Si el perfil de red de esa máquina ya está configurado como "Red privada" y con reglas
generales de IIS habilitadas, puede que ya conecte sin hacer nada de esto — pero si falla
desde otra máquina, este es el primer lugar a revisar.

## 7. Averiguar la IP de la máquina y probar desde otro equipo

En la máquina que hostea la API:

```powershell
ipconfig
```

Buscar la línea **Dirección IPv4** del adaptador de red activo (ej. `192.168.10.18`).

Desde la **misma** máquina, probar primero con `localhost` (sin puerto — es el 80 por
defecto):

```
http://localhost/host/<NombreCliente>/ED04/Apis/api/status
```

Desde **otra** máquina conectada a la misma red (otra laptop, o el contenedor de n8n si
corriera en otra máquina física):

```
http://192.168.10.18/host/<NombreCliente>/ED04/Apis/api/status
```

(sustituir `192.168.10.18` por la IP real de la máquina que hostea la API, y
`<NombreCliente>` por el cliente correspondiente). Si responde, la API ya es consumible
por cualquiera en la misma red usando esa ruta en vez de `localhost`.

**Errores comunes al probar:**

- **500.19** — error de configuración; casi siempre significa que el Hosting Bundle
  (paso 2) no quedó instalado o no se reinició IIS después de instalarlo.
- **502.5 / "ANCM failed to start"** — el proceso de la API no arrancó; revisar que la
  carpeta de publish tenga todos los archivos, y que `appsettings.json` tenga cadenas de
  conexión válidas (un error de conexión a SQL Server/Postgres en el arranque también se
  ve así desde IIS).
- **No conecta desde otra máquina, pero sí desde `localhost`** — volver al paso 6
  (Firewall).

## Actualizar la API (lo que se hace normalmente, después de la primera vez)

Una vez que el sitio, el grupo de aplicaciones y el Firewall ya están configurados, subir
un cambio nuevo es mucho más simple:

1. **Detener la Aplicación (o su grupo de aplicaciones) en IIS Manager** — clic derecho
   sobre la carpeta `Apis` convertida en aplicación (o sobre su Application Pool en
   **Grupos de aplicaciones**) → **Detener**. Es necesario: mientras está corriendo, IIS
   mantiene los `.dll` de la carpeta de publish bloqueados y no se pueden reemplazar.
2. **Publicar de nuevo** (Visual Studio → Publicar, o el mismo comando `dotnet publish`
   del paso 4) a una carpeta aparte, o directo si el perfil de publish ya apunta a la
   carpeta de IIS.
3. **Reemplazar los archivos**: borrar los que ya estaban en
   `...\host\<NombreCliente>\ED04\Apis` y copiar los nuevos del publish.
4. **Volver a iniciar la Aplicación** (o su Application Pool) en IIS Manager.
5. Probar de nuevo `http://localhost/host/<NombreCliente>/ED04/Apis/api/status` (o el
   endpoint real) antes de avisar que ya está actualizado.

No hace falta repetir nada de las secciones 1 a 6 — esas son de una sola vez por máquina.
