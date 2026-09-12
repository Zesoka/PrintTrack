# Primer despliegue en un contenedor nuevo (CT)

Para levantar el Auditor de impresiones en el CT de Sede Central (o cualquier host nuevo) desde
cero. Una vez que esto esté arriba, seguí con `MIGRAR-SEDE.md` para cargar cada sede.

## 1. El CT

- Debian 12, LXC.
- **Features**: Nesting ✅, keyctl ✅ (Docker adentro de LXC los necesita).
- 2-4 cores, 2-4 GB RAM (+1 GB swap si vas a compilar ahí mismo), 20-30 GB disco.
- Con ruta real a las 11 sedes (verificalo antes de seguir — ver `MIGRAR-SEDE.md` paso 0).

## 2. Docker

```bash
apt update && apt install -y curl git
curl -fsSL https://get.docker.com | sh
systemctl enable --now docker
docker compose version
```

## 3. Traer el código

```bash
git clone https://github.com/Zesoka/PrintTrack.git pt
cd pt
```

## 4. Levantar

```bash
docker compose --profile full up -d --build
```

Primera vez tarda 2-4 min (baja el SDK de .NET y compila). Si el CT tiene poca RAM y se cuelga,
armá la imagen en otra máquina y usá `docker-compose.prebuilt.yml` (instrucciones en ese mismo
archivo) en vez de compilar ahí.

Confirmá que levantó:

```bash
for i in $(seq 1 20); do c=$(curl -s -o /dev/null -w '%{http_code}' http://localhost:8095/healthz); [ "$c" = "200" ] && { echo "OK"; break; }; sleep 2; done
```

## 5. Primer login

`http://<IP-del-CT>:8095` → usuario `admin`, clave `ChangeMe!123`.

**Cambiala apenas entrés** — es la clave semilla, la misma en todos los despliegues de prueba que
hicimos hasta ahora. (Todavía no hay pantalla de "cambiar mi contraseña" para el propio usuario —
si la necesitás, avisame y la agrego. Mientras tanto: **Administradores** → editá `admin` → cargale
una contraseña nueva ahí.)

## 6. Seguí con las sedes

A partir de acá, `MIGRAR-SEDE.md` — una sede a la vez.

## Actualizar más adelante

Cada vez que yo te pase un fix o mejora nueva:

```bash
cd pt
git pull
docker compose --profile full up -d --build
```

Esto **no borra datos** (la base vive en un volumen aparte). Para un reset total (borra todo y
arranca de cero):

```bash
docker compose --profile full down -v
docker compose --profile full up -d --build
```
