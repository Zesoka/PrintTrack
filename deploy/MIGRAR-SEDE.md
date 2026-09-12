# Cómo sumar una sede al Auditor de impresiones

Checklist para dar de alta una sede nueva. La carga, modificación y revisión de los datos la hace
el administrador — esto es la guía de pasos, no un script automático.

Tiempo estimado por sede: **15-30 minutos** (bastante menos que cargar impresoras una por una a mano).

---

## 0. Antes de arrancar

- [ ] **Conocés el rango de IPs de las impresoras de esa sede** (ej. `192.168.40.0/24`). Si no lo
      sabés, pedíselo a la persona de la sede o mirá la config del switch/DHCP.
- [ ] **Probá que desde el servidor (Sede Central) llegás a esa red.** Por SSH al CT:

  ```bash
  ping -c 2 192.168.40.4            # una IP conocida de esa sede
  nmap -p 161,631,80,443 192.168.40.4
  ```

  Si `ping` no responde: es un tema de ruteo VPN (FortiGate), hay que resolverlo *antes* de seguir
  — avisá a Redes. Si `ping` anda pero `nmap` no muestra ningún puerto abierto, puede ser que el
  firewall de esa sede filtre SNMP/IPP entre sedes aunque deje pasar la web — también es tema de
  Redes, no del Auditor.

---

## 1. Catálogos → creá la Sede (si no existe)

**Catálogos** → agregá el nombre de la Sede. Usá el mismo nombre que después vas a usar para el
SedeAdmin/SedeViewer de esa sede, para no confundirte.

Si en esta sede también vas a segmentar por Área/Departamento y alguien se va a ocupar de mantenerlo
al día, cargalas ahora. Si nadie las va a tocar, no te compliques — quedan vacías y no molestan.

---

## 2. Descubrir → escaneo de esa sede

**Descubrir** → CIDR de la sede (ej. `192.168.40.0/24`) → community `public` (salvo que uses otra)
→ Sede por defecto: la que acabás de crear → **Escanear**.

Revisá la lista que trae:

- [ ] **¿Los que aparecen son realmente impresoras?** Algunas IPs pueden responder SNMP sin ser
      impresoras (switches, otros equipos de red). Si el campo "Descripción" no dice nada de HP/
      LaserJet/etc., desmarcala antes de agregar.
- [ ] **Nombre a usar**: ajustalo a la convención que estés usando (ej. "Sede - Ubicación", como
      "Isla Facturación", "Isla Color"). El hostname SNMP (tipo `NPIA86EB1`) no dice nada por sí
      solo — ponele un nombre que la persona de esa sede reconozca.
- [ ] **Sede**: confirmá que quedó la correcta en cada fila (por si escaneaste un rango que toca
      más de una sede).

**Agregar seleccionadas.** Esto crea, para cada una: la impresora, su config de Contadores (SNMP)
y su config de Job Log — las tres de una sola vez, con la IP ya cargada.

> Si algún dispositivo no respondió SNMP (impresora apagada, en modo reposo profundo, o con SNMP
> deshabilitado), no va a aparecer en el escaneo. Agregala a mano después en **Impresoras**, o
> reintentá el escaneo más tarde.

---

## 3. Primera verificación — Job Log / IPP

Por cada impresora nueva (o simplemente esperá al próximo ciclo automático, cada
`PrintTrack:JobLog:PollMinutes` — 20 min por defecto):

**Job Log** → **"Traer ahora"** en cada una. Fijate el resultado:

| Lo que ves | Qué significa | Qué hacer |
|---|---|---|
| "OK" o "OK (vía IPP)", trajo trabajos | Anduvo — auditoría automática funcionando | Nada, listo |
| "Job Log no disponible (pide login)... importado por IPP: N trabajo(s)" | El Job Log web está bloqueado pero **IPP lo cubrió igual** | Nada, es normal en varios modelos — revisá que el N no sea 0 |
| "...importado por IPP: 0 trabajo(s)" | Ni Job Log ni IPP dieron datos por ahora | Probá **"Probar IPP (páginas)"** en esa config — si tampoco trae nada, puede ser un modelo viejo sin esa función (ver tabla de abajo) |
| Pide login y no hay fallback por IPP | El equipo exige credenciales para ver hasta el Job Log | Si tenés usuario/clave del EWS de esa impresora, cargalo en "Credenciales" dentro de la misma config |

**Modelos viejos (LaserJet P2055dn, M3035, CM2320nf y similares):** no vas a tener ni Job Log ni
IPP con datos reales — es una limitación del equipo, no del sistema. Para esos, el dato disponible
es el contador SNMP total (paso siguiente).

---

## 4. Primera verificación — Contadores (SNMP)

**Contadores** → confirmá que cada impresora nueva muestra un **Total** (páginas) distinto de "—".
Si alguna da error:

- Probá **"Probar OID"** desde su Config para ver el error puntual.
- Confirmá que el OID por defecto (`prtMarkerLifeCount.1.1`) funciona en ese modelo — si no, HP a
  veces usa otro; buscá el OID correcto para ese modelo puntual.
- Confirmá la community SNMP — la mayoría de la flota usa `public`, pero alguna sede puede tener
  otra configurada.

---

## 5. Creá el administrador de esa sede

**Administradores** → **Nuevo administrador**:

- Usuario y contraseña para la persona responsable de esa sede.
- Rol: **SedeAdmin** si va a poder editar (agregar impresoras, tocar configs), o **SedeViewer** si
  solo necesita consultar/auditar sin tocar nada.
- Tildá la Sede que acabás de crear (y solo esa, salvo que le corresponda ver más de una).

Mandale el usuario/clave por un canal seguro — no quedan visibles para vos después de crearlas.

---

## 6. Verificación final

- [ ] **Trabajos**, filtrado por esa Sede: aparecen filas con usuario/documento/fecha real.
- [ ] **Panel**: si algo no respondió, debería aparecer en "⚠️ Impresoras que necesitan atención"
      pasadas ~24 h — no hace falta que lo revises a mano todo el tiempo, el panel avisa solo.
- [ ] Le diste acceso al responsable de la sede y probó entrar.

Listo, sede migrada. Repetí para la siguiente.

---

## Notas generales (aprendido migrando las primeras impresoras)

- **No todas las impresoras dan el mismo nivel de detalle.** Es normal tener una mezcla: algunas
  con páginas reales por trabajo (FutureSmart + IPP), otras solo con usuario/documento sin páginas
  (Job Log clásico), y las más viejas solo con el total por SNMP. No es un bug, es lo que cada
  equipo expone por red.
- **"Guardar y traer ahora" da 0 trabajos nuevos** la mayoría de las veces que lo tocás manualmente
  — es esperable si el sondeo automático (cada 20 min) ya se adelantó y no hay nada nuevo desde
  entonces. No es error.
- Para forzar que **más impresoras tengan páginas reales**, el camino es que las PCs impriman por
  IPP en vez de puerto TCP/IP crudo — ver `Convert-PrinterToIpp.ps1` en esta misma carpeta.
