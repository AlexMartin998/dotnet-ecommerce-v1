# CI, desactivada a propósito

El pipeline de GitHub Actions **no está activo**: el fichero vive aquí, fuera de
`.github/workflows/`, así que GitHub lo ignora.

## Por qué

Un PAT sin scope `workflow` **no puede empujar cambios a `.github/workflows/`**, y el push
salía rechazado:

```
! [remote rejected] dev -> dev (refusing to allow a Personal Access Token to
  create or update workflow `.github/workflows/ci.yml` without `workflow` scope)
```

La CI la creó una sesión de agente (`ba219f4`) y nunca llegó a correr: el archivo jamás se
ha empujado. Decisión del owner el 2026-09-07: **quitarla de en medio sin perderla**, para
desbloquear el push.

## Cómo se reactiva, el día que se quiera

```sh
mkdir -p .github/workflows
git mv AGENTS/ci/ci.yml.disabled .github/workflows/ci.yml
```

Y hace falta **una** de estas dos cosas, o el push volverá a ser rechazado:

- **remoto por SSH** (`git remote set-url origin git@github.com:…`), que no tiene esa
  restricción — y de paso saca el token del repo; o
- un PAT **con scope `workflow`**.

## Qué hace el pipeline

Build con `-warnaserror` + los 278 tests en cada push y PR sobre `main` y `dev`, con SQL
Server y Redis como `services` del runner, y construye el `Dockerfile` en un job aparte.
Está al día: incluye el arreglo del escalar plegado que impedía arrancar el contenedor de
SQL Server.
