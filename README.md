# XForge IDE

IDE RAD de última geração (poder Clarion + designer visual estilo Radzen) para criar
aplicações **C#** com ASP.NET Core + Blazor, Clean Architecture, DDD, CQRS, EF Core e
Identity — com bancos SQLite/MySQL/PostgreSQL.

> **Estado atual: Marcos M1, M2 e M3 (núcleo) concluídos e validados E2E** — shell completo,
> projetos em SQLite, Data Designer, Wizard New App (solution compilável) e Editor de Código
> com Monaco. Roadmap em `../docs/ANALISE.md` §7.

## Requisitos

- .NET SDK **10.0** (`dotnet --list-sdks`)
- Node.js 20+ (pipeline Tailwind + Monaco)
- dotnet-ef 10 (`dotnet tool install -g dotnet-ef`) — apenas para novas migrations

## Estrutura

```
ide/
├── src/
│   ├── XForge.Ide.Core/            # Domínio da IDE (entidades, abstrações)
│   ├── XForge.Ide.Application/     # Casos de uso (Project, Dictionary, Scaffolding, FileSystem)
│   ├── XForge.Ide.Infrastructure/  # EF Core SQLite, repositórios, introspecção SQLite, migrations
│   ├── XForge.Ide.Templates/       # Gerador de apps (templates C# tipado — decisão M-13)
│   ├── XForge.Ide.App/             # UI Blazor (XFAppShell, páginas, Monaco, TabBar)
│   └── XForge.Ide.Server/          # Host ASP.NET Core (endpoints, DI, assets)
└── tests/
    └── XForge.Ide.Core.Tests/      # xUnit
```

Dependência de UI: `XForge.DesignSystem` (repo irmão `../design-system`) — **todos os
componentes da interface são `XF*`** (decisão D-08 em `../docs/MAPEAMENTO.md`).

## Como executar (desenvolvimento)

```powershell
# 1. Assets: Monaco embedado + CSS do shell (Tailwind v4)
npm install
npm run build          # = build:monaco + build:css

# 2. IDE
dotnet run --project src/XForge.Ide.Server --urls http://127.0.0.1:5600
```

Abra `http://127.0.0.1:5600/home`.

- O banco da IDE (SQLite) é criado/migrado automaticamente em
  `%LOCALAPPDATA%\XForge\ide\ide.db`.
- Watch do CSS durante o desenvolvimento: `npm run watch:css`.
- `wwwroot/monaco` NUNCA deve ser commitado (está no .gitignore).

## API REST (para Electron/MCP)

| Método | Rota | Descrição |
|---|---|---|
| GET | `/api/projects` | Lista projetos |
| GET | `/api/projects/{id}` | Detalhe |
| POST | `/api/projects` | Cria (`{name, path, templateKind, description}`) |
| POST | `/api/projects/{id}/open` | Abre (atualiza LastOpenedAt) |
| DELETE | `/api/projects/{id}` | Remove |
| POST | `/api/wizards/new-app` | Gera solution Clean Architecture completa |
| GET | `/api/files?path=` | Lista diretório (escopo: workspace dos projetos) |
| GET | `/api/files/content?path=` | Lê arquivo texto |
| PUT | `/api/files/content` | Salva arquivo (`{path, content}`) |

### Wizard de aplicativo (POST /api/wizards/new-app)
```powershell
$body = @{
  projectId="<guid>"; displayName="Fiscal Flow"; outputPath="C:\Projetos"
  provider="Sqlite"   # Sqlite | PostgreSql | MySql | SqlServer
  useIdentity=$true; validateBuild=$true
} | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5600/api/wizards/new-app -Method Post -Body $body -ContentType application/json
```
Retorna `{solutionPath, buildSucceeded, log}`. O projeto é registrado automaticamente na IDE.

## Editor de código (M3)

- Monaco Editor embedado (AMD local, zero CDN): csharp, razor, json, typescript, javascript,
  markdown, html, css, sql, rust, xml, plaintext
- Tema vs-dark/vs-light alternável e persistido; Ctrl+S salva
- Explorer de arquivos do projeto aberto + TabBar com drag, indicador âmbar de não-salvo,
  fechar aba (X / Ctrl+W)
- Projeto atual persistido em localStorage (sobrevive a navegações/circuits)

## Auditoria automática de UI/acessibilidade

Com o servidor rodando (`http://127.0.0.1:5600`), execute:

```powershell
npm run qa:ui
```

Ou deixe o script subir e derrubar o servidor automaticamente:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/qa-gate.ps1
```

O gate:

- navega por todas as 37 rotas Razor;
- falha se houver rota sem `H1`, controle sem nome acessível, ação com nome repetido em tabela, caractere inválido, aba sem `role="tab"`, erro de console ou `pageerror`;
- exercita modais e confirmações com cliques reais via Chrome DevTools Protocol;
- exige confirmação em ações sensíveis, sem executar destrutivos.
- valida foco inicial em `Cancelar` e impede fechamento de confirmações por clique no backdrop.

O mesmo gate está pronto para CI em `.github/workflows/ide-quality.yml`.

## Solução de problemas

| Sintoma | Causa provável | Solução |
|---|---|---|
| Shell sem estilos (só texto) | `ide-app.css` não gerado | `npm run build:css` |
| Monaco não carrega | `wwwroot/monaco/vs` ausente | `npm run build:monaco` |
| Erro NU1008 no restore | PackageReference com Version + CPM | Remova a `Version=` local; versões vivem em `Directory.Packages.props` |
| 404 nas páginas | Falta `AddAdditionalAssemblies` em `MapRazorComponents` | Verifique `Program.cs` |
| Build trava (MSB3021/3027) | Servidor/processo dotnet segurando DLL | Mate processos `dotnet` órfãos (PIDs XForge/5600) |
| Banco travado ("database is locked") | Processo antigo da IDE | Encerre processos órfãos e apague `%LOCALAPPDATA%\XForge\ide\ide.db*` |
| `data-theme` não alterna | Render mode não-global | `<Routes @rendermode="InteractiveServer">` no `App.razor` |
| Explorer vazio | Projeto não restaurado | Abra o projeto em **Projetos** (persiste em localStorage) |
| Picker mostra “retornou apenas um nome” | File System Access API não expõe caminho absoluto no navegador | Digite o caminho completo manualmente (ex.: `E:\Projetos\MeuSistema`) |

## Documentação do projeto

- `../docs/00-OBJETIVO.md` — missão, stack e decisões
- `../docs/ANALISE.md` — arquitetura alvo e roadmap M1–M7
- `../docs/MAPEAMENTO.md` — decisões de mapeamento (D-08, M-13..M-19)
- `../docs/PARIDADE.md` — matriz de cobertura funcional
